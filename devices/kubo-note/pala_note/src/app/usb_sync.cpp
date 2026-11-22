#include "usb_sync.h"
#include "provider.h"
#include "usb_data_frame.h"

namespace {
constexpr const char* kPrefix = "KUBO_USB_V1 ";

void sendSerial(const String& frame) {
  Serial.write(reinterpret_cast<const uint8_t*>(frame.c_str()), frame.length());
}

BluetoothSyncReply sendReply = sendSerial;

void reply(const String& message) {
  // A leading delimiter lets the host recover from a previously truncated frame.
  const String frame = String("\n") + kPrefix + message + "\n";
  if (sendReply == sendSerial && message.startsWith("DATA ")) {
    writeUsbDataFrame(Serial, reinterpret_cast<const uint8_t*>(frame.c_str()), frame.length());
  } else {
    sendReply(frame);
  }
}
}

#if PALA_BRIDGE_DISCOVERY
#include "note_config.h"
#include "note_peers.h"
#include "../../config.h"
#include "../../globals.h"
#include "../../types.h"
#include "notes.h"
#include "date_validation.h"
#include "home_weather.h"
#include "home_delivery.h"
#include "home_holidays.h"
#include "holiday_calendar.h"
#include "home_bridge_status.h"
#include "rtc.h"
#include "settings.h"
#include "storage_commit.h"
#include <SD_MMC.h>
#include <esp_random.h>
#include <mbedtls/md.h>
#include <algorithm>
#include <climits>

namespace {
constexpr uint32_t kChallengeMs = 6000;
constexpr uint32_t kSessionMs = 180000;
constexpr size_t kMaximumWav = 25000000;
constexpr size_t kMaximumText = 32000;
constexpr size_t kMaximumRead = 1024;
constexpr size_t kMaximumWrite = 256;

String hostNonce;
String deviceNonce;
String sessionPeer;
String sessionToken;
uint32_t activityAt = 0;
enum class Link { Usb, Bluetooth };
Link owner = Link::Usb;
bool authenticated = false;
bool homeChanged = false;
bool receivingWeather = false;
bool readingWeather = false;
bool bluetoothDrained = false;

File download;
int selectedNote = 0;
size_t downloadBytes = 0;
size_t sentBytes = 0;
String lastReadHex;
size_t textBytes = 0;
String textDigest;
String transcript;

String notePath(int number, const char* extension) {
  char path[64];
  snprintf(path, sizeof(path), "%s/note_%03d.%s", NOTES_DIR, number, extension);
  return String(path);
}

void resetTransfer() {
  if (download) download.close();
  selectedNote = 0;
  downloadBytes = 0;
  sentBytes = 0;
  lastReadHex = String();
  textBytes = 0;
  receivingWeather = false;
  readingWeather = false;
  textDigest = "";
  transcript = String();
}

void resetSession() {
  resetTransfer();
  hostNonce = "";
  deviceNonce = "";
  sessionPeer = "";
  sessionToken = "";
  authenticated = false;
}

bool decimal(const String& value, uint32_t maximum, uint32_t& result) {
  if (!value.length() || value.length() > 10) return false;
  result = 0;
  for (size_t pos = 0; pos < value.length(); pos++) {
    const char c = value[pos];
    if (c < '0' || c > '9') return false;
    const uint32_t digit = static_cast<uint32_t>(c - '0');
    if (result > maximum / 10 ||
        (result == maximum / 10 && digit > maximum % 10)) return false;
    result = result * 10 + digit;
  }
  return true;
}

int hexDigit(char c) {
  if (c >= '0' && c <= '9') return c - '0';
  if (c >= 'a' && c <= 'f') return c - 'a' + 10;
  return -1;
}

bool validHex(const String& text, size_t length) {
  if (text.length() != length) return false;
  for (size_t i = 0; i < length; i++) {
    if (hexDigit(text[i]) < 0) return false;
  }
  return true;
}

String hex(const uint8_t* data, size_t length) {
  static constexpr char digits[] = "0123456789abcdef";
  String result;
  if (!result.reserve(length * 2)) return result;
  for (size_t i = 0; i < length; i++) {
    result += digits[data[i] >> 4];
    result += digits[data[i] & 0x0f];
  }
  return result;
}

bool sameDigest(const String& expected, const String& received) {
  if (expected.length() != 64 || received.length() != 64) return false;
  unsigned int differences = 0;
  for (size_t index = 0; index < 64; index++) {
    differences |= static_cast<unsigned char>(expected[index] ^ received[index]);
  }
  return differences == 0;
}

String proof(const String& role) {
  const String message = String(kPrefix) + role + " " + hostNonce + " " + deviceNonce;
  const auto* algorithm = mbedtls_md_info_from_type(MBEDTLS_MD_SHA256);
  const auto* token = reinterpret_cast<const unsigned char*>(sessionToken.c_str());
  uint8_t digest[32];
  if (!algorithm || sessionToken.isEmpty() || mbedtls_md_hmac(algorithm, token, sessionToken.length(),
        reinterpret_cast<const unsigned char*>(message.c_str()), message.length(), digest) != 0) return "";
  return hex(digest, sizeof(digest));
}

bool transcriptDigestMatches() {
  const auto* algorithm = mbedtls_md_info_from_type(MBEDTLS_MD_SHA256);
  uint8_t digest[32];
  if (!algorithm || mbedtls_md(algorithm,
        reinterpret_cast<const unsigned char*>(transcript.c_str()), transcript.length(), digest) != 0) return false;
  return sameDigest(hex(digest, sizeof(digest)), textDigest);
}

const NoteEntry* findIndexedNote(const String& value) {
  uint32_t number = 0;
  if (!decimal(value, INT_MAX, number) || number == 0) return nullptr;
  const auto note = std::find_if(noteIndex.begin(), noteIndex.end(),
      [number](const NoteEntry& item) { return item.num == static_cast<int>(number); });
  return note != noteIndex.end() ? &*note : nullptr;
}

File openCommittedReadOnly(const String& path) {
  if (SD_MMC.exists(path.c_str())) return SD_MMC.open(path.c_str(), FILE_READ);
  // The backup is the last committed version after an interrupted rename.
  const String backup = path + ".bak";
  if (SD_MMC.exists(backup.c_str())) return SD_MMC.open(backup.c_str(), FILE_READ);
  return File();
}

String captureTimestamp(int note) {
  File meta = openCommittedReadOnly(noteMetaPath(note));
  if (!meta || meta.isDirectory() || meta.size() > 1024) return "-";
  static constexpr char key[] = "created_utc=";
  while (meta.available()) {
    String line = meta.readStringUntil('\n');
    line.trim();
    if (line.startsWith(key)) {
      const String utc = line.substring(sizeof(key) - 1);
      return validUtcTimestamp(utc.c_str()) ? utc : String("-");
    }
  }
  return "-";
}

bool selectNote(const NoteEntry& note) {
  // Detect text committed before an interrupted index update.
  if (note.hasText || noteHasCommittedTranscript(note.num)) return false;
  const String path = notePath(note.num, "wav");
  download = SD_MMC.open(path.c_str(), FILE_READ);
  if (!download) return false;
  if (download.isDirectory() || download.size() <= 44 || download.size() > kMaximumWav) {
    download.close();
    return false;
  }
  selectedNote = note.num;
  downloadBytes = download.size();
  reply(String("NOTE ") + selectedNote + " " + downloadBytes);
  return true;
}

void selectRecording(bool next, const String& value) {
  resetTransfer();
  if (next) {
    if (recordingSyncAllowed()) {
      for (auto note = noteIndex.rbegin(); note != noteIndex.rend(); ++note) {
        if (notePendingTranscription(*note) && selectNote(*note)) return;
      }
    }
    if (owner == Link::Bluetooth) {
      bluetoothDrained = true;
      reply("EMPTY");
    } else {
      reply("EMPTY RECHECK");
    }
    return;
  }
  const NoteEntry* note = findIndexedNote(value);
  if (!note || !selectNote(*note)) reply("ERROR NOTE");
}

void exportTranscript(const String& value) {
  resetTransfer();
  const NoteEntry* note = findIndexedNote(value);
  if (note) download = openCommittedReadOnly(notePath(note->num, "txt"));
  if (!download || download.isDirectory() || !download.size() || download.size() > kMaximumText) {
    resetTransfer();
    reply("ERROR NOTE");
    return;
  }
  downloadBytes = download.size();
  reply(String("TRANSCRIPT ") + note->num + " " + downloadBytes + " " + captureTimestamp(note->num));
}

void readDownload(const String& first, const String& second, bool readingText) {
  uint32_t offset = 0;
  uint32_t length = 0;
  const bool matchesSelection = readingText ? !selectedNote : selectedNote != 0;
  if (!matchesSelection || !decimal(first, readingText ? kMaximumText : kMaximumWav, offset) ||
      !decimal(second, kMaximumRead, length) || length == 0) {
    reply("ERROR READ");
    return;
  }
  // Replay the last block even if it was the final read and the file is closed.
  if (offset + length == sentBytes && lastReadHex.length() == length * 2) {
    reply(String("DATA ") + offset + " " + lastReadHex);
    return;
  }
  if (!download || offset != sentBytes || length > downloadBytes - sentBytes) {
    reply("ERROR READ");
    return;
  }
  uint8_t data[kMaximumRead];
  if (download.read(data, length) != length) {
    resetTransfer();
    reply("ERROR READ");
    return;
  }
  lastReadHex = hex(data, length);
  if (lastReadHex.length() != length * 2) {
    resetTransfer();
    reply("ERROR READ");
    return;
  }
  sentBytes += length;
  if (sentBytes == downloadBytes) download.close();
  reply(String("DATA ") + offset + " " + lastReadHex);
}

void beginPayload(const String& size, const String& checksum, bool weather) {
  uint32_t length = 0;
  const bool validState = weather ? !selectedNote && !downloadBytes
                                  : selectedNote && sentBytes == downloadBytes;
  if (!validState || textBytes || !decimal(size, weather ? kHomeForecastMaxBytes : kMaximumText, length) ||
      !length || !validHex(checksum, 64) || !transcript.reserve(length)) {
    reply(weather ? "ERROR WEATHER" : "ERROR TEXT");
    return;
  }
  textBytes = length;
  textDigest = checksum;
  receivingWeather = weather;
  reply("OK");
}

void appendTranscript(const String& first, const String& data) {
  uint32_t offset = 0;
  const size_t bytes = data.length() / 2;
  if (!textBytes || !decimal(first, kMaximumText, offset) || offset != transcript.length() ||
      data.length() % 2 || !bytes || bytes > kMaximumWrite || bytes > textBytes - offset ||
      !validHex(data, data.length())) {
    reply("ERROR TEXT");
    return;
  }
  char decoded[kMaximumWrite];
  for (size_t i = 0; i < bytes; i++) {
    const char byte = static_cast<char>((hexDigit(data[2 * i]) << 4) | hexDigit(data[2 * i + 1]));
    if (!byte) { reply("ERROR TEXT"); return; }
    decoded[i] = byte;
  }
  reply(transcript.concat(decoded, bytes) ? "OK" : "ERROR TEXT");
}

void commitTranscript(bool organizeCalendar) {
  if (!selectedNote || receivingWeather || !textBytes ||
      transcript.length() != textBytes || !transcriptDigestMatches()) {
    reply("ERROR TEXT");
    return;
  }
  const int note = selectedNote;
  // Calendar commands still need a proposal from the paired Wi-Fi Bridge.
  // Persist that route before the text so a reboot cannot skip organization.
  const char* route = organizeCalendar ? "organizing" : "usb-local";
  if (noteHasCommittedTranscript(note) || !writeNoteQueueState(note, route, nullptr, false, true) ||
      !saveNoteTranscript(note, transcript)) {
    reply("ERROR SAVE");
    resetTransfer();
    return;
  }
  homeChanged = true;
  reply(String("SAVED ") + note);
  resetTransfer();
}

void commitWeather() {
  if (!receivingWeather || !textBytes || transcript.length() != textBytes || !transcriptDigestMatches()) {
    reply("ERROR WEATHER");
    return;
  }
  const auto saved = storeHomeForecast(transcript);
  resetTransfer();
  if (saved == HomeForecastWrite::Rejected) {
    reply("ERROR WEATHER");
    return;
  }
  if (saved == HomeForecastWrite::Updated) homeChanged = true;
  reply("OK");
}

void readWeather() {
  resetTransfer();
  download = openCommittedReadOnly(String(kHomeForecastPath));
  if (!download || download.isDirectory() || !download.size() || download.size() > kHomeForecastMaxBytes) {
    resetTransfer();
    reply("ERROR WEATHER");
    return;
  }
  downloadBytes = download.size();
  readingWeather = true;
  const String local = utcToLocalDeviceLabel(currentUtcIso());
  const String date = local.substring(0, 10);
  HomeForecast current{};
  const String prefix = String("FORECAST ") + downloadBytes + " ";
  if (!homeForecastFor(date, current)) {
    reply(prefix + "-");
    return;
  }
  reply(prefix + date + " " + current.minimum + " " + current.maximum + " " + current.code);
}

void storeNoteName(const String& encoded, const String& signature) {
  if (palaNoteNameLocallyManaged()) { reply("ERROR COMMAND"); return; }
  if (encoded.length() < 2 || encoded.length() > 2 * kPalaNoteNameMax || encoded.length() % 2 ||
      !validHex(encoded, encoded.length()) || !sameDigest(proof(String("name ") + encoded), signature)) {
    reply("ERROR NAME");
    return;
  }

  String name;
  for (size_t i = 0; i < encoded.length(); i += 2) {
    const char c = (hexDigit(encoded[i]) << 4) | hexDigit(encoded[i + 1]);
    if (!c) { reply("ERROR NAME"); return; }
    name += c;
  }
  const bool changed = palaNoteName() != name;
  if (!palaSetNoteName(name)) { reply("ERROR NAME"); return; }
  homeChanged |= changed;
  reply(String("NAME-SAVED ") + encoded + " " + proof(String("name-saved ") + encoded));
}

void storeDeliveryCounts(const String& payload, const String& signature) {
  bool changed = false;
  if (payload.length() > 72 || !sameDigest(proof(String("delivery ") + payload), signature) ||
      !storeHomeDelivery(payload, changed)) {
    reply("ERROR DELIVERY");
    return;
  }
  homeChanged |= changed;
  reply(String("DELIVERY-SAVED ") + payload + " " + proof(String("delivery-saved ") + payload));
}

void storeHolidayCalendar(const String& payload, const String& signature) {
  bool changed = false;
  if (payload.length() > kHolidaySnapshotMaxBytes ||
      !sameDigest(proof(String("holidays ") + payload), signature) ||
      !storeHomeHolidays(payload, changed)) {
    reply("ERROR HOLIDAYS");
    return;
  }
  homeChanged |= changed;
  reply(String("HOLIDAYS-SAVED ") + proof(String("holidays-saved ") + payload));
}

void expireSession() {
  if (!hostNonce.isEmpty() &&
      ((authenticated && millis() - activityAt >= kSessionMs) ||
       notePeerById(sessionPeer).token != sessionToken)) resetSession();
}

void handleCommand(const String& command, const String& first, const String& second,
                   bool idle, Link link, const String& peer) {
  if (!idle) { reply("ERROR BUSY"); return; }
  expireSession();
  if (hostNonce.length() && owner != link) {
    if (!authenticated && (millis() - activityAt >= kChallengeMs ||
                           (link == Link::Usb && command == "HELLO"))) resetSession();
    else { reply("ERROR BUSY"); return; }
  }
  if (command == "HELLO") {
    if (authenticated) { reply("ERROR BUSY"); return; }
    resetSession();
    const String id = second.isEmpty() ? peer : second;
    const NotePeer selected = notePeerById(id);
    if (!validHex(first, 32) || selected.token.isEmpty() || (link == Link::Bluetooth && id != peer)) {
      reply("ERROR AUTH");
      return;
    }
    sessionPeer = selected.id;
    sessionToken = selected.token;
    hostNonce = first;
    owner = link;
    uint8_t nonce[16];
    if (!noteConfigRandom(nonce, sizeof(nonce))) {
      resetSession();
      reply("ERROR AUTH");
      return;
    }
    deviceNonce = hex(nonce, sizeof(nonce));
    const String digest = proof("device");
    if (deviceNonce.length() != 32 || digest.length() != 64) {
      resetSession();
      reply("ERROR AUTH");
      return;
    }
    activityAt = millis();
    reply(String("CHALLENGE ") + deviceNonce + " " + digest);
    return;
  }
  if (command == "AUTH" && !authenticated) {
    if (hostNonce.length() && millis() - activityAt < kChallengeMs && !second.length() &&
        validHex(first, 64) && sameDigest(proof("host"), first)) {
      authenticated = true;
      homeBridgeStatus.verified(link == Link::Usb ? HomeBridgeLink::Usb : HomeBridgeLink::Bluetooth);
      activityAt = millis();
      reply("OK");
    } else {
      resetSession();
      reply("ERROR AUTH");
    }
    return;
  }
  if (!authenticated) { reply("ERROR AUTH"); return; }
  activityAt = millis();
  if (command == "NEXT" && !first.length()) selectRecording(true, first);
  else if (command == "SELECT" && !second.length()) selectRecording(false, first);
  else if (command == "META" && !first.length() && !second.length()) {
    if (selectedNote) reply(String("META ") + captureTimestamp(selectedNote));
    else reply("ERROR NOTE");
  }
  else if (command == "EXPORT" && !second.length()) exportTranscript(first);
  else if (command == "READ") readDownload(first, second, false);
  else if (command == "READTXT" && !readingWeather) readDownload(first, second, true);
  else if (command == "TEXT") beginPayload(first, second, false);
  else if (command == "PUT") appendTranscript(first, second);
  else if (command == "COMMIT" && !second.length() && (!first.length() || first == "CALENDAR"))
    commitTranscript(first == "CALENDAR");
  else if (command == "NAME") storeNoteName(first, second);
  else if (command == "DELIVERY") storeDeliveryCounts(first, second);
  else if (command == "HOLIDAYS") storeHolidayCalendar(first, second);
  else if (command == "WEATHER") beginPayload(first, second, true);
  else if (command == "WEATHER-COMMIT" && !first.length()) commitWeather();
  else if (command == "WEATHER-READ" && !first.length()) readWeather();
  else if (command == "READWEATHER" && readingWeather) readDownload(first, second, true);
  else if (command == "CLOSE" && !first.length()) { resetSession(); reply("OK"); }
  else reply("ERROR COMMAND");
}
}

bool syncActive(Link link) {
  expireSession();
  return authenticated && owner == link;
}

bool usbSyncActive() { return syncActive(Link::Usb); }
bool usbSyncWorking() { return usbSyncActive() && (downloadBytes > 0 || receivingWeather); }
bool bluetoothSyncActive() { return syncActive(Link::Bluetooth); }
bool takeBluetoothSyncDrained() {
  const bool drained = bluetoothDrained;
  bluetoothDrained = false;
  return drained;
}
void cancelUsbSync() { if (owner == Link::Usb) resetSession(); }
void cancelBluetoothSync() { if (owner == Link::Bluetooth) resetSession(); }

bool takeSyncHomeChanged() {
  const bool changed = homeChanged;
  homeChanged = false;
  return changed;
}

static bool handleLine(const String& line, bool idle, Link link, const String& peer, BluetoothSyncReply send) {
  if (line.startsWith(kNoteConfigPrefix)) {
    const bool authorized = syncActive(link) && (link == Link::Usb || sessionPeer == peer);
    const String oldName = palaNoteName();
    const String oldLanguage = palaUiLanguage();
    const bool oldWeeklyCodexLimit = palaPrefersWeeklyCodexLimit();
    const String response = idle
        ? noteConfigRequest(line.substring(sizeof(kNoteConfigPrefix) - 1),
             link == Link::Usb ? NoteConfigTransport::Usb : NoteConfigTransport::Bluetooth, authorized,
             link == Link::Usb ? noteUsbAttachment() : 0)
        : String(F("{\"ok\":false,\"error\":\"Note is busy.\"}"));
    // Separate the config response from unrelated serial diagnostics.
    send(String("\n") + kNoteConfigPrefix + response + "\n");
    if (authorized) activityAt = millis();
    homeChanged |= oldName != palaNoteName() || oldLanguage != palaUiLanguage() ||
                   oldWeeklyCodexLimit != palaPrefersWeeklyCodexLimit();
    return true;
  }
  if (!line.startsWith(kPrefix)) return false;
  sendReply = send;
  const String message = line.substring(strlen(kPrefix));
  const int separator = message.indexOf(' ');
  const String command = separator < 0 ? message : message.substring(0, separator);
  const String arguments = separator < 0 ? "" : message.substring(separator + 1);
  const int argumentSeparator = arguments.indexOf(' ');
  const String first = argumentSeparator < 0 ? arguments : arguments.substring(0, argumentSeparator);
  const String second = argumentSeparator < 0 ? "" : arguments.substring(argumentSeparator + 1);
  handleCommand(command, first, second, idle, link, peer);
  sendReply = sendSerial;
  return true;
}

bool handleUsbSyncLine(const String& line, bool idle) {
  return handleLine(line, idle, Link::Usb, "legacy", sendSerial);
}

bool handleBluetoothSyncLine(const String& line, const String& peer, BluetoothSyncReply send) {
  return handleLine(line, true, Link::Bluetooth, peer, send);
}
#else
bool usbSyncActive() { return false; }
bool usbSyncWorking() { return false; }
bool takeBluetoothSyncDrained() { return false; }
void cancelUsbSync() {}
bool takeSyncHomeChanged() { return false; }
bool bluetoothSyncActive() { return false; }
void cancelBluetoothSync() {}
bool handleBluetoothSyncLine(const String&, const String&, BluetoothSyncReply) { return false; }
bool handleUsbSyncLine(const String& line, bool) {
  if (!line.startsWith(kPrefix)) return false;
  reply("ERROR AUTH");
  return true;
}
#endif
