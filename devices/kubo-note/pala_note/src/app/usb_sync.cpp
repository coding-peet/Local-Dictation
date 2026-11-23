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
#include "bluetooth_audio.h"
#include "bluetooth_sync.h"
#include "brain.h"
#include "network.h"
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
constexpr size_t kOrganizationLimit = 80000;
constexpr size_t kOrganizationChunk = 224;
constexpr uint32_t kOrganizationTimeoutMs = 300000;

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

struct BluetoothBacklog {
  std::vector<int> notes;
  size_t completed = 0;
  bool process = false;
  bool planned = false;
  bool changed = false;

  int current() const { return completed < notes.size() ? notes[completed] : 0; }
  void saved(int note) {
    if (note != current()) return;
    ++completed;
    changed = true;
  }
} bluetoothBacklog;

enum class OrganizationState { Idle, Scheduled, Running, Done, Deferred };
struct BluetoothOrganization {
  OrganizationState state = OrganizationState::Idle;
  const String* request = nullptr;
  const char* route = nullptr;
  String response;
  size_t expectedBytes = 0;
  String digest;
  bool received = false;
  bool failed = false;
} bluetoothOrganization;

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
  if (owner == Link::Bluetooth) {
    bluetoothBacklog = BluetoothBacklog{};
    bluetoothOrganization = BluetoothOrganization{};
  }
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

bool payloadDigestMatches(const String& payload, const String& expected) {
  const auto* algorithm = mbedtls_md_info_from_type(MBEDTLS_MD_SHA256);
  uint8_t digest[32];
  if (!algorithm || mbedtls_md(algorithm,
        reinterpret_cast<const unsigned char*>(payload.c_str()), payload.length(), digest) != 0) return false;
  return sameDigest(hex(digest, sizeof(digest)), expected);
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

bool canTranscribe(const NoteEntry& note) {
  if (!notePendingTranscription(note) || noteHasCommittedTranscript(note.num)) return false;
  File wav = SD_MMC.open(notePath(note.num, "wav").c_str(), FILE_READ);
  return wav && !wav.isDirectory() && wav.size() > 44 && wav.size() <= kMaximumWav;
}

bool canOrganize(const NoteEntry& note) {
  if (!note.hasText || !noteQueueRetryDue(note.num) || brainMemoryIsCurrent(note.num)) return false;
  File text = openCommittedReadOnly(notePath(note.num, "txt"));
  return text && !text.isDirectory() && text.size() && text.size() <= kMaximumText;
}

void planBluetoothBacklog(const String& mode) {
  if (bluetoothBacklog.planned || (mode != "AUDIO" && mode != "TEXT" && mode != "FULL")) {
    reply("ERROR COMMAND");
    return;
  }
  bluetoothBacklog.planned = true;
  bluetoothBacklog.process = mode != "AUDIO";
  bluetoothBacklog.changed = true;
  if (recordingSyncAllowed()) {
    for (auto note = noteIndex.rbegin(); note != noteIndex.rend(); ++note) {
      if ((mode != "TEXT" && canTranscribe(*note)) || (bluetoothBacklog.process && canOrganize(*note))) {
        bluetoothBacklog.notes.push_back(note->num);
      }
    }
  }
  reply(String("BATCH ") + bluetoothBacklog.notes.size());
}

void nextBluetoothWork() {
  const int note = bluetoothBacklog.current();
  if (!bluetoothBacklog.planned || bluetoothOrganization.state == OrganizationState::Running ||
      bluetoothOrganization.state == OrganizationState::Scheduled) {
    reply("ERROR BUSY");
  } else if (!note) {
    bluetoothDrained = true;
    reply("EMPTY");
  } else {
    reply(String("WORK ") + note + (noteHasCommittedTranscript(note) ? " TEXT" : " AUDIO"));
  }
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
  if (!note || (owner == Link::Bluetooth && bluetoothBacklog.planned && bluetoothBacklog.current() != note->num) ||
      !selectNote(*note)) reply("ERROR NOTE");
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

bool appendPayload(String& buffer, size_t expected, size_t maxChunk, const String& first, const String& data) {
  uint32_t offset = 0;
  const size_t bytes = data.length() / 2;
  if (!expected || !decimal(first, expected, offset) || offset != buffer.length() ||
      data.length() % 2 || !bytes || bytes > maxChunk || bytes > expected - offset ||
      !validHex(data, data.length())) return false;

  char decoded[kMaximumWrite];
  for (size_t i = 0; i < bytes; i++) {
    const char byte = static_cast<char>((hexDigit(data[2 * i]) << 4) | hexDigit(data[2 * i + 1]));
    if (!byte) return false;
    decoded[i] = byte;
  }
  return buffer.concat(decoded, bytes);
}

void appendTranscript(const String& first, const String& data) {
  reply(appendPayload(transcript, textBytes, kMaximumWrite, first, data) ? "OK" : "ERROR TEXT");
}

void commitTranscript(bool organizeCalendar) {
  if (!selectedNote || receivingWeather || !textBytes ||
      transcript.length() != textBytes || !payloadDigestMatches(transcript, textDigest)) {
    reply("ERROR TEXT");
    return;
  }
  const int note = selectedNote;
  const bool plannedBluetooth = owner == Link::Bluetooth && bluetoothBacklog.planned;
  const bool needsOrganization = organizeCalendar || (plannedBluetooth && bluetoothBacklog.process);
  // Persist the route before the text so a reboot cannot skip organization.
  const char* route = needsOrganization ? "organizing" : "usb-local";
  if (noteHasCommittedTranscript(note) || !writeNoteQueueState(note, route, nullptr, false, true) ||
      !saveNoteTranscript(note, transcript)) {
    reply("ERROR SAVE");
    resetTransfer();
    return;
  }
  homeChanged = true;
  if (plannedBluetooth && !bluetoothBacklog.process) bluetoothBacklog.saved(note);
  reply(String("SAVED ") + note);
  resetTransfer();
}

void scheduleBluetoothOrganization(const String& value) {
  const NoteEntry* note = findIndexedNote(value);
  if (!bluetoothBacklog.process || !note || note->num != bluetoothBacklog.current() ||
      bluetoothOrganization.state == OrganizationState::Running ||
      bluetoothOrganization.state == OrganizationState::Scheduled || !canOrganize(*note)) {
    reply("ERROR NOTE");
    return;
  }
  bluetoothOrganization = BluetoothOrganization{};
  bluetoothOrganization.state = OrganizationState::Scheduled;
  reply("OK");
}

void bluetoothOrganizationStatus() {
  const auto& job = bluetoothOrganization;
  if (job.state == OrganizationState::Done) reply("DONE");
  else if (job.state == OrganizationState::Deferred) reply("DEFERRED");
  else if (job.request && !job.received && !job.failed) {
    reply(String("REQUEST ") + job.route + " " + job.request->length());
  } else if (job.state == OrganizationState::Scheduled || job.state == OrganizationState::Running) {
    reply("WAIT");
  } else reply("ERROR COMMAND");
}

void readOrganizationRequest(const String& first, const String& second) {
  const auto* request = bluetoothOrganization.request;
  uint32_t offset = 0, length = 0;
  if (!request || !decimal(first, request->length(), offset) || !decimal(second, kOrganizationChunk, length) ||
      !length || length > request->length() - offset) {
    reply("ERROR READ");
    return;
  }
  const auto* data = reinterpret_cast<const uint8_t*>(request->c_str()) + offset;
  const String encoded = hex(data, length);
  if (encoded.length() != length * 2) reply("ERROR READ");
  else reply(String("DATA ") + offset + " " + encoded);
}

void beginOrganizationResponse(const String& first, const String& second) {
  auto& job = bluetoothOrganization;
  uint32_t size = 0;
  if (!job.request || job.received || job.failed || job.expectedBytes ||
      !decimal(first, kOrganizationLimit, size) || !size || !validHex(second, 64) ||
      !job.response.reserve(size)) {
    reply("ERROR TEXT");
    return;
  }
  job.expectedBytes = size;
  job.digest = second;
  reply("OK");
}

void finishOrganizationResponse(bool failed) {
  auto& job = bluetoothOrganization;
  if (!job.request || job.received || job.failed ||
      (!failed && (!job.expectedBytes || job.response.length() != job.expectedBytes ||
                   !payloadDigestMatches(job.response, job.digest)))) {
    reply("ERROR TEXT");
    return;
  }
  job.failed = failed;
  job.received = !failed;
  reply("OK");
}

bool handleBluetoothWork(const String& command, const String& first, const String& second) {
  if (command == "BATCH" && !second.length()) planBluetoothBacklog(first);
  else if (command == "BATCH-NEXT" && !first.length()) nextBluetoothWork();
  else if (command == "ORGANIZE" && !second.length()) scheduleBluetoothOrganization(first);
  else if (command == "JOB" && !first.length()) bluetoothOrganizationStatus();
  else if (command == "JOB-READ") readOrganizationRequest(first, second);
  else if (command == "JOB-RESPONSE") beginOrganizationResponse(first, second);
  else if (command == "JOB-PUT") {
    auto& job = bluetoothOrganization;
    reply(job.request && !job.received && !job.failed &&
          appendPayload(job.response, job.expectedBytes, kOrganizationChunk, first, second) ? "OK" : "ERROR TEXT");
  } else if (command == "JOB-COMMIT" && !first.length()) finishOrganizationResponse(false);
  else if (command == "JOB-FAIL" && !first.length()) finishOrganizationResponse(true);
  else return false;
  return true;
}

bool serviceOrganizationConnection() {
  return pollBluetoothSync() && bluetoothSyncActive();
}

bool bluetoothOrganizationPost(const char* endpoint, const String& payload, String& response,
                               uint16_t, String* failure, const String&) {
  auto& job = bluetoothOrganization;
  if (failure) *failure = "organization: Bluetooth Bridge response interrupted";
  if (job.state != OrganizationState::Running || !payload.length() || payload.length() > kOrganizationLimit)
    return false;
  if (!strcmp(endpoint, "/v1/responses")) job.route = "MEMORY";
  else if (!strcmp(endpoint, "/pala/event")) job.route = "EVENT";
  else return false;
  job.request = &payload;
  job.response = String();
  job.expectedBytes = 0;
  job.digest = "";
  job.received = false;
  job.failed = false;
  const uint32_t started = millis();
  while (millis() - started < kOrganizationTimeoutMs && serviceOrganizationConnection()) {
    if (job.received && !job.failed) {
      response = std::move(job.response);
      job.request = nullptr;
      return true;
    }
    if (job.failed) break;
    delay(5);
  }
  job.request = nullptr;
  job.response = String();
  return false;
}

void commitWeather() {
  if (!receivingWeather || !textBytes || transcript.length() != textBytes || !payloadDigestMatches(transcript, textDigest)) {
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

bool storeDeliveryCounts(const String& payload, const String& signature) {
  bool changed = false;
  if (payload.length() > 72 || !sameDigest(proof(String("delivery ") + payload), signature) ||
      !storeHomeDelivery(payload, changed)) {
    reply("ERROR DELIVERY");
    return false;
  }
  homeChanged |= changed;
  reply(String("DELIVERY-SAVED ") + payload + " " + proof(String("delivery-saved ") + payload));
  return true;
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
                   bool idle, Link link, const String& peer, bool deliveryOnly, bool audioSupported) {
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
  if (command == "DELIVERY") {
    if (storeDeliveryCounts(first, second) && deliveryOnly) bluetoothDrained = true;
  }
  else if (command == "CLOSE" && !first.length()) { resetSession(); reply("OK"); }
  else if (deliveryOnly) reply("ERROR COMMAND");
  else if (owner == Link::Bluetooth && audioSupported && handleBluetoothWork(command, first, second)) {}
  else if (command == "NEXT" && !first.length()) selectRecording(true, first);
  else if (command == "SELECT" && !second.length()) selectRecording(false, first);
  else if (command == "AUDIO" && audioSupported && selectedNote && download && !sentBytes && first.isEmpty()) {
    reply(String("AUDIO ") + pala_ble::kAudioBlockBytes);
  }
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
  else if (command == "HOLIDAYS") storeHolidayCalendar(first, second);
  else if (command == "WEATHER") beginPayload(first, second, true);
  else if (command == "WEATHER-COMMIT" && !first.length()) commitWeather();
  else if (command == "WEATHER-READ" && !first.length()) readWeather();
  else if (command == "READWEATHER" && readingWeather) readDownload(first, second, true);
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
bool readBluetoothAudio(const String& peer, uint32_t offset, uint8_t* data, size_t length) {
  if (!bluetoothSyncActive() || sessionPeer != peer || !selectedNote || !download ||
      offset != sentBytes || length == 0 || length > downloadBytes - sentBytes) return false;
  if (download.read(data, length) != length) {
    resetTransfer();
    return false;
  }
  sentBytes += length;
  lastReadHex = String();
  activityAt = millis();
  if (sentBytes == downloadBytes) download.close();
  return true;
}
bool takeBluetoothSyncDrained() {
  const bool drained = bluetoothDrained;
  bluetoothDrained = false;
  return drained;
}
bool takeBluetoothBacklogProgress(int& completed, int& total) {
  if (!bluetoothBacklog.changed) return false;
  completed = static_cast<int>(bluetoothBacklog.completed);
  total = static_cast<int>(bluetoothBacklog.notes.size());
  bluetoothBacklog.changed = false;
  return true;
}

bool pollBluetoothOrganization() {
  if (bluetoothOrganization.state != OrganizationState::Scheduled) return false;
  const int note = bluetoothBacklog.current();
  bluetoothOrganization.state = OrganizationState::Running;
  const bool saved = understandNoteOverBluetooth(note, bluetoothOrganizationPost, serviceOrganizationConnection);
  if (saved) {
    writeNoteQueueState(note, "indexing", nullptr, false, true);
    homeChanged = true;
  }
  if (bluetoothSyncActive() && note == bluetoothBacklog.current()) {
    bluetoothOrganization.state = saved ? OrganizationState::Done : OrganizationState::Deferred;
    bluetoothOrganization.request = nullptr;
    bluetoothOrganization.response = String();
    if (saved) bluetoothBacklog.saved(note);
  }
  return true;
}

void cancelUsbSync() { if (owner == Link::Usb) resetSession(); }
void cancelBluetoothSync() { if (owner == Link::Bluetooth) resetSession(); }

bool takeSyncHomeChanged() {
  const bool changed = homeChanged;
  homeChanged = false;
  return changed;
}

static bool handleLine(const String& line, bool idle, Link link, const String& peer, BluetoothSyncReply send,
                       bool deliveryOnly, bool audioSupported) {
  if (line.startsWith(kNoteConfigPrefix)) {
    if (deliveryOnly) return false;
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
  handleCommand(command, first, second, idle, link, peer, deliveryOnly, audioSupported);
  sendReply = sendSerial;
  return true;
}

bool handleUsbSyncLine(const String& line, bool idle) {
  return handleLine(line, idle, Link::Usb, "legacy", sendSerial, false, false);
}

bool handleBluetoothSyncLine(const String& line, const String& peer, BluetoothSyncReply send,
                             bool deliveryOnly, bool audioSupported) {
  return handleLine(line, true, Link::Bluetooth, peer, send, deliveryOnly, audioSupported);
}
#else
bool usbSyncActive() { return false; }
bool usbSyncWorking() { return false; }
bool takeBluetoothSyncDrained() { return false; }
bool takeBluetoothBacklogProgress(int&, int&) { return false; }
bool pollBluetoothOrganization() { return false; }
void cancelUsbSync() {}
bool takeSyncHomeChanged() { return false; }
bool bluetoothSyncActive() { return false; }
void cancelBluetoothSync() {}
bool handleBluetoothSyncLine(const String&, const String&, BluetoothSyncReply, bool, bool) { return false; }
bool readBluetoothAudio(const String&, uint32_t, uint8_t*, size_t) { return false; }
bool handleUsbSyncLine(const String& line, bool) {
  if (!line.startsWith(kPrefix)) return false;
  reply("ERROR AUTH");
  return true;
}
#endif
