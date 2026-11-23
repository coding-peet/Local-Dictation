#include <Arduino.h>
#include <SD_MMC.h>
#include <WiFi.h>
#include <WebServer.h>
#include <ArduinoJson.h>
#include <esp_partition.h>
#include <vector>
#include <algorithm>

extern "C" {
#include "config.h"
#include "src/i2c_bsp/i2c_bsp.h"
#include "src/audio/audio_bsp.h"
}

#include "types.h"
#include "globals.h"
#include "sounds.h"
#include "src/power/board_power_bsp.h"
#include "src/display/epaper_driver_bsp.h"
#include "src/app/aquarium_view.h"
#include "src/app/brain.h"
#include "src/app/provider.h"
#include "src/app/battery.h"
#include "src/app/button_gesture.h"
#include "src/app/companion_controls.h"
#include "src/app/display_pages.h"
#include "src/app/network.h"
#include "src/app/notes.h"
#include "src/app/note_config.h"
#include "src/app/note_peers.h"
#include "src/app/record.h"
#include "src/app/rtc.h"
#include "src/app/settings.h"
#include "src/app/sleep.h"
#include "src/app/sleep_policy.h"
#include "src/app/tasks.h"
#include "src/app/ui.h"
#include "src/app/usb_sync.h"
#include "src/app/bridge_client.h"
#include "src/app/bluetooth_sync.h"
#include "src/app/home_weather.h"
#include "src/app/home_holidays.h"
#include "src/app/home_delivery.h"
#include "src/app/home_bridge_status.h"
#if PALA_BRIDGE_DISCOVERY
#include <driver/usb_serial_jtag.h>
#endif

board_power_bsp_t board(EPD_PWR_PIN, Audio_PWR_PIN, VBAT_PWR_PIN);
epaper_driver_display* display = nullptr;
std::vector<NoteEntry> noteIndex;

AppState state = STATE_IDLE;
HomeBridgeStatus homeBridgeStatus;
int lastRecNum = -1;
uint32_t lastActivityMs = 0;

WebServer transferServer(80);
bool transferServerActive = false;
String transferUrl;

bool timeReady = false;
bool audioPlaying = false;
bool stopPlayback = false;

void resetActivity();
bool recordingSyncAllowed();

enum class PressResult { Released, LongSingle, Chord };

namespace {

bool idleInputArmed = true;
enum class PairingView { None, BluetoothWait, BluetoothCode, UsbWait, UsbPrompt };
PairingView pairingView = PairingView::None;
bool pairingButtonsArmed = false;
uint32_t pairingDisplayedCode = 0;
uint32_t companionGestureStartedAt = 0;
uint32_t nextBackgroundAttemptAt = 0;
uint32_t backlogRequestedAt = 0;
bool backlogRequested = false;
RecordingSyncSchedule recordingSync;
struct BacklogScreen {
  BacklogLink link = BacklogLink::Deferred;
  int completed = 0;
  int total = 0;
  bool authenticated = false;
} backlogScreen;
#if PALA_BRIDGE_DISCOVERY
bool usbDataConnected = false;
bool usbQueueChecked = false;
bool triedPreferredBluetooth = false;
RTC_DATA_ATTR int64_t retryHomeWeatherAfter = 0;
uint32_t nextHomeWeatherCheckAt = 0;
#endif
bool backlogSyncActive = false;
int backlogSyncTotal = 0;
uint32_t nextHomeClockCheckAt = 0;
struct ButtonEdge { uint32_t at; bool down; };
constexpr uint8_t kButtonEdgeCapacity = 32;
struct ButtonInput {
  volatile ButtonEdge edges[kButtonEdgeCapacity];
  volatile uint8_t read = 0;
  volatile uint8_t write = 0;
  volatile bool overflow = false;
  volatile bool pressLatched = false;
  ButtonGesture gesture;
};
ButtonInput upperButton;
ButtonInput lowerButton;
AquariumState aquarium;
bool aquariumChord = false;
int lastAquariumFrame = -1;
uint32_t lastAquariumPaintAt = 0;
CompanionControls companionControls;
CompanionDisplayState companionDisplay;
bool companionUsesHotspot = false;
uint32_t nextCompanionNetworkCheckAt = 0;
String companionNetworkName;
String companionApPassword;
String displayedHomeDay;
HomeCodexDisplay displayedHomeUsage;
String activeDisplayPages;
int displayPageIndex = -1;  // Events open separately from the lower button.
uint32_t agendaShownAt = 0;
int agendaPage = 0;
bool setupRequired = false;
uint32_t setupCompanionHoldStartedAt = 0;
int processPendingMemories(int preferredNote, int maxNotes);

String usbSetupLine;
bool discardUsbLine = false;

struct FlashSetupHeader {
  char magic[8];
  uint32_t jsonLength;
  uint32_t checksum;
};

uint32_t setupChecksum(const uint8_t* data, size_t length) {
  uint32_t hash = 2166136261UL;
  for (size_t i = 0; i < length; i++) {
    hash ^= data[i];
    hash *= 16777619UL;
  }
  return hash;
}

bool importFlashSetup() {
  const esp_partition_t* partition = esp_partition_find_first(
      ESP_PARTITION_TYPE_DATA, ESP_PARTITION_SUBTYPE_ANY, "palacfg");
  if (!partition) return false;

  FlashSetupHeader header{};
  if (esp_partition_read(partition, 0, &header, sizeof(header)) != ESP_OK ||
      memcmp(header.magic, "PALACFG1", 8) != 0 ||
      header.jsonLength == 0 || header.jsonLength > 700) return false;

  std::vector<uint8_t> bytes(header.jsonLength);
  if (esp_partition_read(partition, sizeof(header), bytes.data(), bytes.size()) != ESP_OK ||
      setupChecksum(bytes.data(), bytes.size()) != header.checksum) {
    std::fill(bytes.begin(), bytes.end(), 0);
    esp_partition_erase_range(partition, 0, partition->size);
    return false;
  }

  JsonDocument doc;
  if (deserializeJson(doc, bytes.data(), bytes.size())) {
    std::fill(bytes.begin(), bytes.end(), 0);
    esp_partition_erase_range(partition, 0, partition->size);
    return false;
  }
  const String ssid = doc["ssid"] | "";
  const String password = doc["password"] | "";
  const String apiKey = doc["apiKey"] | "";
  const String voice = doc["voice"] | "cedar";
  const String language = doc["language"] | "en";
  const bool valid = ssid.length() && ssid.length() <= 32 && password.length() <= 63 &&
      (PALA_MAC_BRIDGE || apiKey.length() >= 11) && apiKey.length() <= 200 &&
      (voice == "cedar" || voice == "coral") &&
      (language == "de" || language == "en" || language == "fr" ||
       language == "es" || language == "it" || language == "pl" || language == "ru" ||
       language == "sk" || language == "cs" || language == "uk" || language == "nl" || language == "pt");
       
  std::fill(bytes.begin(), bytes.end(), 0);
  if (!valid) {
    esp_partition_erase_range(partition, 0, partition->size);
    return false;
  }

  const bool saved = palaSaveSettings(ssid, password, apiKey, voice) &&
                     palaSetUiLanguage(language);
  // The temporary transfer area contains credentials. Remove it after the
  // single import attempt even if Preferences rejects the write, so a raw API
  // key is never left indefinitely in the staging partition.
  esp_partition_erase_range(partition, 0, partition->size);
  if (saved) {
    Serial.println("[Setup] One-step configuration imported");
  }
  return saved;
}

void replyUsbSetup(const char* status, const char* detail = nullptr) {
  Serial.print("PALA_SETUP_");
  Serial.print(status);
  if (detail) { Serial.print(':'); Serial.print(detail); }
  Serial.println();
}

void applyUsbSetupLine(const String& line) {
  if (!line.startsWith("PALA_SETUP_V1 ")) return;
#if PALA_BRIDGE_DISCOVERY
  replyUsbSetup("ERROR", "USE_NOTE_PAIRING");
  return;
#endif
  if (line.length() > 700) { replyUsbSetup("ERROR", "TOO_LONG"); return; }

  JsonDocument doc;
  if (deserializeJson(doc, line.substring(14))) {
    replyUsbSetup("ERROR", "INVALID_JSON");
    return;
  }
  const String ssid = doc["ssid"] | "";
  const String password = doc["password"] | "";
  const String apiKey = doc["apiKey"] | "";
  const String voice = doc["voice"] | "cedar";
  const String language = doc["language"] | "en";
  if (!ssid.length() || ssid.length() > 32 || password.length() > 63 ||
      (!PALA_MAC_BRIDGE && apiKey.length() < 11) || apiKey.length() > 200 ||
      (voice != "cedar" && voice != "coral") ||
      (language != "de" && language != "en" && language != "fr" &&
       language != "es" && language != "it" && language != "pl" && language != "ru" &&
       language != "sk" && language != "cs" && language != "uk" && language != "nl" && language != "pt")) {
    replyUsbSetup("ERROR", "INVALID_VALUES");
    return;
  }
  if (!palaSaveSettings(ssid, password, apiKey, voice) || !palaSetUiLanguage(language)) {
    replyUsbSetup("ERROR", "SAVE_FAILED");
    return;
  }
  replyUsbSetup("OK");
  Serial.flush();
  // Give WebSerial enough time to receive the acknowledgement before USB is
  // torn down by the restart. A short delay was unreliable on macOS/Chrome.
  delay(1500);
  ESP.restart();
}

void handleUsbSetup() {
  while (Serial.available()) {
    const char c = static_cast<char>(Serial.read());
    if (c == '\n') {
      if (!discardUsbLine) {
        usbSetupLine.trim();
        if (!handleUsbSyncLine(usbSetupLine, state == STATE_IDLE) && !usbSyncActive())
          applyUsbSetupLine(usbSetupLine);
      }
      usbSetupLine = "";
      discardUsbLine = false;
    } else if (c != '\r' && !discardUsbLine) {
      if (usbSetupLine.length() < 720) usbSetupLine += c;
      else { usbSetupLine = ""; discardUsbLine = true; }
    }
  }
}

bool bothButtonsDown() {
  return digitalRead(BTN_REC) == LOW && digitalRead(BTN_PWR) == LOW;
}

bool bothButtonsUp() {
  return digitalRead(BTN_REC) == HIGH && digitalRead(BTN_PWR) == HIGH;
}

void ARDUINO_ISR_ATTR latchButtonEdge(ButtonInput& button, int pin) {
  const bool down = digitalRead(pin) == LOW;
  if (down) button.pressLatched = true;
  const uint8_t next = (button.write + 1) % kButtonEdgeCapacity;
  if (next == button.read) {
    button.overflow = true;
    return;
  }
  button.edges[button.write].at = millis();
  button.edges[button.write].down = down;
  button.write = next;
}

void ARDUINO_ISR_ATTR latchUpperEdge() { latchButtonEdge(upperButton, BTN_REC); }
void ARDUINO_ISR_ATTR latchLowerEdge() { latchButtonEdge(lowerButton, BTN_PWR); }

// Call with GPIO interrupts disabled.
void clearButtonEdges(ButtonInput& button) {
  button.pressLatched = false;
  button.read = button.write;
  button.overflow = false;
  button.gesture.reset();
}

ButtonAction pollButtonGesture(ButtonInput& button, uint8_t completeAt) {
  for (;;) {
    noInterrupts();
    if (button.overflow) {
      // A saturated ring cannot prove a complete gesture.
      clearButtonEdges(button);
      interrupts();
      return ButtonAction::None;
    }
    if (button.read == button.write) {
      button.pressLatched = false;
      const uint32_t now = millis();
      interrupts();
      return button.gesture.poll(now, completeAt);
    }
    const ButtonEdge edge{button.edges[button.read].at, button.edges[button.read].down};
    button.read = (button.read + 1) % kButtonEdgeCapacity;
    interrupts();
    const ButtonAction action = button.gesture.edge(edge.down, edge.at, completeAt);
    if (action != ButtonAction::None) return action;
  }
}

void clearLatchedPresses() {
  noInterrupts();
  clearButtonEdges(upperButton);
  clearButtonEdges(lowerButton);
  interrupts();
}

bool takeLatchedPress(bool upper) {
  auto& button = upper ? upperButton : lowerButton;
  noInterrupts();
  const bool latched = button.pressLatched;
  clearButtonEdges(button);
  interrupts();
  return latched;
}

void powerOnPeripherals() {
  pinMode(PWR_HOLD_PIN, OUTPUT);
  digitalWrite(PWR_HOLD_PIN, HIGH);
  board.VBAT_POWER_ON();
  board.POWEER_EPD_ON();
  board.POWEER_Audio_ON();
  delay(200);
}

void stopWifi() {
  WiFi.disconnect(true);
  WiFi.mode(WIFI_OFF);
}

String localDayKey() {
  String local = utcToLocalDeviceLabel(currentUtcIso());
  return local.length() >= 10 && local != "time not set" ? local.substring(0, 10) : "";
}

bool hasQueuedAudio() {
  for (const auto& note : noteIndex) {
    if (notePendingTranscription(note)) return true;
  }
  return false;
}

void requestBacklogSync() {
  backlogRequestedAt = millis();
  backlogRequested = true;
  nextBackgroundAttemptAt = 0;
#if PALA_BRIDGE_DISCOVERY
  usbQueueChecked = false;
  triedPreferredBluetooth = false;
#endif
}

void beginRecordingSync(SyncTiming event) {
  if (!recordingSync.begin(palaSyncTiming(), event, lastActivityMs)) return;
  if (pendingBrainMemoryCount() > 0) requestBacklogSync();
  else recordingSync.finish();
}

void pollUsbBacklog() {
#if PALA_BRIDGE_DISCOVERY
  const bool dataCable = usb_serial_jtag_is_connected();
  noteUsbLinkChanged(dataCable);
  if (dataCable && !usbDataConnected && state == STATE_IDLE && recordingSyncAllowed() && !bluetoothSyncActive() &&
      !bluetoothEnrollmentOpen()) {
    stopBluetoothSync();
    requestBacklogSync();
  }
  if (!dataCable && usbDataConnected) {
    const bool wasProbing = backlogScreen.link == BacklogLink::Usb || backlogRequested;
    const bool resumeBacklog = resumeBacklogAfterUsbDetach(state == STATE_IDLE, usbSyncActive(), wasProbing);
    cancelUsbSync();
    usbSetupLine = "";
    discardUsbLine = false;
    // Discard the preceding session's retry deadline when the cable goes away.
    if (resumeBacklog && recordingSyncAllowed()) requestBacklogSync();
  }
  usbDataConnected = dataCable;
#endif
  handleUsbSetup();
}

void serviceConnectingLinks() {
  // Only the transport pumps run during panel waits. They use the same loop
  // context as usual and never draw, including during authentication.
  pollUsbBacklog();
  if (usbSyncActive()) stopBluetoothSync();
  else pollBluetoothSync();
}

bool preferUsbForBacklog() {
#if PALA_BRIDGE_DISCOVERY
  return backlogLink(usbSyncActive(), usbDataConnected,
                     backlogRequested && millis() - backlogRequestedAt < 3500,
                     WifiBacklog::Unchecked) == BacklogLink::Usb;
#else
  return false;
#endif
}

void showBacklogLink(BacklogLink link, int completed, int total, bool authenticated = false) {
  backlogScreen = {link, completed, total, authenticated};
  showBacklogProcessing(completed, total, authenticated ? "SYNCING" : "CONNECTING",
                        link, serviceConnectingLinks);
}

void renderReadyHome() {
  backlogScreen.link = BacklogLink::Deferred;
  const BatteryReading battery = readBattery();
  // The home screen only exposes recordings that are not transcribed yet.
  // Rebuilding structure/vector indexes is silent maintenance: the transcript
  // is already searchable and must not leave a permanent "waiting" badge.
  showIdle(pendingTranscriptionCount(), battery.valid ? battery.percent : -1);
  displayedHomeDay = localDayKey();
  displayedHomeUsage = homeDeliveryCounts().codexAt(time(nullptr), palaPrefersWeeklyCodexLimit());
  nextHomeClockCheckAt = millis() + 60000UL;
}

void returnFromBackgroundSync() {
  // Drop the transfer's input, then capture fresh presses as Home appears.
  clearLatchedPresses();
  idleInputArmed = false;
  renderReadyHome();
}

void returnToReady() {
  stopWifi();
  state = STATE_IDLE;
  idleInputArmed = false;
  resetActivity();
  // Discard the edge that selected Back, then retain any new edge occurring
  // while the e-paper is busy redrawing Home.
  clearLatchedPresses();
  renderReadyHome();
  audio_idle_suspend();
}

void browseAnswerPages(const String& answer, int sourceCount) {
  // The REC press that stopped speech belongs to playback, not navigation.
  // The caller has already displayed page one. Avoid another panel refresh.
  bool upperArmed = digitalRead(BTN_REC) == HIGH;
  const int pages = askAnswerPageCount(answer);
  int page = 0;
  bool lowerWasDown = digitalRead(BTN_PWR) == LOW;
  uint32_t closesAt = millis() + 15000UL;
  while (static_cast<int32_t>(millis() - closesAt) < 0) {
    if (digitalRead(BTN_REC) == HIGH) upperArmed = true;
    else if (upperArmed) {
      break;
    }
    const bool lowerDown = digitalRead(BTN_PWR) == LOW;
    if (lowerDown && !lowerWasDown) {
      page = (page + 1) % pages;
      showAskSpeakingPage(answer, sourceCount, page);
      closesAt = millis() + 15000UL;
    }
    lowerWasDown = lowerDown;
    delay(10);
  }
}

bool connectConfiguredWifi(uint8_t attempts = 20, uint32_t budgetMs = 0) {
  if (!palaWifiEnabled() || !palaHasWifi()) return false;
  const uint32_t startedAt = millis();
  const auto hasTime = [&] { return !budgetMs || millis() - startedAt < budgetMs; };
  const uint32_t clockTimeout = budgetMs ? 1200 : 4000;
  // Background processing may already have a healthy association. Calling
  // WiFi.begin again tears it down and adds an avoidable 1-3 second reconnect.
  if (WiFi.status() == WL_CONNECTED) {
    if (time(nullptr) < 1700000000) syncTimeFromNTP(clockTimeout);
    return true;
  }
  WiFi.mode(WIFI_STA);

  // Reconnect to the last working network first. A full radio scan is only a
  // fallback for roaming between saved locations and hotspots.
  const String lastSsid = palaLastWifiSsid();
  int quickProfile = -1;
  for (int profile = 0; profile < palaWifiProfileCount(); profile++) {
    if (palaWifiProfile(profile).ssid == lastSsid) { quickProfile = profile; break; }
  }
  if (quickProfile < 0 && palaWifiProfileCount() > 0) quickProfile = 0;
  if (quickProfile >= 0) {
    const PalaWifiProfile quick = palaWifiProfile(quickProfile);
    // Association commonly needs a little over one second. Giving the known
    // network a short direct window is much faster than falling into a full
    // channel scan on every wake.
    const uint8_t quickAttempts = attempts < 10 ? attempts : 10;
    WiFi.mode(WIFI_STA);
    delay(60);
    WiFi.begin(quick.ssid.c_str(), quick.password.c_str());
    for (uint8_t attempt = 0; attempt < quickAttempts && hasTime() &&
         WiFi.status() != WL_CONNECTED; attempt++) delay(200);
    if (WiFi.status() == WL_CONNECTED) {
      palaSetLastWifiSsid(quick.ssid);
      if (time(nullptr) < 1700000000) syncTimeFromNTP(clockTimeout);
      Serial.printf("[WiFi] quick reconnect: %s\n", quick.ssid.c_str());
      return true;
    }
    WiFi.disconnect(false);
    WiFi.mode(WIFI_STA);
    delay(80);
  }

  // Rank every saved network seen by the scan, then try all profiles. A scan
  // can occasionally miss an AP while its channel is busy; unseen profiles
  // remain valid fallbacks instead of silently falling back to profile zero.
  int profileRssi[PALA_MAX_WIFI_PROFILES];
  bool tried[PALA_MAX_WIFI_PROFILES];
  for (int profile = 0; profile < PALA_MAX_WIFI_PROFILES; profile++) {
    profileRssi[profile] = -2000;
    // A short direct attempt is only an optimization, not a final verdict.
    // Retry that profile after the scan as well; association can legitimately
    // take longer than the quick window.
    tried[profile] = false;
  }
  const int visible = hasTime() ? WiFi.scanNetworks(false, true, false, budgetMs ? 120 : 300) : 0;
  Serial.printf("[WiFi] scan found %d networks; %d profiles saved\n",
                visible, palaWifiProfileCount());
  for (int network = 0; network < visible; network++) {
    const String visibleSsid = WiFi.SSID(network);
    for (int profile = 0; profile < palaWifiProfileCount(); profile++) {
      const PalaWifiProfile saved = palaWifiProfile(profile);
      if (saved.ssid == visibleSsid)
        profileRssi[profile] = max(profileRssi[profile], static_cast<int>(WiFi.RSSI(network)));
    }
  }
  WiFi.scanDelete();

  const uint8_t perProfileAttempts = attempts < 4 ? 4 : (attempts < 8 ? attempts : 8);
  for (int pass = 0; pass < palaWifiProfileCount() && hasTime(); pass++) {
    int selectedProfile = -1;
    int strongestRssi = -3000;
    for (int profile = 0; profile < palaWifiProfileCount(); profile++) {
      if (!tried[profile] && profileRssi[profile] > strongestRssi) {
        strongestRssi = profileRssi[profile];
        selectedProfile = profile;
      }
    }
    if (selectedProfile < 0) break;
    tried[selectedProfile] = true;
    const PalaWifiProfile selected = palaWifiProfile(selectedProfile);
    if (!selected.ssid.length()) continue;
    Serial.printf("[WiFi] trying profile %d/%d: %s (%s)\n",
                  selectedProfile + 1, palaWifiProfileCount(), selected.ssid.c_str(),
                  strongestRssi > -2000 ? (String(strongestRssi) + " dBm").c_str() : "not seen");
    WiFi.disconnect(false);
    // scanNetworks()/disconnect() can leave ESP32 Arduino's STA interface
    // stopped even though the high-level mode still reports WIFI_STA.
    // Fully restart it for every profile before begin().
    WiFi.mode(WIFI_OFF);
    delay(40);
    WiFi.mode(WIFI_STA);
    delay(80);
    WiFi.begin(selected.ssid.c_str(), selected.password.c_str());
    // A newly encountered router may need several seconds for DHCP. Keep the
    // short fallback for unseen profiles, but allow visible saved APs six seconds.
    const uint8_t associationAttempts = strongestRssi > -2000 ? 20 : perProfileAttempts;
    for (uint8_t attempt = 0;
         attempt < associationAttempts && hasTime() && WiFi.status() != WL_CONNECTED;
         ++attempt) delay(300);
    if (WiFi.status() == WL_CONNECTED) {
      palaSetLastWifiSsid(selected.ssid);
      if (time(nullptr) < 1700000000) syncTimeFromNTP(clockTimeout);
      Serial.printf("[WiFi] connected: %s\n", selected.ssid.c_str());
      return true;
    }
    Serial.printf("[WiFi] profile failed: %s status=%d\n",
                  selected.ssid.c_str(), static_cast<int>(WiFi.status()));
  }
  stopWifi();
  Serial.println("[WiFi] no saved profile connected");
  return false;
}

int processPendingMemories(int preferredNote = -1, int maxNotes = 1) {
  const uint8_t attempts = palaBackgroundNetworkMode() ? 8 : 20;
  if (!(PALA_MAC_BRIDGE || palaHasOpenAiKey()) || !connectConfiguredWifi(attempts)) {
    stopWifi();
    return 0;
  }
  loadIndex();
  const int processed = processPendingNotes(preferredNote, maxNotes);
  loadIndex();
  // Keep one stable Wi-Fi/TLS environment while the background queue still
  // has work. Rapid WIFI_OFF -> reconnect cycles produced invalid sockets on
  // the ESP32-S3. Foreground actions still shut Wi-Fi down immediately.
  if (!palaBackgroundNetworkMode()) stopWifi();
  return processed;
}

bool refreshCompanionConnection() {
  const bool available = companionUsesHotspot ? (WiFi.getMode() & WIFI_AP) != 0
                                              : WiFi.status() == WL_CONNECTED;
  const IPAddress ip = !available ? IPAddress() : companionUsesHotspot ? WiFi.softAPIP() : WiFi.localIP();
  const String address = ip == IPAddress() ? String() : ip.toString();
  const String network = address.isEmpty() ? String() : companionUsesHotspot ? WiFi.softAPSSID() : WiFi.SSID();
  const bool changed = transferUrl != address || companionNetworkName != network;
  transferUrl = address;
  companionNetworkName = network;
  return changed;
}

void enterCompanion() {
  if (!palaWifiEnabled()) {
    showError("WI-FI OFF");
    delay(1000);
    returnToReady();
    return;
  }
  resetActivity();
  state = STATE_TRANSFER;
  companionControls.reset();
  companionGestureStartedAt = 0;
  clearLatchedPresses();
  companionDisplay = {};
  companionDisplay.updateBattery(millis(), readBattery().percent);
  showTransferConnecting(companionDisplay.batteryPercent());

  companionUsesHotspot = !connectConfiguredWifi(12);
  companionApPassword = "";
  if (companionUsesHotspot) {
    stopWifi();
    WiFi.mode(WIFI_AP);
    const String accessPointName = palaNoteName() + "-Note";
    char password[9];
    // Generate a fresh setup password independent of the public device name.
    snprintf(password, sizeof(password), "%08lX", static_cast<unsigned long>(esp_random()));
    companionApPassword = password;
    if (!WiFi.softAP(accessPointName.c_str(), companionApPassword.c_str())) {
      showError("WIFI ERROR");
      delay(1400);
      returnToReady();
      return;
    }
  } else {
    syncTimeFromNTP(5000);
  }

  setupTransferServer();
  transferServer.begin();
  transferServerActive = true;
  refreshCompanionConnection();
  nextCompanionNetworkCheckAt = millis() + 1000;
  redrawCompanionHome(false);
}

void leaveCompanion() {
  setupRequired = (!palaHasWifi() && !PALA_BRIDGE_DISCOVERY) || !(PALA_MAC_BRIDGE || palaHasOpenAiKey());
  soundBack();
  stopTransferMode();
  companionControls.reset();
  companionGestureStartedAt = 0;
  companionApPassword = "";
  companionNetworkName = "";
  state = STATE_IDLE;
  idleInputArmed = false;
  clearLatchedPresses();
  resetActivity();
  renderReadyHome();
  clearLatchedPresses();
}

void redrawCompanionHome(bool adjustingVolume) {
  const CompanionLink link = transferUrl.isEmpty() ? CompanionLink::Offline
      : companionUsesHotspot ? CompanionLink::Hotspot : CompanionLink::Wifi;
  const int overlay = adjustingVolume || companionDisplay.volumeVisible() ? palaPlaybackVolume() : -1;
  showTransferMode(link, companionNetworkName, transferUrl, companionApPassword,
                   companionDisplay.batteryPercent(), overlay);
  // Start the visible interval after the e-paper refresh finishes.
  if (adjustingVolume) companionDisplay.showVolume(millis());
}

void captureMemory() {
  constexpr uint64_t kMinimumFreeBytes = 10ULL * 1024ULL * 1024ULL;
  if (SD_MMC.totalBytes() <= SD_MMC.usedBytes() ||
      SD_MMC.totalBytes() - SD_MMC.usedBytes() < kMinimumFreeBytes) {
    showError("STORAGE FULL");
    delay(1600);
    returnToReady();
    return;
  }
  state = STATE_RECORDING;
  // E-paper refreshes are blocking. Starting one here used to delay I2S reads
  // and cut the first spoken words from the recording.
  palaSoundSetEnabled(false);
  const String capturedUtc = currentUtcIso();
  const bool recorded = record();
  palaSoundSetEnabled(true);

  if (!recorded) {
    showError("RECORD AGAIN");
    delay(1300);
    returnToReady();
    return;
  }

  if (!saveTag(lastRecNum, "Inbox", capturedUtc)) {
    // The WAV is retained for orphan recovery; never claim a failed index
    // commit succeeded or launch cloud work against an uncommitted capture.
    showError("STORAGE FULL"); delay(1300); returnToReady(); return;
  }
  soundSaved();
  showSaved(lastRecNum);
  delay(350);

  // A new recording needs its own scheduling event, including in Manual only.
  recordingSync.finish();
  backlogRequested = false;
  backlogSyncActive = false;
  backlogSyncTotal = 0;
  returnToReady();
  beginRecordingSync(SyncTiming::AfterRecording);
}

void askBrain() {
  if (!palaHasWifi() || !(PALA_MAC_BRIDGE || palaHasOpenAiKey())) {
    enterCompanion();
    return;
  }

  state = STATE_ASKING;
  constexpr const char* kQuestionPath = NOTES_DIR "/.ask.wav";
  constexpr const char* kAnswerPath = NOTES_DIR "/.pala-answer.wav";

  palaSoundSetEnabled(false);
  const bool recorded = recordQuestion(kQuestionPath, BTN_PWR);
  palaSoundSetEnabled(true);
  if (!recorded) {
    showError("ASK AGAIN");
    delay(1200);
    returnToReady();
    return;
  }

  showAskProgress("CONNECTING", nullptr);
  if (!connectConfiguredWifi()) {
    stopWifi();
    showError("NO WIFI");
    delay(1400);
    returnToReady();
    return;
  }

#if PALA_BRIDGE_DISCOVERY
  if (wifiBridgeTranscriptionOnly()) {
    stopWifi();
    showError("KUBO DISABLED");
    delay(1400);
    returnToReady();
    return;
  }
#endif
  loadIndex();

  String question;
  String answer;
  std::vector<int> sources;
  showAskProgress("DECODING", "YOUR QUESTION");
  const bool questionReady = transcribeQuestion(kQuestionPath, question);
  if (!questionReady) {
    stopWifi();
    showError("ASK AGAIN");
    delay(1400);
    returnToReady();
    return;
  }
  bool answered = false;
  int taskSource = -1;
  if (applyBrainTaskCommand(question, answer, taskSource)) {
    answered = true;
    if (taskSource >= 0) sources.push_back(taskSource);
  } else if (answerAgendaQuestion(question, answer, sources)) {
    answered = true;
    // Date-sensitive agenda questions are answered from the same local source
    // as the Today screen, avoiding broad semantic matches from other days.
  } else {

  // If the newest capture has not completed its background transcription yet,
  // transcribe only that one. Raw transcripts are immediately searchable; Ask
  // never waits for the complete organization or embedding backlog.
  String recentQuestion = question;
  recentQuestion.toLowerCase();
  const bool asksForNewest = recentQuestion.indexOf("gerade") >= 0 ||
                             recentQuestion.indexOf("eben") >= 0 ||
                             recentQuestion.indexOf("zuletzt") >= 0 ||
                             recentQuestion.indexOf("latest") >= 0 ||
                             recentQuestion.indexOf("most recent") >= 0;
  if (asksForNewest && !noteIndex.empty() && !noteIndex.back().hasText) {
    const int newest = noteIndex.back().num;
    char newestPath[64];
    snprintf(newestPath, sizeof(newestPath), "%s/note_%03d.wav", NOTES_DIR, newest);
    showAskProgress("PREPARING", "LATEST THOUGHT");
    transcribe(String(newestPath), newest);
    loadIndex();
  }
    showAskProgress("SEARCHING", "YOUR MEMORY");
    answered = answerFromBrain(question, answer, sources);
  }

  if (!answered) {
    stopWifi();
    showError("NO ANSWER");
    delay(1400);
    returnToReady();
    return;
  }

#if PALA_PRIVATE_DIAGNOSTICS
  Serial.printf("[Ask] %s\n[Answer] %s\n", question.c_str(), answer.c_str());
#endif
  showAskProgress("BUILDING", "VOICE");
  const bool speechReady = playBrainSpeech(answer, kAnswerPath, static_cast<int>(sources.size()));
  if (!speechReady) {
    stopWifi();
    showError("VOICE FAILED");
    delay(1000);
    showAskSpeaking(answer, static_cast<int>(sources.size()));
  }
  stopWifi();
  browseAnswerPages(answer, static_cast<int>(sources.size()));
  returnToReady();
}

void playDailyBriefing() {
  if (!palaHasWifi() || !(PALA_MAC_BRIDGE || palaHasOpenAiKey())) {
    enterCompanion();
    return;
  }
  state = STATE_ASKING;
  constexpr const char* kBriefingPath = NOTES_DIR "/.pala-briefing.wav";
  showAskProgress("CONNECTING", "TODAY");
  if (!connectConfiguredWifi()) {
    stopWifi();
    showError("NO WIFI");
    delay(1400);
    returnToReady();
    return;
  }
  loadIndex();
  showAskProgress("THINKING", "DAILY BRIEF");
  String answer;
  int tasks = 0, events = 0;
  if (!buildDailyBriefing(answer, tasks, events)) {
    stopWifi();
    showError("BRIEF FAILED");
    delay(1400);
    returnToReady();
    return;
  }
  Serial.printf("[Briefing] %d tasks, %d events; answer=%u characters\n", tasks, events, answer.length());
  showAskProgress("BUILDING", "VOICE");
  const bool speechReady = playBrainSpeech(answer, kBriefingPath, tasks + events, true);
  if (!speechReady) {
    stopWifi();
    showError("VOICE FAILED");
    delay(1000);
    showAskSpeaking(answer, tasks + events);
  }
  stopWifi();
  browseAnswerPages(answer, tasks + events);
  returnToReady();
}

void playWeeklyBriefing() {
  if (!palaHasWifi() || !(PALA_MAC_BRIDGE || palaHasOpenAiKey())) {
    enterCompanion();
    return;
  }
  state = STATE_ASKING;
  constexpr const char* kBriefingPath = NOTES_DIR "/.pala-weekly.wav";
  showAskProgress("CONNECTING", "THIS WEEK");
  if (!connectConfiguredWifi()) {
    stopWifi();
    showError("NO WIFI");
    delay(1400);
    returnToReady();
    return;
  }
  loadIndex();
  showAskProgress("THINKING", "WEEKLY BRIEF");
  String answer;
  int tasks = 0, events = 0;
  if (!buildWeeklyBriefing(answer, tasks, events)) {
    stopWifi();
    showError("BRIEF FAILED");
    delay(1400);
    returnToReady();
    return;
  }
  showAskProgress("BUILDING", "VOICE");
  const bool speechReady = playBrainSpeech(answer, kBriefingPath, tasks + events, true);
  if (!speechReady) {
    stopWifi();
    showError("VOICE FAILED");
    delay(1000);
    showAskSpeaking(answer, tasks + events);
  }
  stopWifi();
  browseAnswerPages(answer, tasks + events);
  returnToReady();
}

PressResult classifyPress(int pin) {
  if (digitalRead(pin) != LOW) return PressResult::Released;
  delay(14);
  if (digitalRead(pin) != LOW) return PressResult::Released;
  const uint32_t pressedAt = millis();
  while (digitalRead(pin) == LOW && millis() - pressedAt < REC_HOLD_MS) {
    // A two-button gesture always wins, even when the fingers land a little
    // apart. The old blocking hold detector missed this transition and began
    // Capture/Ask before Companion could see both buttons.
    if (bothButtonsDown()) {
      companionGestureStartedAt = millis();
      return PressResult::Chord;
    }
    delay(4);
  }
  return digitalRead(pin) == LOW ? PressResult::LongSingle : PressResult::Released;
}

void showAgendaPage() {
  if (displayPageIndex < 0) {
    showTodayAgenda(true, agendaPage, AgendaNavigation::EventsShortcut);
  } else {
    const auto page = static_cast<DisplayPage>(activeDisplayPages[displayPageIndex]);
    const bool morePages = nextDisplayPageIndex(activeDisplayPages.c_str(), displayPageIndex) >= 0;
    if (page == DisplayPage::Tasks || page == DisplayPage::Events) {
      const auto navigation = morePages ? AgendaNavigation::MoreDisplayPages : AgendaNavigation::LastDisplayPage;
      showTodayAgenda(page == DisplayPage::Events, agendaPage, navigation);
    } else {
      const int offset = page == DisplayPage::CurrentMonth ? 0 : page == DisplayPage::NextMonth ? 1 : 2;
      showCalendarMonth(offset, morePages);
    }
  }
  agendaShownAt = millis();
}

void advanceDisplayPage(bool fromHome) {
  if (fromHome) {
    activeDisplayPages = palaDisplayPages();
    displayPageIndex = -1;
  }

  const int next = nextDisplayPageIndex(activeDisplayPages.c_str(), displayPageIndex,
                                       palaDisplayPagesWrap());
  if (next == displayPageIndex) return;
  displayPageIndex = next;
  if (displayPageIndex < 0) {
    if (!fromHome) returnToReady();
    return;
  }
  if (fromHome) takeLatchedPress(false);
  state = STATE_AGENDA;
  agendaPage = 0;
  showAgendaPage();
}

void enterAquarium() {
  stopBluetoothSync();
  stopWifi();
  clearLatchedPresses();
  state = STATE_AQUARIUM;
  aquarium.open(millis());
  aquariumChord = false;
  lastAquariumFrame = -1;
}

void handleAquarium() {
  const uint32_t now = millis();
  ButtonAction upper = ButtonAction::None;
  ButtonAction lower = ButtonAction::None;
  if (bothButtonsDown()) aquariumChord = true;
  if (aquariumChord) {
    clearLatchedPresses();
    if (bothButtonsUp()) aquariumChord = false;
  } else {
    upper = pollButtonGesture(upperButton, 2);
    // Real press and release times survive a blocking e-paper refresh.
    lower = pollButtonGesture(lowerButton, 1);
  }

  if (upper != ButtonAction::None && upper != ButtonAction::Hold) {
    aquarium.upper(upper, now);
    resetActivity();
  } else if (!upperButton.gesture.pending() && aquarium.lower(lower, now)) {
    resetActivity();
  }
  aquarium.advance(now, upperButton.gesture.pending() || lowerButton.gesture.pending());
  if (aquarium.scene == AquariumScene::Closed) {
    returnToReady();
    return;
  }

  // The panel is the pacing limit. Skip late frames, never replay a backlog.
  if (lastAquariumFrame >= 0 && now - lastAquariumPaintAt < 170) return;
  const int frame = aquariumFrame(aquarium.scene, now - aquarium.since);
  if (frame == lastAquariumFrame) return;
  if (!showAquariumFrame(frame, pollUsbBacklog)) {
    Serial.println("Aquarium frame unavailable");
    returnToReady();
    return;
  }
  lastAquariumFrame = frame;
  lastAquariumPaintAt = now;
  if (aquarium.scene == AquariumScene::Peek) aquarium.since = millis();
}

void handleIdle() {
  if (!idleInputArmed) {
    // Fresh edges captured during a Home refresh belong to Home.
    if (upperButton.pressLatched || lowerButton.pressLatched) idleInputArmed = true;
    else {
      if (bothButtonsUp()) idleInputArmed = true;
      return;
    }
  }

  if (bothButtonsDown()) {
    clearLatchedPresses();
    if (companionGestureStartedAt == 0) companionGestureStartedAt = millis();
    if (millis() - companionGestureStartedAt >= COMPANION_HOLD_MS) {
      soundSelect();
      enterCompanion();
    }
    return;
  }
  if (companionGestureStartedAt) {
    if (!bothButtonsUp()) return;
    companionGestureStartedAt = 0;
    clearLatchedPresses();
    resetActivity();
    if (bluetoothEnrollmentAvailable()) {
      stopBluetoothSync();
      if (!startBluetoothEnrollment()) {
        showError("BT UNAVAILABLE");
        renderReadyHome();
      }
    }
    return;
  }

  if (upperButton.pressLatched || lowerButton.pressLatched) resetActivity();

  const ButtonAction upper = pollButtonGesture(upperButton, 3);
  if (upper != ButtonAction::None) {
    switch (upper) {
      case ButtonAction::Single: advanceDisplayPage(true); break;
      case ButtonAction::Double:
        takeLatchedPress(false);
        playDailyBriefing();
        break;
      case ButtonAction::Triple:
        beginRecordingSync(SyncTiming::Manual);
        break;
      case ButtonAction::Hold:
        if (digitalRead(BTN_REC) == LOW) {
          // Recording wins over unfinished clicks on Home.
          takeLatchedPress(false);
          captureMemory();
        }
        break;
      case ButtonAction::None: break;
    }
    return;
  }
  if (upperButton.gesture.pending()) return;

  switch (pollButtonGesture(lowerButton, 3)) {
    case ButtonAction::None: break;
    case ButtonAction::Single:
#if PALA_BRIDGE_DISCOVERY
      if (palaDisplayPages().indexOf(static_cast<char>(DisplayPage::Events)) < 0) break;
#endif
      state = STATE_AGENDA;
      displayPageIndex = -1;
      agendaPage = 0;
      showAgendaPage();
      break;
    case ButtonAction::Double:
      playWeeklyBriefing();
      break;
    case ButtonAction::Triple:
      if (!backlogSyncActive && backlogScreen.link == BacklogLink::Deferred) enterAquarium();
      break;
    case ButtonAction::Hold:
      if (digitalRead(BTN_PWR) == LOW) askBrain();
      break;
  }
}

void handleAgenda() {
  if (bothButtonsDown()) {
    clearLatchedPresses();
    if (companionGestureStartedAt == 0) companionGestureStartedAt = millis();
    if (millis() - companionGestureStartedAt >= COMPANION_HOLD_MS) enterCompanion();
    return;
  }
  if (companionGestureStartedAt) {
    if (!bothButtonsUp()) return;
    companionGestureStartedAt = 0;
    clearLatchedPresses();
    agendaShownAt = millis();
    return;
  }

  if (displayPageIndex >= 0) {
    if (upperButton.pressLatched) agendaShownAt = millis();
    // Preserve the press timestamps across blocking e-paper refreshes.
    const ButtonAction upper = pollButtonGesture(upperButton, 1);
    if (upper == ButtonAction::Hold) {
      if (digitalRead(BTN_REC) == LOW) captureMemory();
      return;
    }
    if (upper == ButtonAction::Single) {
      resetActivity();
      const auto page = static_cast<DisplayPage>(activeDisplayPages[displayPageIndex]);
      if ((page == DisplayPage::Tasks || page == DisplayPage::Events) &&
          agendaPage + 1 < todayAgendaPageCount(page == DisplayPage::Events)) {
        ++agendaPage;
        showAgendaPage();
      } else {
        advanceDisplayPage(false);
      }
      return;
    }
    if (upperButton.gesture.pending()) return;
  } else if (digitalRead(BTN_REC) == LOW || (upperButton.pressLatched && !lowerButton.pressLatched)) {
    takeLatchedPress(true);
    if (digitalRead(BTN_REC) == LOW && classifyPress(BTN_REC) == PressResult::Chord) return;
    returnToReady();
    return;
  }

  if (digitalRead(BTN_PWR) == LOW || lowerButton.pressLatched) {
    takeLatchedPress(false);
    if (digitalRead(BTN_PWR) == LOW && classifyPress(BTN_PWR) == PressResult::Chord) return;
    resetActivity();
    if (displayPageIndex < 0 && agendaPage + 1 < todayAgendaPageCount(true)) {
      ++agendaPage;
      showAgendaPage();
    } else returnToReady();
    return;
  }

  if (millis() - agendaShownAt >= 30000UL) returnToReady();
}

#if PALA_BRIDGE_DISCOVERY
bool yieldBacklogToUsb() {
  if (!preferUsbForBacklog()) return false;
  stopWifi();
  backlogSyncActive = false;
  backlogSyncTotal = 0;
  return true;
}
#endif

#if PALA_BRIDGE_DISCOVERY
bool startBacklogBluetooth(uint32_t retryDelay) {
  if (!startBluetoothSync()) return false;
  backlogSyncActive = false;
  backlogSyncTotal = 0;
  nextBackgroundAttemptAt = millis() + retryDelay;
  // The peer supplies its processing capabilities after authentication.
  showBacklogLink(BacklogLink::Bluetooth, 0, 0);
  return true;
}
#endif

void handleBackgroundBrain(const IdleSleepPolicy& sleep) {
  // Do not launch another network stage after the sleep budget has expired.
  // A stage already running is allowed to finish and save its result safely.
  const auto sleepDue = [&] {
    return !recordingSync.permits(palaSyncTiming(), millis(), lastActivityMs, sleep);
  };
  if (sleepDue()) return;
  if ((!palaHasWifi() && !PALA_BRIDGE_DISCOVERY) || !(PALA_MAC_BRIDGE || palaHasOpenAiKey())) return;
  if (nextBackgroundAttemptAt && static_cast<int32_t>(millis() - nextBackgroundAttemptAt) < 0) return;
  if (!backlogRequested && millis() - lastActivityMs < BACKGROUND_IDLE_MS) return;
  if (!bothButtonsUp()) return;
#if PALA_BRIDGE_DISCOVERY
  // Give the Windows watcher a full poll interval plus time to authenticate.
  if (preferUsbForBacklog()) {
    if (!usbQueueChecked) {
      usbQueueChecked = true;
      const int total = pendingTranscriptionCount();
      if (total > 0) showBacklogLink(BacklogLink::Usb, 0, total);
    }
    return;
  }
#endif
  backlogRequested = false;
  // This scan touches per-note metadata, so never perform it in every 8 ms
  // idle-loop iteration. At scale it must only run when a pass is actually due.
  int pendingBefore = pendingBrainMemoryCount();
  int indexingNote = -1;
  // Optional search maintenance must never extend the normal sleep window.
  if (pendingBefore == 0 && millis() - lastActivityMs < sleep.homeMs) {
    for (const NoteEntry& note : noteIndex) {
      if (note.hasText && brainMemoryIsCurrent(note.num) &&
          !brainEmbeddingExists(note.num) && noteQueueRetryDue(note.num)) {
        indexingNote = note.num;
        pendingBefore = 1;
        break;
      }
    }
  }
  if (pendingBefore == 0) {
    recordingSync.finish();
    stopWifi();
    if (backlogSyncActive) {
      showBacklogProcessing(backlogSyncTotal, backlogSyncTotal, "DONE");
      delay(900);
      backlogSyncActive = false;
      backlogSyncTotal = 0;
      returnFromBackgroundSync();
    } else if (backlogScreen.link != BacklogLink::Deferred) {
      renderReadyHome();
    }
    nextBackgroundAttemptAt = millis() + BACKGROUND_RETRY_MS;
    return;
  }

  // Never perform an invisible network operation behind an apparently idle
  // home screen. Pick the exact durable item this pass will work on and show
  // its real stage before a TLS request can temporarily occupy the device.
  int visibleNote = indexingNote;
  const char* visiblePhase = "REMEMBERING";
  // Rotate the selected item too: passing a preferred note bypasses the
  // network worker's own rotation. A bad newest note must not starve old ones.
  // Only one network stage runs per pass so buttons and the e-paper UI remain
  // responsive between transcription and organization steps.
  static size_t queueCursor = 0;
  for (size_t offset = 0; indexingNote < 0 && offset < noteIndex.size(); ++offset) {
    const size_t index = (queueCursor + offset) % noteIndex.size();
    const NoteEntry& note = noteIndex[index];
    if (noteQueueRetryDue(note.num)) {
      if (!note.hasText) { visiblePhase = "DECODING"; visibleNote = note.num; }
      else if (!brainMemoryIsCurrent(note.num)) { visiblePhase = "UNDERSTANDING"; visibleNote = note.num; }
    }
    if (visibleNote >= 0) { queueCursor = (index + 1) % noteIndex.size(); break; }
  }
#if PALA_BRIDGE_DISCOVERY
  // Wi-Fi backoff can leave no runnable Wi-Fi stage. A paired Bridge can still
  // fetch the same WAV over Bluetooth if the Wi-Fi Bridge is unavailable.
  const bool waitingAudio = visibleNote < 0 && hasQueuedAudio();
#else
  constexpr bool waitingAudio = false;
#endif
  if (visibleNote < 0 && !waitingAudio) {
    stopWifi();
    if (backlogSyncActive || backlogScreen.link != BacklogLink::Deferred) {
      backlogSyncActive = false;
      backlogSyncTotal = 0;
      returnFromBackgroundSync();
    }
    nextBackgroundAttemptAt = millis() + BACKGROUND_RETRY_MS;
    return;
  }
  if (!backlogSyncActive) {
    backlogSyncActive = true;
    backlogSyncTotal = pendingBefore;
  } else if (pendingBefore > backlogSyncTotal) {
    backlogSyncTotal = pendingBefore;
  }
  int completedBefore = max(0, backlogSyncTotal - pendingBefore);
  bool transcribeOnly = false;
  const auto deferForSleep = [&] {
    if (!sleepDue()) return false;
    stopWifi();
    backlogSyncActive = false;
    backlogSyncTotal = 0;
    renderReadyHome();
    return true;
  };
  // Association and an authenticated Bridge readiness reply are separate.
#if PALA_BRIDGE_DISCOVERY
  const bool bluetoothWork = hasQueuedAudio() || (visibleNote >= 0 && indexingNote < 0);
  if (tryPreferredBluetooth(palaPreferBluetooth(), triedPreferredBluetooth, bluetoothWork)) {
    pollUsbBacklog();
    if (yieldBacklogToUsb() || deferForSleep()) return;
    triedPreferredBluetooth = true;
    stopWifi();
    // An unused preferred Bluetooth window falls through to Wi-Fi immediately.
    if (startBacklogBluetooth(0)) return;
  }
  WifiBacklog wifi = WifiBacklog::Unavailable;
  if (palaWifiEnabled() && palaHasWifi()) {
    showBacklogLink(BacklogLink::Wifi, completedBefore, backlogSyncTotal);
    if (yieldBacklogToUsb()) return;
    if (!sleepDue() && connectConfiguredWifi(8, 4500) && !sleepDue()) wifi = pairedWifiBacklog();
  }
  pollUsbBacklog();
  if (yieldBacklogToUsb()) return;
  if (!sleepDue() && wifi != WifiBacklog::Unavailable && homeNoteNameNeedsRefresh()) refreshHomeNoteName();
  if (deferForSleep()) return;
  const BacklogLink link = pairedBacklogAfterWifi(wifi, visibleNote >= 0, !triedPreferredBluetooth && bluetoothWork);
  if (link != BacklogLink::Wifi) {
    stopWifi();
    if (link == BacklogLink::Bluetooth &&
        startBacklogBluetooth(BACKGROUND_RETRY_MS)) return;
#else
  showBacklogLink(BacklogLink::Wifi, completedBefore, backlogSyncTotal);
  const bool connected = !sleepDue() && connectConfiguredWifi(8);
  if (deferForSleep()) return;
  if (!connected) {
#endif
    showBacklogProcessing(completedBefore, backlogSyncTotal, "QUEUED");
    delay(900);
    backlogSyncActive = false;
    backlogSyncTotal = 0;
    nextBackgroundAttemptAt = millis() + BACKGROUND_RETRY_MS;
    returnFromBackgroundSync();
    return;
  }
#if PALA_BRIDGE_DISCOVERY
  transcribeOnly = wifiBridgeTranscriptionOnly();
  if (transcribeOnly) {
    pendingBefore = pendingTranscriptionCount();
    if (pendingBefore == 0) {
      if (!sleepDue()) refreshHomeHolidays();
      recordingSync.finish();
      stopWifi();
      backlogSyncActive = false;
      backlogSyncTotal = 0;
      nextBackgroundAttemptAt = millis() + BACKGROUND_RETRY_MS;
      returnFromBackgroundSync();
      return;
    }
    indexingNote = -1;
    visibleNote = -1;
    visiblePhase = "DECODING";
    for (const auto& note : noteIndex) {
      if (notePendingTranscription(note) && noteQueueRetryDue(note.num)) {
        visibleNote = note.num;
        break;
      }
    }
    backlogSyncTotal = max(backlogSyncTotal, pendingBefore);
    completedBefore = max(0, backlogSyncTotal - pendingBefore);
  }
#endif
  backlogScreen.link = BacklogLink::Deferred;
  showBacklogProcessing(completedBefore, backlogSyncTotal, visiblePhase);
  if (deferForSleep()) return;

  // Wi-Fi and Arduino's TCP stack must stay on the normal loop context.
  // Running this on a separate core starved IDLE0 and repeatedly crashed the
  // socket layer. Process one durable recording per bounded idle pass instead.
  palaSetBackgroundNetworkMode(true);
  const int processed = processPendingMemories(visibleNote, 1);
  palaSetBackgroundNetworkMode(false);
  // Leaving the runnable queue is not proof of success: permanent errors are
  // blocked too. Do not count such a transition as a completed memory.
  if (visibleNote >= 0 && indexingNote < 0 && noteQueueState(visibleNote) == "blocked" &&
      !brainMemoryIsCurrent(visibleNote)) {
    stopWifi();
    showError(noteQueueError(visibleNote) == "no speech recognized" ? "NO SPEECH" : "PROCESSING FAILED");
    delay(900);
    backlogSyncActive = false;
    backlogSyncTotal = 0;
    nextBackgroundAttemptAt = millis() + BACKGROUND_RETRY_MS;
    returnFromBackgroundSync();
    return;
  }
  const int pendingAfter = transcribeOnly ? pendingTranscriptionCount() :
      indexingNote >= 0 ? (processed > 0 ? 0 : 1) : pendingBrainMemoryCount();
  if (pendingAfter == 0) {
#if PALA_BRIDGE_DISCOVERY
    // The recording is durable. Use this paired link before closing it.
    if (!sleepDue()) refreshHomeHolidays();
#endif
    recordingSync.finish();
    stopWifi();
    showBacklogProcessing(backlogSyncTotal, backlogSyncTotal, "DONE");
    delay(900);
    backlogSyncActive = false;
    backlogSyncTotal = 0;
    returnFromBackgroundSync();
    nextBackgroundAttemptAt = millis() + BACKGROUND_RETRY_MS;
  } else if (processed > 0) {
    showBacklogProcessing(max(0, backlogSyncTotal - pendingAfter), backlogSyncTotal, visiblePhase);
    nextBackgroundAttemptAt = millis() + BACKGROUND_IDLE_MS;
  } else {
    stopWifi();
    if (indexingNote >= 0) showBacklogProcessing(backlogSyncTotal, backlogSyncTotal, "DONE");
    else showBacklogProcessing(max(0, backlogSyncTotal - pendingAfter), backlogSyncTotal, "QUEUED");
    delay(900);
    backlogSyncActive = false;
    backlogSyncTotal = 0;
    nextBackgroundAttemptAt = millis() + BACKGROUND_RETRY_MS;
    returnFromBackgroundSync();
  }
}

bool canFollowHomeDeliveryOverBluetooth() {
#if PALA_BRIDGE_DISCOVERY
  return palaBluetoothEnabled() && !usbDataConnected && notePeerCount() > 0 &&
         (!palaWifiEnabled() || !palaHasWifi() || homeBridgeStatus.latest() == HomeBridgeLink::Bluetooth);
#else
  return false;
#endif
}

void refreshHomeMetadataWhenIdle(uint32_t homeSleepMs) {
#if PALA_BRIDGE_DISCOVERY
  const uint32_t now = millis();
  if (!palaWifiEnabled() || !palaHasWifi() || backlogRequested || backlogSyncActive ||
      now - lastActivityMs < BACKGROUND_IDLE_MS ||
      now - lastActivityMs >= homeSleepMs - 12000 ||
      (nextHomeWeatherCheckAt && static_cast<int32_t>(now - nextHomeWeatherCheckAt) < 0) ||
      !bothButtonsUp() || upperButton.pressLatched || lowerButton.pressLatched) return;
  nextHomeWeatherCheckAt = now + 15000;
  const int64_t utc = time(nullptr);
  const String day = localDayKey();
  const bool weatherDue = homeForecastNeedsRefresh(day, utc);
  const bool holidayDue = homeHolidaysNeedRefresh(day.substring(0, 4).toInt());
  const bool nameDue = homeNoteNameNeedsRefresh();
  const bool deliveryDue = homeDeliveryNeedsRefresh(now);
  const bool deliveryFollowup = homeDeliveryNeedsFollowup(now);
  const bool onCooldown = utc < retryHomeWeatherAfter && retryHomeWeatherAfter - utc <= 300;
  const bool routineDue = !onCooldown && (weatherDue || nameDue || holidayDue);
  if ((!routineDue && !deliveryDue && !deliveryFollowup) ||
      (recordingSyncAllowed() && hasQueuedAudio())) return;
  if (routineDue) retryHomeWeatherAfter = utc + 300;
  if (deliveryDue) homeDeliveryRefreshAttempted(now);
  bool updated = false;
  if (connectConfiguredWifi(8, 4500)) {
    if (routineDue && nameDue) updated = refreshHomeNoteName();
    if (routineDue && weatherDue) updated = refreshHomeForecast() || updated;
    if (deliveryFollowup || deliveryDue) updated = refreshHomeDelivery() || updated;
    if (deliveryDue || (routineDue && holidayDue)) updated = refreshHomeHolidays() || updated;
  }
  stopWifi();
  if (updated && bothButtonsUp() && !upperButton.pressLatched && !lowerButton.pressLatched) renderReadyHome();
#endif
}

void handleCompanion() {
  if (transferServerActive) transferServer.handleClient();
  if (!palaWifiEnabled()) {
    // Give the settings reply time to leave before shutting down the radio.
    delay(250);
    leaveCompanion();
    return;
  }

  const bool upperDown = digitalRead(BTN_REC) == LOW;
  const bool lowerDown = digitalRead(BTN_PWR) == LOW;
  if (upperDown || lowerDown) resetActivity();
  const uint32_t now = millis();
  if (now - lastActivityMs >= idleSleepPolicy(palaIdleSleepMinutes()).companionMs) {
    leaveCompanion();
    enterUltraSleep();
    return;
  }

  const CompanionAction action = companionControls.poll(now, upperDown, lowerDown, COMPANION_HOLD_MS);
  if (action == CompanionAction::Exit) {
    leaveCompanion();
    return;
  }

  const bool adjustingVolume = action == CompanionAction::Louder || action == CompanionAction::Quieter;
  if (adjustingVolume) {
    const int before = palaPlaybackVolume();
    const int volume = constrain(before + (action == CompanionAction::Louder ? 20 : -20), 20, 100);
    if (volume != before) palaSetPlaybackVolume(volume);
  }

  bool connectionChanged = false;
  bool batteryChanged = false;
  if (!upperDown && !lowerDown) {
    if (static_cast<int32_t>(now - nextCompanionNetworkCheckAt) >= 0) {
      nextCompanionNetworkCheckAt = now + 1000;
      connectionChanged = refreshCompanionConnection();
    }
    if (companionDisplay.batterySampleDue(now)) {
      batteryChanged = companionDisplay.updateBattery(now, readBattery().percent);
    }
  }
  const bool volumeDismissed = companionDisplay.dismissExpiredVolume(now);
  if (adjustingVolume || connectionChanged || batteryChanged || volumeDismissed) {
    redrawCompanionHome(adjustingVolume);
  }
}

void initializeDisplay() {
  custom_lcd_spi_t config = {};
  config.cs = EPD_CS_PIN;
  config.dc = EPD_DC_PIN;
  config.rst = EPD_RST_PIN;
  config.busy = EPD_BUSY_PIN;
  config.mosi = EPD_MOSI_PIN;
  config.scl = EPD_SCK_PIN;
  config.spi_host = EPD_SPI_NUM;
  config.buffer_len = (200 * 200) / 8;

  display = new epaper_driver_display(200, 200, config);
  // Do not visibly clear the panel here. The first actual UI frame establishes
  // the controller base image, avoiding two slow full refreshes during wake.
}

void initializeStorage() {
  SD_MMC.setPins(SD_CLK, SD_CMD, SD_D0);
  if (!SD_MMC.begin("/sdcard", true)) {
    showError("SD ERROR");
    while (true) delay(1000);
  }
  if (!SD_MMC.exists(NOTES_DIR)) SD_MMC.mkdir(NOTES_DIR);
  // Only boot performs the comparatively expensive orphan-WAV recovery scan.
  // Normal reloads read the compact catalog, while search/vector indexes are
  // rebuilt lazily by the first operation that actually needs them.
  loadIndex(true);
  loadHomeForecast();
  Serial.printf("[SD] %d memories\n", static_cast<int>(noteIndex.size()));
}

bool pollPairingOverlay() {
  if (takeNoteBleEnableRequest() && state == STATE_TRANSFER && !usbSyncActive()) {
    if (!bluetoothEnrollmentOpen()) stopBluetoothSync();
    startBluetoothEnrollment();
  }
  const bool ble = bluetoothEnrollmentOpen() && pollBluetoothSync();
  String peer;
  uint32_t code = 0;
  PairingView next = PairingView::None;
  if (noteUsbPairingPrompt(peer)) next = PairingView::UsbPrompt;
  else if (noteUsbPairingOpen()) next = PairingView::UsbWait;
  else if (ble) next = bluetoothEnrollmentPrompt(code) ? PairingView::BluetoothCode : PairingView::BluetoothWait;

  if (next != pairingView || (next == PairingView::BluetoothCode && code != pairingDisplayedCode)) {
    clearLatchedPresses();
    pairingButtonsArmed = false;
    pairingDisplayedCode = code;
    pairingView = next;
    if (next == PairingView::UsbPrompt) showNoteUsbPairingPrompt(peer);
    else if (next == PairingView::BluetoothCode) showNoteBluetoothPairingNumber(code);
    else if (next == PairingView::BluetoothWait || next == PairingView::UsbWait) {
      showNotePairingWait(next == PairingView::BluetoothWait);
    } else if (state == STATE_TRANSFER) {
      companionControls.reset();
      refreshCompanionConnection();
      companionDisplay.dismissExpiredVolume(millis());
      redrawCompanionHome(false);
    }
    else if (setupRequired) showSetupRequired();
    else renderReadyHome();
    // Edges captured while e-paper was updating cannot approve a new prompt.
    clearLatchedPresses();
  }
  if (next == PairingView::None) return false;
  if (state == STATE_TRANSFER && transferServerActive) transferServer.handleClient();
  resetActivity();
  if (!pairingButtonsArmed) {
    if (bothButtonsUp()) pairingButtonsArmed = true;
    return true;
  }
  const bool upper = takeLatchedPress(true);
  const bool lower = takeLatchedPress(false);
  if (next == PairingView::UsbPrompt && (upper || lower)) noteUsbPairingConfirm(upper && !lower);
  else if (next == PairingView::BluetoothCode && (upper || lower)) {
    bluetoothEnrollmentConfirm(upper && !lower);
    if (lower) stopBluetoothSync();
  } else if (next == PairingView::UsbWait && lower) noteUsbPairingConfirm(false);
  else if (next == PairingView::BluetoothWait && lower) stopBluetoothSync();
  return true;
}

}  // namespace

bool recordingSyncAllowed() {
  return recordingSync.permits(palaSyncTiming(), millis(), lastActivityMs,
                               idleSleepPolicy(palaIdleSleepMinutes()));
}

void setup() {
  homeBridgeStatus.reset();
  Serial.begin(115200);
  delay(40);
  Serial.println("\n=== Pala Note " FIRMWARE_VERSION " ===");

  releasePeripheralPowerHolds();
  palaSettingsBegin();
  loadHomeHolidays();
  importFlashSetup();
  noteConfigSeedEntropy();
  batteryBegin();
  pinMode(BTN_REC, INPUT_PULLUP);
  pinMode(BTN_PWR, INPUT_PULLUP);
  attachInterrupt(digitalPinToInterrupt(BTN_REC), latchUpperEdge, CHANGE);
  attachInterrupt(digitalPinToInterrupt(BTN_PWR), latchLowerEdge, CHANGE);
  board.VBAT_POWER_ON();

  const bool wokeFromUltraSleep = esp_sleep_get_wakeup_cause() == ESP_SLEEP_WAKEUP_EXT1;
  Serial.printf("[Wake] cause=%d\n", static_cast<int>(esp_sleep_get_wakeup_cause()));
  if (wokeFromUltraSleep) {
    // EXT1 keeps the pin that caused wake-up even when a quick tap has already
    // been released by the time Arduino setup runs. Reading only digitalRead()
    // here lost exactly those quick lower-button taps.
    const uint64_t wakePins = esp_sleep_get_ext1_wakeup_status();
    Serial.printf("[Wake] ext1 pins=0x%llx source=%s\n",
                  static_cast<unsigned long long>(wakePins),
                  (wakePins & (1ULL << BTN_PWR)) ? "pwr" : "unknown");
  }

  resetActivity();
  powerOnPeripherals();
  initializeDisplay();

  const size_t psramBytes = ESP.getPsramSize();
  Serial.printf("[HW] Flash: %u MB, PSRAM: %u MB\n",
                static_cast<unsigned>(ESP.getFlashChipSize() / (1024 * 1024)),
                static_cast<unsigned>(psramBytes / (1024 * 1024)));
  if (!psramFound() || psramBytes < 8UL * 1024UL * 1024UL) {
    showError("8/8 ONLY");
    while (true) delay(1000);
  }

  i2c_master_Init();
  rtcSyncSystemFromChip();
  delay(50);
  audio_bsp_init();
  audio_play_init();
  initializeStorage();
  restoreHomeDeliveryRefresh();

  setupRequired = (!palaHasWifi() && !PALA_BRIDGE_DISCOVERY) || !(PALA_MAC_BRIDGE || palaHasOpenAiKey());
  if (setupRequired) {
    state = STATE_IDLE;
    showSetupRequired();
  } else {
    state = STATE_IDLE;
    renderReadyHome();
  }

  // Waking is deliberately a separate interaction: PWR only wakes to Home.
  // Ignore the held wake press until both buttons are released so it cannot
  // accidentally open Appointments or start recording during boot.
  if (wokeFromUltraSleep && state == STATE_IDLE) idleInputArmed = false;
  if (!setupRequired) beginRecordingSync(SyncTiming::OnWake);
  // The wake press itself only wakes to Home. New presses from this point on
  // are retained even if they happen during a display update.
  clearLatchedPresses();
}

void loop() {
  pollUsbBacklog();
  if (pollPairingOverlay()) {
    if (state == STATE_AQUARIUM) {
      aquarium = AquariumState{};
      state = STATE_IDLE;
      idleInputArmed = false;
    }
    delay(5);
    return;
  }
  if (usbSyncActive()) {
    stopBluetoothSync();
    if (backlogScreen.link != BacklogLink::Deferred &&
        (backlogScreen.link != BacklogLink::Usb || !backlogScreen.authenticated)) {
      showBacklogLink(BacklogLink::Usb, backlogScreen.completed, backlogScreen.total, true);
    }
    if (bothButtonsUp()) idleInputArmed = true;
    else if (idleInputArmed) cancelUsbSync();
    // Checking an empty cable leaves the normal idle-to-sleep deadline intact.
    if (usbSyncWorking() && !recordingSync.beforeSleep()) resetActivity();
    delay(5);
    return;
  }
  if (state == STATE_AQUARIUM) {
    handleAquarium();
    delay(8);
    return;
  }
  const bool bluetoothOpen = pollBluetoothSync();
  if (bluetoothOpen && bluetoothSyncActive()) {
    const bool organized = pollBluetoothOrganization();
    int completed = backlogScreen.completed, total = backlogScreen.total;
    const bool progress = takeBluetoothBacklogProgress(completed, total);
    if (backlogScreen.link == BacklogLink::Bluetooth && (organized || progress || !backlogScreen.authenticated)) {
      showBacklogLink(BacklogLink::Bluetooth, completed, total, true);
    }
    if (organized) {
      clearLatchedPresses();
      idleInputArmed = false;
    } else if (bothButtonsUp()) idleInputArmed = true;
    else if (idleInputArmed) stopBluetoothSync();
    if (!bluetoothHomeDeliveryOpen() && !recordingSync.beforeSleep()) resetActivity();
    delay(5);
    return;
  }
  if (bluetoothOpen && (upperButton.pressLatched || lowerButton.pressLatched)) stopBluetoothSync();
  if (!bluetoothOpen && !preferUsbForBacklog()) {
    if (backlogScreen.link == BacklogLink::Bluetooth) {
      backlogScreen.link = BacklogLink::Deferred;
      takeSyncHomeChanged();
      if (state == STATE_IDLE && !setupRequired) returnFromBackgroundSync();
    } else if (takeSyncHomeChanged() && state == STATE_IDLE && !setupRequired) {
      renderReadyHome();
    }
  }

  // Companion must also be serviced on a device that still needs setup.
  if (state == STATE_TRANSFER) {
    handleCompanion();
    delay(5);
    return;
  }

  // A freshly flashed device waits transparently for the local USB setup.
  // Do not start Wi-Fi Companion automatically: opening WebSerial resets the
  // ESP32 and the browser must still be able to deliver the setup command.
  if (setupRequired) {
    if (bothButtonsDown()) {
      if (!setupCompanionHoldStartedAt) setupCompanionHoldStartedAt = millis();
      else if (millis() - setupCompanionHoldStartedAt >= COMPANION_HOLD_MS) {
        setupCompanionHoldStartedAt = 0;
        enterCompanion();
      }
    } else {
      setupCompanionHoldStartedAt = 0;
    }
    delay(5);
    return;
  }

  if (state == STATE_AGENDA) {
    handleAgenda();
    delay(8);
    return;
  }

  if (state == STATE_IDLE) {
    handleIdle();
    // Let clicks settle before starting maintenance or a blocking network stage.
    if (state != STATE_IDLE || upperButton.gesture.pending() || lowerButton.gesture.pending() ||
        upperButton.pressLatched || lowerButton.pressLatched || !bothButtonsUp()) {
      delay(8);
      return;
    }
    const auto sleep = idleSleepPolicy(palaIdleSleepMinutes());
    if (recordingSync.running() && !recordingSyncAllowed()) {
      recordingSync.finish();
      backlogRequested = false;
      if (backlogSyncActive) {
        stopWifi();
        backlogSyncActive = false;
        backlogSyncTotal = 0;
        renderReadyHome();
      }
    }
    if (idleInputArmed && millis() - lastActivityMs >= sleep.homeMs) {
      beginRecordingSync(SyncTiming::BeforeSleep);
    }
    if (!bluetoothOpen) handleBackgroundBrain(sleep);
    // A panel refresh or Wi-Fi probe can service a newly authenticated USB session.
    if (usbSyncActive() || bluetoothSyncActive() || bluetoothOpen ||
        backlogScreen.link == BacklogLink::Bluetooth) {
      delay(8);
      return;
    }
    const uint32_t now = millis();
    const bool deliveryFollowupDue = homeDeliveryNeedsBluetoothFollowup(now);
    const bool deliveryWakeWindow = homeDeliveryKeepingBluetoothAwake(now);
    const bool followDeliveryOverBluetooth = (deliveryFollowupDue || deliveryWakeWindow) &&
                                              canFollowHomeDeliveryOverBluetooth();
    const bool keepAwakeForDelivery = deliveryWakeWindow && followDeliveryOverBluetooth;
    if (state == STATE_IDLE && !backlogRequested && !backlogSyncActive &&
        (now - lastActivityMs < sleep.homeMs || keepAwakeForDelivery) &&
        deliveryFollowupDue && followDeliveryOverBluetooth) {
      homeDeliveryBluetoothAttempted(now);
      if (startBluetoothHomeDelivery()) return;
    }
    if (state == STATE_IDLE) refreshHomeMetadataWhenIdle(sleep.homeMs);
    if (state == STATE_IDLE && !backlogRequested && !backlogSyncActive &&
        bothButtonsUp() && !upperButton.pressLatched && !lowerButton.pressLatched && !preferUsbForBacklog()) {
      maintainNoteTrash();
    }
    if (state == STATE_IDLE &&
        static_cast<int32_t>(millis() - nextHomeClockCheckAt) >= 0) {
      nextHomeClockCheckAt = millis() + 60000UL;
      const String day = localDayKey();
      const auto usage = homeDeliveryCounts().codexAt(time(nullptr), palaPrefersWeeklyCodexLimit());
      if (day != displayedHomeDay || usage != displayedHomeUsage) renderReadyHome();
    }
    // A verified link may have closed without changing any other Home data.
    if (state == STATE_IDLE && backlogScreen.link == BacklogLink::Deferred &&
        !backlogSyncActive && !preferUsbForBacklog() && bothButtonsUp() &&
        !upperButton.pressLatched && !lowerButton.pressLatched && homeBridgeStatus.needsRedraw()) {
      renderReadyHome();
    }
    if (state == STATE_IDLE &&
        idleInputArmed && bothButtonsUp() && !upperButton.gesture.pending() &&
        !lowerButton.gesture.pending() && !upperButton.pressLatched && !lowerButton.pressLatched &&
        !usbSyncActive() && !bluetoothSyncActive()) {
      if (millis() - lastActivityMs >= sleep.homeMs && !recordingSyncAllowed() && !keepAwakeForDelivery)
        enterUltraSleep();
    }
  }
  delay(8);
}
