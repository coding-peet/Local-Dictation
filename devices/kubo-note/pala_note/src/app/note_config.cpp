#include "note_config.h"
#include "owner_trace.h"
#include "../../config.h"

#include "note_access.h"

#include "battery.h"
#include "bluetooth_sync.h"
#include "bridge_client.h"
#include "display_pages.h"
#include "memory_telemetry.h"
#include "note_peers.h"
#include "notes.h"
#include "record.h"
#include "sync_queue.h"
#include "restart_maintenance.h"
#include "../../globals.h"
#include "settings.h"
#include "sleep.h"
#include "sleep_policy.h"
#include <ArduinoJson.h>
#include <WebServer.h>
#include <WiFi.h>
#include <bootloader_random.h>
#include <esp_random.h>
#include <mbedtls/hmac_drbg.h>
#include <mbedtls/platform_util.h>

namespace {
constexpr uint32_t kPairTimeout = 90000;
constexpr size_t kConfigRequestLimit = 1600;
constexpr size_t kCertificateChunk = 240;
constexpr char kSettingsPage[] PROGMEM =
#include "note_settings_page.inc"
;

enum class Approval { Waiting, Approved, Declined };
struct Enrollment {
  NoteConfigTransport source = NoteConfigTransport::Usb;
  uint32_t attachment = 0;
  uint32_t opened = 0;
  String ticket;
  NotePeer peer;
  Approval approval = Approval::Waiting;
  bool certDone = false;
  bool saved = false;
};
Enrollment pending;
uint32_t usbAttachment = 0;
bool usbPresent = false;
bool bleEnableRequested = false;
String csrf;
mbedtls_hmac_drbg_context randomGenerator;
bool entropyReady = false;

String randomHex(size_t count) {
  uint8_t bytes[32];
  if (count > sizeof(bytes)) return {};
  if (!noteConfigRandom(bytes, count)) return {};
  String result;
  if (result.reserve(count * 2)) {
    for (size_t i = 0; i < count; ++i) {
      result += "0123456789abcdef"[bytes[i] >> 4];
      result += "0123456789abcdef"[bytes[i] & 15];
    }
  }
  mbedtls_platform_zeroize(bytes, sizeof(bytes));
  return result;
}

void expirePair() {
  if (!pending.ticket.isEmpty() && millis() - pending.opened >= kPairTimeout) pending = {};
}

void fillResult(JsonDocument& result, const char* error = nullptr) {
  result["ok"] = !error;
  if (error) result["error"] = error;
}

bool matchesTicket(JsonVariantConst request, NoteConfigTransport link, uint32_t attachment) {
  const String ticket = request["ticket"] | "";
  return !pending.ticket.isEmpty() && pending.source == link && pending.attachment == attachment &&
         pending.ticket == ticket;
}

void addWifi(JsonArray rows, int index) {
  const PalaWifiProfile profile = palaWifiProfile(index);
  if (!profile.ssid.isEmpty()) rows.add<JsonObject>()["ssid"] = profile.ssid;
}

void addPeer(JsonArray rows, int index) {
  const NotePeer peer = notePeerAt(index);
  if (peer.id.isEmpty()) return;
  JsonObject row = rows.add<JsonObject>();
  row["id"] = peer.id;
  row["name"] = peer.name;
  row["primary"] = index == 0;
  row["wifi"] = !peer.host.isEmpty();
}

void addHeapUsage(JsonObject device, const char* key, const HeapUsage& usage) {
  if (!usage.totalBytes) return;
  // Two percentages keep the base settings reply within the BLE frame.
  JsonArray values = device[key].to<JsonArray>();
  values.add(usage.usedPercent());
  values.add(usage.peakPercent());
}

void getSettings(JsonVariantConst request, NoteConfigTransport link, JsonDocument& result) {
  const String section = request["section"] | "";
  const int wifiCount = palaWifiProfileCount();
  const int peerCount = notePeerCount();
  if (!section.isEmpty()) {
    if (!request["index"].is<int>()) { fillResult(result, "Invalid page index."); return; }
    const int index = request["index"].as<int>();
    if (section == "wifi" && index >= 0 && index < wifiCount) {
      addWifi(result["wifi"].to<JsonArray>(), index);
      return;
    }
    if (section == "peers" && index >= 0 && index < peerCount) {
      addPeer(result["peers"].to<JsonArray>(), index);
      return;
    }
    fillResult(result, "Invalid settings page.");
    return;
  }

  JsonObject device = result["device"].to<JsonObject>();
  device["name"] = palaNoteName();
  device["language"] = palaUiLanguage();
  device["weeklyCodexLimit"] = palaPrefersWeeklyCodexLimit();
  device["keepsWifiPassword"] = true;
  device["memoryTrash"] = !PALA_RECORDER_FIRST;
  device["r"] = 1;  // Compact wire capability within the BLE settings frame.
  device["webPasswordConfigured"] = noteAccessConfigured();
  device[link == NoteConfigTransport::Web ? "webPasswordRequired" : "pa"] = noteAccessPasswordRequired();
  device["displayPages"] = palaDisplayPages();
  device["sleepMinutes"] = palaIdleSleepMinutes();
  device["syncTiming"] = static_cast<int>(palaSyncTiming());
  device["pageWrap"] = palaDisplayPagesWrap();
  device["wifiEnabled"] = palaWifiEnabled();
  const BatteryReading battery = readBattery();
  if (battery.valid) device["batteryPercent"] = battery.percent;
  const MemoryTelemetry memory = readMemoryTelemetry();
  addHeapUsage(device, "ramUsage", memory.internal);
  addHeapUsage(device, "psramUsage", memory.psram);
#if PALA_RECORDER_FIRST
  device["rf"] = 1;
  if (link == NoteConfigTransport::Web) {
    const auto pending = syncQueueCounts();
    if (syncQueueReady()) {
      device["pendingAudio"] = pending.audio;
      device["pendingDelivery"] = pending.organization;
    }
    if (recordingTotalMiB()) {
      device["sdUsedMiB"] = recordingUsedMiB();
      device["sdTotalMiB"] = recordingTotalMiB();
    }
    device["recorderFirst"] = true;
    device["firmware"] = FIRMWARE_VERSION;
  }
#endif
#if PALA_BRIDGE_DISCOVERY
  device["preferBluetooth"] = palaPreferBluetooth();
  device["bluetoothEnabled"] = palaBluetoothEnabled();
  device["bridgeAddress"] = palaBridgeAddress();
#endif
  const bool hotspot = link == NoteConfigTransport::Web && WiFi.getMode() == WIFI_AP;
  result["transport"] = link == NoteConfigTransport::Usb ? "USB" :
                        link == NoteConfigTransport::Bluetooth ? "BLE" :
                        hotspot ? "Wi-Fi hotspot" : "Wi-Fi";
  JsonArray wifi = result["wifi"].to<JsonArray>();
  JsonArray peers = result["peers"].to<JsonArray>();
  if (link == NoteConfigTransport::Bluetooth) {
    JsonObject counts = result["pageCounts"].to<JsonObject>();
    counts["wifi"] = wifiCount;
    counts["peers"] = peerCount;
  } else {
    for (int i = 0; i < wifiCount; ++i) addWifi(wifi, i);
    for (int i = 0; i < peerCount; ++i) addPeer(peers, i);
  }
  if (link == NoteConfigTransport::Web) {
    JsonArray priority = result["bridgeOrder"].to<JsonArray>();
    for (int i = 0; i < peerCount; ++i) priority.add(notePeerAt(i).id);
    JsonObject capabilities = result["capabilities"].to<JsonObject>();
    capabilities["pair"] = false;
    capabilities["devicePortal"] = true;
    capabilities["enableBlePairing"] = bluetoothEnrollmentAvailable();
    capabilities["peers"] = true;
    capabilities["bridgeOrder"] = true;
    result["csrf"] = csrf;
  }
}

void startPair(JsonVariantConst request, NoteConfigTransport link, uint32_t attachment,
               JsonDocument& result) {
  if (!pending.ticket.isEmpty() && !pending.saved && pending.approval != Approval::Declined) {
    fillResult(result, "A pairing request is already pending.");
    return;
  }
  NotePeer peer;
  peer.id = request["id"] | "";
  peer.id.toLowerCase();
  peer.name = request["name"] | "";
  peer.host = request["host"] | "";
  peer.host.toLowerCase();
  if (!peer.host.isEmpty() && !request["port"].is<uint16_t>()) {
    fillResult(result, "A valid Bridge port is required.");
    return;
  }
  peer.port = request["port"] | static_cast<uint16_t>(0);
  if (!notePeerIdValid(peer.id) || peer.id == "legacy" || !notePeerNameValid(peer.name) ||
      (!peer.host.isEmpty() && (!notePeerHostValid(peer.host) || !peer.port)) ||
      (peer.host.isEmpty() && peer.port)) {
    fillResult(result, "Invalid peer id, name or Bridge address.");
    return;
  }
  if (notePeerCount() >= kNoteMaxPeers || !notePeerById(peer.id).id.isEmpty()) {
    fillResult(result, "Peer id already exists or all five slots are in use.");
    return;
  }
  const String ticket = randomHex(16);
  if (ticket.length() != 32) { fillResult(result, "Could not create a pairing ticket."); return; }
  pending = {};
  pending.source = link;
  pending.attachment = attachment;
  pending.opened = millis();
  pending.ticket = ticket;
  pending.approval = link == NoteConfigTransport::Bluetooth ? Approval::Approved : Approval::Waiting;
  pending.peer = peer;
  result["ticket"] = ticket;
  result["state"] = pending.approval == Approval::Approved ? "approved" : "pending";
}

void writeCertificate(JsonVariantConst request, JsonDocument& result) {
  if (pending.peer.host.isEmpty() || pending.approval != Approval::Approved || pending.saved ||
      !request["offset"].is<unsigned>() || !request["chunk"].is<const char*>() ||
      !request["final"].is<bool>()) {
    fillResult(result, "Certificate upload is unavailable.");
    return;
  }
  const unsigned offset = request["offset"].as<unsigned>();
  const JsonString bytes = request["chunk"].as<JsonString>();
  const String chunk(bytes.c_str(), bytes.size());
  const bool final = request["final"].as<bool>();
  String& cert = pending.peer.cert;
  if (chunk.length() != bytes.size() || chunk.isEmpty() || chunk.length() > kCertificateChunk ||
      offset > cert.length() || offset > kNoteMaxPeerCertificate - chunk.length()) {
    fillResult(result, "Invalid certificate chunk.");
    return;
  }
  for (char c : chunk) if (c != '\r' && c != '\n' && (c < 32 || c > 126)) {
    fillResult(result, "Certificate must use PEM text.");
    return;
  }
  if (offset == cert.length()) {
    if (pending.certDone || !cert.concat(chunk)) {
      fillResult(result, "Could not store certificate chunk.");
      return;
    }
  } else if (offset + chunk.length() > cert.length() || cert.substring(offset, offset + chunk.length()) != chunk) {
    fillResult(result, "Unexpected certificate offset.");
    return;
  }
  if (final) {
    if (offset + chunk.length() != cert.length()) { fillResult(result, "Invalid certificate ending."); return; }
    pending.certDone = true;
  }
  result["offset"] = cert.length();
}

void pairStatus(JsonDocument& result) {
  if (pending.approval != Approval::Approved) {
    result["state"] = pending.approval == Approval::Declined ? "cancelled" : "pending";
    return;
  }
  if (!pending.saved && !pending.peer.host.isEmpty() && !pending.certDone) {
    result["state"] = "approved";
    return;
  }
  if (!pending.saved) {
    pending.peer.token = randomHex(32);
    if (pending.peer.token.length() != 64 || !noteAddPeer(pending.peer)) {
      pending.approval = Approval::Declined;
      fillResult(result, "Unable to save the peer or its Bridge certificate.");
      return;
    }
    pending.saved = true;
  }
  result["state"] = "paired";
  result["id"] = pending.peer.id;
  result["token"] = pending.peer.token;
  result["serial"] = noteDeviceSerial();
  result["advertiser"] = notePeerAdvertiser();
}

void trashSettings(const String& op, JsonVariantConst request, JsonDocument& result) {
  if (op == "trash.status" || op == "trash.entry") {
    std::vector<TrashedNote> entries;
    if (!listTrashedNotes(entries)) {
      fillResult(result, "Could not read the Note's Trash.");
      return;
    }
    if (op == "trash.status") {
      result["days"] = trashRetentionDays();
      result["count"] = entries.size();
      return;
    }

    if (!request["index"].is<int>()) {
      fillResult(result, "Invalid Trash entry index.");
      return;
    }
    const int index = request["index"].as<int>();
    if (index < 0 || static_cast<size_t>(index) >= entries.size()) {
      fillResult(result, "This Trash entry has changed. Refresh Trash.");
      return;
    }
    const TrashedNote& entry = entries[index];
    JsonObject row = result["entry"].to<JsonObject>();
    row["num"] = entry.num;
    row["tag"] = entry.tag.length() <= 15 ? entry.tag : String();
    row["trashedAt"] = entry.trashedAt;
    row["capturedUtc"] = entry.capturedUtc.length() == 20 ? entry.capturedUtc : String();
  } else if (op == "trash.restore") {
    if (!request["num"].is<int>() || request["num"].as<int>() <= 0 ||
        !restoreTrashedNote(request["num"].as<int>()))
      fillResult(result, "The recording could not be restored.");
  } else if (op == "trash.retention") {
    const int days = request["days"].is<int>() ? request["days"].as<int>() : 0;
    if (days < 1 || days > 3650 || !setTrashRetentionDays(days))
      fillResult(result, "Could not save Trash retention. Choose 1 to 3650 days.");
  } else if (op == "trash.empty") {
    constexpr char confirmation[] = "EMPTY TRASH";
    const JsonString typed = request["confirmation"].as<JsonString>();
    if (typed.isNull() || typed.size() != sizeof(confirmation) - 1 ||
        memcmp(typed.c_str(), confirmation, sizeof(confirmation) - 1) != 0) {
      fillResult(result, "Type EMPTY TRASH to permanently delete trashed recordings.");
      return;
    }
    bool more = false;
    if (!emptyTrash(more)) fillResult(result, "Could not empty Trash. Refresh to see what remains.");
    else result["more"] = more;
  } else {
    fillResult(result, "Unsupported Trash operation.");
  }
}

String accessField(JsonVariantConst field) {
  const JsonString value = field.as<JsonString>();
  return value.isNull() || strlen(value.c_str()) != value.size() ? String() : String(value.c_str());
}

void dispatch(JsonVariantConst request, NoteConfigTransport link, bool authenticated,
              uint32_t attachment, bool enrollment, JsonDocument& result) {
  const String op = request["op"] | "";
  fillResult(result);
  expirePair();
  if (op == "pair.start" || op == "pair.status" || op == "pair.certificate") {
    if (!attachment || !(link == NoteConfigTransport::Usb ? usbPresent && usbAttachment == attachment : enrollment) ||
        link == NoteConfigTransport::Web) {
      fillResult(result, "Enrollment requires the current physical USB or approved Bluetooth connection.");
    } else if (op == "pair.start") startPair(request, link, attachment, result);
    else if (!matchesTicket(request, link, attachment)) fillResult(result, "Pairing request expired.");
    else if (op == "pair.certificate") writeCertificate(request, result);
    else pairStatus(result);
    return;
  }
  if (!authenticated) { fillResult(result, "Authenticate the Note connection first."); return; }
  const bool localUsb = link == NoteConfigTransport::Usb && usbPresent && attachment == usbAttachment && attachment;
  if (op == "auth.policy") {
    if (!localUsb) { fillResult(result, "Connect the paired Note by USB to change password protection."); return; }
    const auto required = request["required"];
    if (!required.is<bool>()) { fillResult(result, "Choose whether a password is required."); return; }
    const char* error = noteAccessSetPasswordRequired(required.as<bool>());
    fillResult(result, error);
    if (!error) csrf = "";
    return;
  }
  const bool passwordRequired = noteAccessPasswordRequired();
  if (link == NoteConfigTransport::Bluetooth && passwordRequired) {
    if (op == "auth.login") {
      String token;
      const char* error = noteAccessLogin(link, accessField(request["password"]), token);
      fillResult(result, error);
      if (!error) result["session"] = token;
      return;
    }
    if (!noteAccessSessionValid(link, accessField(request["session"]))) {
      const bool configured = noteAccessConfigured();
      fillResult(result, configured ? "Enter the Note password." : "Set the Note password over USB.");
      result["access"] = configured ? "locked" : "setup";
      return;
    }
  }
  if (op == "auth.save") {
    if (!localUsb && (link == NoteConfigTransport::Usb || !passwordRequired)) {
      fillResult(result, "Connect the Note by USB to change its password.");
      return;
    }
    const char* error = noteAccessSave(localUsb, accessField(request["old"]),
                                        accessField(request["password"]), accessField(request["repeat"]));
    fillResult(result, error);
    if (!error) csrf = "";
  } else if (op == "auth.logout" && link != NoteConfigTransport::Usb) {
    noteAccessLogout(link);
    if (link == NoteConfigTransport::Web) csrf = "";
  } else if (op == "get") getSettings(request, link, result);
  else if (op.startsWith("trash.")) trashSettings(op, request, result);
  else if (op == "restart.status") {
    addNoteStorageAuditSettings(result["storageAudit"].to<JsonObject>(), noteIndex.size());
    addNoteRestartConfirmation(result["confirmation"].to<JsonObject>());
  } else if (op == "restart.request") {
    const String mode = accessField(request["mode"]);
    const auto restart = mode == "restart" ? NoteRestartMode::Restart
        : !PALA_RECORDER_FIRST && mode == "audit" ? NoteRestartMode::Audit : NoteRestartMode::None;
    if (restart == NoteRestartMode::None) fillResult(result, "Choose a supported restart.");
    else {
      const uint32_t ticket = state == STATE_IDLE || state == STATE_TRANSFER ? requestNoteRestart(restart) : 0;
      if (ticket) {
        result["pending"] = true;
        result["ticket"] = ticket;
      } else fillResult(result, "Finish the current activity or confirmation first.");
    }
  }
  else if (op == "pair.enableBle" && link == NoteConfigTransport::Web && bluetoothEnrollmentAvailable()) {
    bleEnableRequested = true;
    result["state"] = "listening";
  } else if (op == "wifi.save" && request["ssid"].is<const char*>() && request["password"].is<const char*>()) {
    const String ssid = request["ssid"].as<String>();
    String password = request["password"].as<String>();
    if (password.isEmpty()) {
      for (int i = 0; i < palaWifiProfileCount(); ++i) {
        const PalaWifiProfile profile = palaWifiProfile(i);
        if (profile.ssid == ssid) {
          password = profile.password;
          break;
        }
      }
    }
    if (!palaSaveWifiProfile(ssid, password))
      fillResult(result, "Could not save this Wi-Fi profile.");
  } else if (op == "wifi.remove") {
    if (!palaRemoveWifiProfile(request["ssid"] | "")) fillResult(result, "Wi-Fi profile was not removed.");
  } else if (op == "peers.remove") {
    if (!noteRemovePeer(request["id"] | "")) fillResult(result, "Bridge was not removed.");
  } else if (op == "bridge.order") {
    const JsonArrayConst ids = request["ids"].as<JsonArrayConst>();
    if (ids.isNull() || ids.size() > kNoteMaxPeers) { fillResult(result, "Invalid Bridge order."); return; }
    String desired[kNoteMaxPeers];
    int index = 0;
    for (JsonVariantConst id : ids) desired[index++] = id | "";
    if (!noteOrderPeers(desired, index)) fillResult(result, "Invalid or unsaved Bridge order.");
#if PALA_BRIDGE_DISCOVERY
  } else if (op == "bridge.address") {
    const JsonString raw = request["address"].as<JsonString>();
    if (raw.isNull() || strlen(raw.c_str()) != raw.size() ||
        !palaSetBridgeAddress(String(raw.c_str()))) {
      fillResult(result, "Enter a private IPv4 address with an optional port, or leave the field empty.");
    } else {
      resetBridgeAddressCache();
    }
#endif
  } else if (op == "sync.save") {
    const JsonVariantConst value = request["mode"];
    if (!value.is<int>() || !validSyncTiming(value.as<int>())) {
      fillResult(result, "Choose a supported sync timing.");
    } else if (!palaSetSyncTiming(static_cast<SyncTiming>(value.as<int>()))) {
      fillResult(result, "Could not save the sync timing.");
    } else {
      resetActivity();
    }
  } else if (op == "sleep.save") {
    const JsonVariantConst value = request["minutes"];
    if (!value.is<int>() || !validIdleSleepMinutes(value.as<int>())) {
      fillResult(result, "Enter a timeout from 1 to 99 minutes or use Default.");
    } else if (!palaSetIdleSleepMinutes(value.as<int>())) {
      fillResult(result, "Could not save the sleep timeout.");
    } else {
      resetActivity();
    }
#if PALA_BRIDGE_DISCOVERY
  } else if (op == "radios.save") {
    const auto wifi = request["wifiEnabled"];
    const auto bluetooth = request["bluetoothEnabled"];
    if (!wifi.is<bool>() || !bluetooth.is<bool>() ||
        !palaSetWirelessRadios(wifi.as<bool>(), bluetooth.as<bool>())) {
      fillResult(result, "Could not save the radio settings.");
    }
  } else if (op == "wireless.save") {
    const auto preferred = request["preferBluetooth"];
    if (!preferred.is<bool>() || !palaSetPreferBluetooth(preferred.as<bool>())) {
      fillResult(result, "Could not save wireless sync priority.");
    }
#endif
  } else if (op == "pages.save") {
    const JsonString order = request["pages"].as<JsonString>();
    const auto wrap = request["wrap"];
    if (order.size() != kDisplayPageCount || !validDisplayPages(order.c_str(), order.size()) ||
        (request.containsKey("wrap") && !wrap.is<bool>())) {
      fillResult(result, "Invalid display page settings.");
    } else if (!palaSetDisplayPages(String(order.c_str()), wrap | palaDisplayPagesWrap())) {
      fillResult(result, "Could not save the display pages.");
    }
  } else if (op == "settings.save") {
    const auto weekly = request["weeklyCodexLimit"];
    if (!weekly.isNull() && !weekly.is<bool>()) {
      fillResult(result, "Invalid Codex limit selection.");
    } else if (!palaSaveLocalNoteSettings(request["name"] | "", request["language"] | "",
                                          weekly | palaPrefersWeeklyCodexLimit())) {
      fillResult(result, "Could not save the Note settings.");
    }
  } else fillResult(result, "Unsupported Note settings operation.");
}

void sendJson(WebServer& server, int status, const String& payload) {
  server.sendHeader("Cache-Control", "no-store");
  server.send(status, "application/json; charset=utf-8", payload);
}
}  // namespace

void noteConfigSeedEntropy() {
  // Called before the battery ADC or the radio starts. ESP32-S3 needs an
  // active entropy source even when the first pairing is over USB alone.
  uint8_t seed[48];
  bootloader_random_enable();
  esp_fill_random(seed, sizeof(seed));
  bootloader_random_disable();
  mbedtls_hmac_drbg_init(&randomGenerator);
  const auto* sha256 = mbedtls_md_info_from_type(MBEDTLS_MD_SHA256);
  entropyReady = sha256 && mbedtls_hmac_drbg_seed_buf(&randomGenerator, sha256, seed, sizeof(seed)) == 0;
  mbedtls_platform_zeroize(seed, sizeof(seed));
  if (!entropyReady) mbedtls_hmac_drbg_free(&randomGenerator);
}

bool noteConfigRandom(uint8_t* output, size_t length) {
  return entropyReady && mbedtls_hmac_drbg_random(&randomGenerator, output, length) == 0;
}

String noteConfigRequest(const String& json, NoteConfigTransport transport, bool authenticated,
                         uint32_t attachment, bool enrollment) {
  owner_trace::WorkScope ownerWork(owner_trace::Work::Settings);
  JsonDocument result;
  JsonDocument request;
  if (json.length() > kConfigRequestLimit || deserializeJson(request, json) ||
      !request.is<JsonObject>()) fillResult(result, "Invalid Note settings request.");
  else dispatch(request.as<JsonVariantConst>(), transport, authenticated, attachment, enrollment, result);
  String response;
  serializeJson(result, response);
  return response;
}

void noteSettingsBegin() { csrf = ""; }

void noteSettingsHttp(WebServer& server) {
  const auto servePage = [&server]() {
    server.sendHeader("Cache-Control", "no-store");
    server.sendHeader("X-Frame-Options", "DENY");
    server.sendHeader("Content-Security-Policy",
      "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src 'self' data:; "
      "connect-src 'self'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'");
    server.send_P(200, "text/html; charset=utf-8", kSettingsPage);
  };
  server.on("/settings", HTTP_GET, servePage);
  server.on("/note-settings", HTTP_GET, servePage);
  server.on("/api/note-settings", HTTP_GET, [&server]() {
    if (server.header("X-Pala-Settings") != "1") {
      sendJson(server, 403, F("{\"ok\":false,\"error\":\"Missing settings request header.\"}"));
      return;
    }
    if (csrf.isEmpty()) csrf = randomHex(16);
    sendJson(server, 200, noteConfigRequest(F("{\"op\":\"get\"}"), NoteConfigTransport::Web, true, 0));
  });
  server.on("/api/note-settings", HTTP_POST, [&server]() {
    if (csrf.length() != 32 || server.header("X-Pala-Settings") != "1" || server.header("X-Pala-CSRF") != csrf) {
      sendJson(server, 403, F("{\"ok\":false,\"error\":\"Invalid settings request signature.\"}"));
      return;
    }
    sendJson(server, 200, noteConfigRequest(server.arg("plain"), NoteConfigTransport::Web, true, 0));
  });
}

void noteUsbLinkChanged(bool active) {
  if (active == usbPresent) return;
  notePairingDetach(NoteConfigTransport::Usb, usbAttachment);
  usbPresent = active;
  if (++usbAttachment == 0) ++usbAttachment;
}

uint32_t noteUsbAttachment() { return usbPresent ? usbAttachment : 0; }

bool noteUsbPairingOpen() {
  expirePair();
  return !pending.ticket.isEmpty() && pending.source == NoteConfigTransport::Usb &&
         pending.approval != Approval::Declined && !pending.saved && usbPresent &&
         pending.attachment == usbAttachment;
}

bool noteUsbPairingPrompt(String& name, String* id) {
  if (!noteUsbPairingOpen() || pending.approval != Approval::Waiting) return false;
  name = pending.peer.name;
  if (id) *id = pending.peer.id;
  return true;
}

void noteUsbPairingConfirm(bool accept) {
  String unused;
  if (!accept && noteUsbPairingOpen()) pending.approval = Approval::Declined;
  else if (accept && noteUsbPairingPrompt(unused)) pending.approval = Approval::Approved;
}

void notePairingDetach(NoteConfigTransport source, uint32_t attachment) {
  if (!pending.ticket.isEmpty() && pending.source == source && pending.attachment == attachment) pending = {};
}

bool takeNoteBleEnableRequest() {
  const bool wanted = bleEnableRequested;
  bleEnableRequested = false;
  return wanted;
}
