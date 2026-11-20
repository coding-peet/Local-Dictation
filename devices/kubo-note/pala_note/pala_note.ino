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
#include "src/app/brain.h"
#include "src/app/provider.h"
#include "src/app/battery.h"
#include "src/app/network.h"
#include "src/app/notes.h"
#include "src/app/record.h"
#include "src/app/rtc.h"
#include "src/app/settings.h"
#include "src/app/sleep.h"
#include "src/app/tasks.h"
#include "src/app/ui.h"

board_power_bsp_t board(EPD_PWR_PIN, Audio_PWR_PIN, VBAT_PWR_PIN);
epaper_driver_display* display = nullptr;
std::vector<NoteEntry> noteIndex;

AppState state = STATE_IDLE;
int lastRecNum = -1;
uint32_t lastActivityMs = 0;

WebServer transferServer(80);
bool transferServerActive = false;
String transferUrl;

bool timeReady = false;
bool audioPlaying = false;
bool stopPlayback = false;

void resetActivity();

enum class PressResult { Released, LongSingle, Chord };

namespace {

bool companionExitArmed = false;
bool idleInputArmed = true;
uint32_t companionGestureStartedAt = 0;
uint32_t nextBackgroundAttemptAt = 0;
bool backlogSyncActive = false;
int backlogSyncTotal = 0;
uint32_t nextHomeClockCheckAt = 0;
uint32_t lastUpperTapAt = 0;
uint32_t lastLowerTapAt = 0;
volatile bool upperPressLatched = false;
volatile bool lowerPressLatched = false;
bool companionUpperWasDown = false;
bool companionLowerWasDown = false;
uint32_t companionVolumeShownAt = 0;
uint32_t companionSingleStartedAt = 0;
int companionSinglePin = -1;
String companionApPassword;
String displayedHomeDay;
bool agendaShowsEvents = false;
uint32_t agendaShownAt = 0;
int agendaPage = 0;
bool setupRequired = false;
uint32_t setupCompanionHoldStartedAt = 0;
int processPendingMemories(int preferredNote, int maxNotes);

String usbSetupLine;

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
      usbSetupLine.trim();
      applyUsbSetupLine(usbSetupLine);
      usbSetupLine = "";
    } else if (c != '\r') {
      if (usbSetupLine.length() < 720) usbSetupLine += c;
      else usbSetupLine = "";
    }
  }
}

bool bothButtonsDown() {
  return digitalRead(BTN_REC) == LOW && digitalRead(BTN_PWR) == LOW;
}

bool bothButtonsUp() {
  return digitalRead(BTN_REC) == HIGH && digitalRead(BTN_PWR) == HIGH;
}

void latchUpperPress() { upperPressLatched = true; }
void latchLowerPress() { lowerPressLatched = true; }

void clearLatchedPresses() {
  noInterrupts();
  upperPressLatched = false;
  lowerPressLatched = false;
  interrupts();
}

bool takeLatchedPress(bool upper) {
  noInterrupts();
  const bool latched = upper ? upperPressLatched : lowerPressLatched;
  if (upper) upperPressLatched = false;
  else lowerPressLatched = false;
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

void renderReadyHome() {
  const BatteryReading battery = readBattery();
  // The home screen only exposes recordings that are not transcribed yet.
  // Rebuilding structure/vector indexes is silent maintenance: the transcript
  // is already searchable and must not leave a permanent "waiting" badge.
  int untranscribed = 0;
  for (const NoteEntry& note : noteIndex) {
    if (!note.hasText && noteQueueState(note.num) != "blocked") untranscribed++;
  }
  showIdle(untranscribed, battery.valid ? battery.percent : -1);
  displayedHomeDay = localDayKey();
  nextHomeClockCheckAt = millis() + 60000UL;
}

void returnFromBackgroundSync() {
  renderReadyHome();
  // Presses during network/display work must not become agenda shortcuts.
  clearLatchedPresses();
  idleInputArmed = false;
  lastUpperTapAt = 0;
  lastLowerTapAt = 0;
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

bool connectConfiguredWifi(uint8_t attempts = 20) {
  if (!palaHasWifi()) return false;
  // Background processing may already have a healthy association. Calling
  // WiFi.begin again tears it down and adds an avoidable 1-3 second reconnect.
  if (WiFi.status() == WL_CONNECTED) {
    if (time(nullptr) < 1700000000) syncTimeFromNTP(4000);
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
    for (uint8_t attempt = 0; attempt < quickAttempts && WiFi.status() != WL_CONNECTED; attempt++) delay(200);
    if (WiFi.status() == WL_CONNECTED) {
      palaSetLastWifiSsid(quick.ssid);
      if (time(nullptr) < 1700000000) syncTimeFromNTP(4000);
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
  const int visible = WiFi.scanNetworks(false, true);
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
  for (int pass = 0; pass < palaWifiProfileCount(); pass++) {
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
         attempt < associationAttempts && WiFi.status() != WL_CONNECTED;
         ++attempt) delay(300);
    if (WiFi.status() == WL_CONNECTED) {
      palaSetLastWifiSsid(selected.ssid);
      if (time(nullptr) < 1700000000) syncTimeFromNTP(4000);
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

void enterCompanion() {
  resetActivity();
  state = STATE_TRANSFER;
  companionExitArmed = false;
  companionGestureStartedAt = 0;
  companionUpperWasDown = digitalRead(BTN_REC) == LOW;
  companionLowerWasDown = digitalRead(BTN_PWR) == LOW;
  companionVolumeShownAt = 0;
  companionSingleStartedAt = 0;
  companionSinglePin = -1;
  clearLatchedPresses();
  showTransferConnecting();

  bool joinedSavedNetwork = connectConfiguredWifi(12);
  String accessPointPassword;
  if (!joinedSavedNetwork) {
    stopWifi();
    WiFi.mode(WIFI_AP);
    char password[16];
    // Generate a fresh setup password instead of deriving a predictable one
    // from the public device identity.
    snprintf(password, sizeof(password), "Pala-%08lX", static_cast<unsigned long>(esp_random()));
    accessPointPassword = password;
    if (!WiFi.softAP("Pala Note 2.0", accessPointPassword.c_str())) {
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
  const IPAddress ip = joinedSavedNetwork ? WiFi.localIP() : WiFi.softAPIP();
  transferUrl = ip.toString();
  companionApPassword = joinedSavedNetwork ? "" : accessPointPassword;
  showTransferMode(transferUrl.c_str(),
                   joinedSavedNetwork ? nullptr : accessPointPassword.c_str());
}

void leaveCompanion() {
  setupRequired = !palaHasWifi() || !(PALA_MAC_BRIDGE || palaHasOpenAiKey());
  soundBack();
  stopTransferMode();
  companionExitArmed = false;
  companionGestureStartedAt = 0;
  companionSingleStartedAt = 0;
  companionSinglePin = -1;
  state = STATE_IDLE;
  idleInputArmed = false;
  clearLatchedPresses();
  resetActivity();
  renderReadyHome();
  clearLatchedPresses();
}

bool processCapturedMemoryNow(int noteNum) {
  showMemoryProcessing(noteNum, "CONNECTING");
  if (!palaHasWifi() || !(PALA_MAC_BRIDGE || palaHasOpenAiKey()) || !connectConfiguredWifi(14)) {
    stopWifi();
    writeNoteQueueState(noteNum, "queued", "saved locally; waiting for connection");
    showMemoryProcessing(noteNum, "QUEUED");
    delay(900);
    return false;
  }

  loadIndex();
  bool ready = false;
  // Exactly one durable network stage per iteration. The screen therefore
  // always describes the operation that can currently take time.
  // Finish the two user-visible essentials now: transcription and structured
  // understanding. The optional semantic embedding is derived maintenance and
  // must never keep a freshly captured thought on a processing screen.
  for (int stage = 0; stage < 2; stage++) {
    auto entry = std::find_if(noteIndex.begin(), noteIndex.end(), [noteNum](const NoteEntry& note) {
      return note.num == noteNum;
    });
    if (entry == noteIndex.end()) break;
    if (!entry->hasText) showMemoryProcessing(noteNum, "DECODING");
    else if (!brainMemoryIsCurrent(noteNum)) showMemoryProcessing(noteNum, "UNDERSTANDING");
    else if (!brainEmbeddingExists(noteNum)) showMemoryProcessing(noteNum, "REMEMBERING");
    else { ready = true; break; }

    if (processPendingNotes(noteNum, 1) <= 0) break;
    loadIndex();
  }
  ready = ready || brainMemoryIsCurrent(noteNum);
  stopWifi();
  if (noteQueueError(noteNum) == "no speech recognized") {
    showError(palaUiLanguage() == "de" ? "KEINE SPRACHE" : "NO SPEECH");
    delay(900);
    return false;
  }
  if (ready) {
    writeNoteQueueState(noteNum, "ready");
    showSaved(noteNum);
    delay(650);
  } else {
    if (noteQueueState(noteNum) == "blocked") showError("PROCESSING FAILED");
    else showMemoryProcessing(noteNum, "QUEUED");
    delay(900);
  }
  return ready;
}

void redrawCompanionHome() {
  showTransferMode(transferUrl.c_str(),
                   companionApPassword.length() ? companionApPassword.c_str() : nullptr);
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
  const bool recorded = record();
  palaSoundSetEnabled(true);

  if (!recorded) {
    showError("RECORD AGAIN");
    delay(1300);
    returnToReady();
    return;
  }

  if (!saveTag(lastRecNum, "Inbox")) {
    // The WAV is retained for orphan recovery; never claim a failed index
    // commit succeeded or launch cloud work against an uncommitted capture.
    showError("STORAGE FULL"); delay(1300); returnToReady(); return;
  }
  soundSaved();
  showSaved(lastRecNum);
  delay(350);

  // The WAV is durable before any network work starts. Process this specific
  // memory visibly now; if the network fails it remains safely queued.
  processCapturedMemoryNow(lastRecNum);
  // Do not make the freshly returned home screen mysteriously unresponsive by
  // immediately starting unrelated backlog work.
  nextBackgroundAttemptAt = millis() + BACKGROUND_RETRY_MS;
  returnToReady();
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
  lastUpperTapAt = 0;
  lastLowerTapAt = 0;
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
  lastUpperTapAt = 0;
  lastLowerTapAt = 0;
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
      lastUpperTapAt = 0;
      lastLowerTapAt = 0;
      return PressResult::Chord;
    }
    delay(4);
  }
  return digitalRead(pin) == LOW ? PressResult::LongSingle : PressResult::Released;
}

bool waitForSecondTap(int pin) {
  const uint32_t deadline = millis() + ASK_DOUBLE_TAP_MS;
  while (static_cast<int32_t>(deadline - millis()) > 0) {
    if (bothButtonsDown()) return false;
    if (digitalRead(pin) == LOW) {
      delay(14);
      if (digitalRead(pin) != LOW) continue;
      // Consume the second short press, but never turn a held second press into
      // another action after the briefing returns.
      const uint32_t releaseDeadline = millis() + REC_HOLD_MS;
      while (digitalRead(pin) == LOW &&
             static_cast<int32_t>(releaseDeadline - millis()) > 0) delay(4);
      return digitalRead(pin) == HIGH;
    }
    delay(4);
  }
  return false;
}

void handleIdle() {
  if (!idleInputArmed) {
    // A fresh edge captured during the blocking e-paper refresh is safe to
    // consume. The old Back/wake edge was cleared before that refresh.
    if (upperPressLatched || lowerPressLatched) idleInputArmed = true;
    else {
      if (bothButtonsUp()) idleInputArmed = true;
      return;
    }
  }

  if (bothButtonsDown()) {
    clearLatchedPresses();
    lastUpperTapAt = 0;
    lastLowerTapAt = 0;
    if (companionGestureStartedAt == 0) companionGestureStartedAt = millis();
    if (millis() - companionGestureStartedAt >= COMPANION_HOLD_MS) {
      soundSelect();
      enterCompanion();
    }
    return;
  }
  companionGestureStartedAt = 0;

  const bool upperLatched = upperPressLatched;
  const bool lowerLatched = lowerPressLatched;
  if (digitalRead(BTN_REC) == LOW || (upperLatched && !lowerLatched)) {
    takeLatchedPress(true);
    resetActivity();
    // A released edge captured during refresh is necessarily a short press.
    if (digitalRead(BTN_REC) == HIGH) {
      lastLowerTapAt = 0;
      state = STATE_AGENDA;
      agendaShowsEvents = false;
      agendaPage = 0;
      agendaShownAt = millis();
      showTodayAgenda(false, agendaPage);
      lastUpperTapAt = 0;
      return;
    }
    const PressResult press = classifyPress(BTN_REC);
    if (press == PressResult::Chord) return;
    if (press == PressResult::LongSingle) {
      lastUpperTapAt = 0;
      captureMemory();
    }
    else {
      if (waitForSecondTap(BTN_REC)) {
        playDailyBriefing();
        return;
      }
      lastLowerTapAt = 0;
      state = STATE_AGENDA;
      agendaShowsEvents = false;
      agendaPage = 0;
      agendaShownAt = millis();
      showTodayAgenda(false, agendaPage);
      lastUpperTapAt = 0;
    }
    return;
  }
  if (digitalRead(BTN_PWR) == LOW || lowerLatched) {
    takeLatchedPress(false);
    resetActivity();
    if (digitalRead(BTN_PWR) == HIGH) {
      lastUpperTapAt = 0;
      state = STATE_AGENDA;
      agendaShowsEvents = true;
      agendaPage = 0;
      agendaShownAt = millis();
      showTodayAgenda(true, agendaPage);
      lastLowerTapAt = 0;
      return;
    }
    const PressResult press = classifyPress(BTN_PWR);
    if (press == PressResult::Chord) return;
    if (press == PressResult::LongSingle) {
      lastLowerTapAt = 0;
      askBrain();
    } else {
      if (waitForSecondTap(BTN_PWR)) {
        playWeeklyBriefing();
        return;
      }
      lastUpperTapAt = 0;
      state = STATE_AGENDA;
      agendaShowsEvents = true;
      agendaPage = 0;
      agendaShownAt = millis();
      showTodayAgenda(true, agendaPage);
      lastLowerTapAt = 0;
    }
  }
}

void handleAgenda() {
  if (millis() - agendaShownAt >= 30000UL) {
    returnToReady();
    return;
  }
  if (bothButtonsDown()) {
    if (companionGestureStartedAt == 0) companionGestureStartedAt = millis();
    if (millis() - companionGestureStartedAt >= COMPANION_HOLD_MS) enterCompanion();
    return;
  }
  companionGestureStartedAt = 0;

  const bool upperLatched = upperPressLatched;
  const bool lowerLatched = lowerPressLatched;
  if (digitalRead(BTN_REC) == LOW || (upperLatched && !lowerLatched)) {
    takeLatchedPress(true);
    const PressResult press = digitalRead(BTN_REC) == LOW
      ? classifyPress(BTN_REC) : PressResult::Released;
    if (press == PressResult::Chord) return;
    resetActivity();
    if (!agendaShowsEvents && agendaPage + 1 < todayAgendaPageCount(false)) {
      agendaPage++;
      agendaShownAt = millis();
      showTodayAgenda(false, agendaPage);
    } else returnToReady();
    return;
  }
  if (digitalRead(BTN_PWR) == LOW || lowerLatched) {
    takeLatchedPress(false);
    const PressResult press = digitalRead(BTN_PWR) == LOW
      ? classifyPress(BTN_PWR) : PressResult::Released;
    if (press == PressResult::Chord) return;
    resetActivity();
    if (agendaShowsEvents && agendaPage + 1 < todayAgendaPageCount(true)) {
      agendaPage++;
      agendaShownAt = millis();
      showTodayAgenda(true, agendaPage);
    } else returnToReady();
  }
}

void handleBackgroundBrain() {
  // Do not launch another network stage after the sleep budget has expired.
  // A stage already running is allowed to finish and save its result safely.
  if (millis() - lastActivityMs >= AUTO_SLEEP_GRACE_MS) return;
  if (!palaHasWifi() || !(PALA_MAC_BRIDGE || palaHasOpenAiKey())) return;
  if (nextBackgroundAttemptAt && static_cast<int32_t>(millis() - nextBackgroundAttemptAt) < 0) return;
  if (millis() - lastActivityMs < BACKGROUND_IDLE_MS) return;
  if (!bothButtonsUp()) return;
  // This scan touches per-note metadata, so never perform it in every 8 ms
  // idle-loop iteration. At scale it must only run when a pass is actually due.
  int pendingBefore = pendingBrainMemoryCount();
  int indexingNote = -1;
  // Optional search maintenance must never extend the normal sleep window.
  if (pendingBefore == 0 && millis() - lastActivityMs < AUTO_SLEEP_IDLE_MS) {
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
    stopWifi();
    if (backlogSyncActive) {
      showBacklogProcessing(backlogSyncTotal, backlogSyncTotal, "DONE");
      delay(900);
      backlogSyncActive = false;
      backlogSyncTotal = 0;
      returnFromBackgroundSync();
    }
    nextBackgroundAttemptAt = millis() + BACKGROUND_RETRY_MS;
    return;
  }

  // Never perform an invisible network operation behind an apparently idle
  // home screen. Pick the exact durable item this pass will work on and show
  // its real stage before a TLS request can temporarily occupy the device.
  int visibleNote = indexingNote;
  const char* visiblePhase = indexingNote >= 0 ? "REMEMBERING" : "CONNECTING";
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
  if (visibleNote < 0) {
    stopWifi();
    if (backlogSyncActive) {
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
  const int completedBefore = max(0, backlogSyncTotal - pendingBefore);
  showBacklogProcessing(completedBefore, backlogSyncTotal, "CONNECTING");

  // Separate network association from the cloud stage so the screen always
  // describes what the device is actually doing. A failed association keeps
  // the durable recording queued and returns to Home without appearing stuck.
  if (!connectConfiguredWifi(8)) {
    showBacklogProcessing(completedBefore, backlogSyncTotal, "QUEUED");
    delay(900);
    backlogSyncActive = false;
    backlogSyncTotal = 0;
    nextBackgroundAttemptAt = millis() + BACKGROUND_RETRY_MS;
    returnFromBackgroundSync();
    return;
  }
  showBacklogProcessing(completedBefore, backlogSyncTotal, visiblePhase);

  // Wi-Fi and Arduino's TCP stack must stay on the normal loop context.
  // Running this on a separate core starved IDLE0 and repeatedly crashed the
  // socket layer. Process one durable recording per bounded idle pass instead.
  palaSetBackgroundNetworkMode(true);
  const int processed = processPendingMemories(visibleNote, 1);
  palaSetBackgroundNetworkMode(false);
  // Leaving the runnable queue is not proof of success: permanent errors are
  // blocked too. Do not count such a transition as a completed memory.
  if (indexingNote < 0 && noteQueueState(visibleNote) == "blocked" &&
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
  const int pendingAfter = indexingNote >= 0 ? (processed > 0 ? 0 : 1) : pendingBrainMemoryCount();
  if (pendingAfter == 0) {
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

void handleCompanion() {
  if (transferServerActive) transferServer.handleClient();

  const bool upperDown = digitalRead(BTN_REC) == LOW;
  const bool lowerDown = digitalRead(BTN_PWR) == LOW;

  if (upperDown || lowerDown) resetActivity();
  if (millis() - lastActivityMs >= COMPANION_IDLE_SLEEP_MS) {
    leaveCompanion();
    enterUltraSleep();
    return;
  }

  if (!companionExitArmed) {
    companionUpperWasDown = upperDown;
    companionLowerWasDown = lowerDown;
    if (!upperDown && !lowerDown) companionExitArmed = true;
    return;
  }

  if (upperDown && lowerDown) {
    companionSingleStartedAt = 0;
    companionSinglePin = -1;
    if (companionGestureStartedAt == 0) companionGestureStartedAt = millis();
    if (millis() - companionGestureStartedAt >= COMPANION_HOLD_MS) {
      leaveCompanion();
      return;
    }
    companionUpperWasDown = true;
    companionLowerWasDown = true;
    return;
  }

  companionGestureStartedAt = 0;
  if (upperDown && !companionUpperWasDown) {
    companionSingleStartedAt = millis();
    companionSinglePin = BTN_REC;
  } else if (lowerDown && !companionLowerWasDown) {
    companionSingleStartedAt = millis();
    companionSinglePin = BTN_PWR;
  }

  if (companionSinglePin >= 0 && millis() - companionSingleStartedAt >= 140UL) {
    if (!bothButtonsDown()) {
      const int volume = companionSinglePin == BTN_REC
        ? min(100, palaPlaybackVolume() + 20)
        : max(20, palaPlaybackVolume() - 20);
      palaSetPlaybackVolume(volume);
      showVolumeSetting(volume);
      companionVolumeShownAt = millis();
    }
    companionSingleStartedAt = 0;
    companionSinglePin = -1;
  } else if (companionVolumeShownAt && millis() - companionVolumeShownAt >= 1800UL) {
    companionVolumeShownAt = 0;
    redrawCompanionHome();
  }

  companionUpperWasDown = upperDown;
  companionLowerWasDown = lowerDown;
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
  Serial.printf("[SD] %d memories\n", static_cast<int>(noteIndex.size()));
}

}  // namespace

void setup() {
  Serial.begin(115200);
  delay(40);
  Serial.println("\n=== Pala Note " FIRMWARE_VERSION " ===");

  releasePeripheralPowerHolds();
  palaSettingsBegin();
  importFlashSetup();
  batteryBegin();
  pinMode(BTN_REC, INPUT_PULLUP);
  pinMode(BTN_PWR, INPUT_PULLUP);
  attachInterrupt(digitalPinToInterrupt(BTN_REC), latchUpperPress, FALLING);
  attachInterrupt(digitalPinToInterrupt(BTN_PWR), latchLowerPress, FALLING);
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

  setupRequired = !palaHasWifi() || !(PALA_MAC_BRIDGE || palaHasOpenAiKey());
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
  // The wake press itself only wakes to Home. New presses from this point on
  // are retained even if they happen during a display update.
  clearLatchedPresses();
}

void loop() {
  handleUsbSetup();

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
    if (state == STATE_IDLE) handleBackgroundBrain();
    if (state == STATE_IDLE &&
        static_cast<int32_t>(millis() - nextHomeClockCheckAt) >= 0) {
      nextHomeClockCheckAt = millis() + 60000UL;
      const String day = localDayKey();
      if (day.length() && day != displayedHomeDay) renderReadyHome();
    }
    if (state == STATE_IDLE &&
        idleInputArmed && bothButtonsUp() &&
        (!lastUpperTapAt || millis() - lastUpperTapAt > ASK_DOUBLE_TAP_MS) &&
        (!lastLowerTapAt || millis() - lastLowerTapAt > ASK_DOUBLE_TAP_MS)) {
      const uint32_t idleFor = millis() - lastActivityMs;
      if (idleFor >= AUTO_SLEEP_IDLE_MS) {
        // Give queued memories time to finish, but never let a bad network or
        // failed cloud request keep the device awake indefinitely.
        // This scans SD metadata. Never repeat it on every 8 ms idle tick.
        static uint32_t nextQueueCheck = 0;
        static bool workPending = false;
        if (!nextQueueCheck || static_cast<int32_t>(millis() - nextQueueCheck) >= 0) {
          workPending = palaHasWifi() && (PALA_MAC_BRIDGE || palaHasOpenAiKey()) && pendingBrainMemoryCount() > 0;
          nextQueueCheck = millis() + 1000UL;
        }
        if (!workPending || idleFor >= AUTO_SLEEP_GRACE_MS) enterUltraSleep();
      }
    }
  }
  delay(8);
}
