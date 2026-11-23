#include "provider.h"
#include "settings.h"
#include <Preferences.h>
#include "bridge_address.h"
#include "display_pages.h"
#include "notes.h"
#include "settings_pair.h"
#include "sleep_policy.h"

static Preferences prefs;
static bool ready = false;
static int cachedWifiCount = -1;
static int cachedApiConfigured = -1;
static int cachedIdleSleepMinutes = -1;
static int cachedSyncTiming = -1;
static int cachedPreferBluetooth = -1;
static constexpr uint8_t kWifiRadio = 1;
static constexpr uint8_t kBluetoothRadio = 2;
static constexpr uint8_t kBothRadios = kWifiRadio | kBluetoothRadio;
static uint8_t enabledRadios = kBothRadios;

static bool supportedLanguage(const String& language) {
  return language == "de" || language == "en" || language == "fr" ||
         language == "es" || language == "it" || language == "pl" ||
         language == "ru" || language == "sk" || language == "cs" || language == "uk" ||
         language == "nl" || language == "pt";
}

static String wifiKey(int index, const char* suffix) {
  return "wifi" + String(index) + suffix;
}

static String storedString(const String& key) {
  if (!ready || !prefs.isKey(key.c_str())) return "";
  return prefs.getString(key.c_str(), "");
}

static bool storeWifiPair(int index, const String& ssid, const String& password) {
  return saveSettingsPair(ssid, password,
    [&](bool secret) { return storedString(wifiKey(index, secret ? "p" : "s")); },
    [&](bool secret, const String& text) { prefs.putString(wifiKey(index, secret ? "p" : "s").c_str(), text); });
}

static bool storeLegacyWifi(const PalaWifiProfile& profile) {
  return saveSettingsPair(profile.ssid, profile.password,
    [&](bool secret) { return storedString(secret ? "wifi_pass" : "wifi_ssid"); },
    [&](bool secret, const String& text) { prefs.putString(secret ? "wifi_pass" : "wifi_ssid", text); });
}

void palaSettingsBegin() {
  if (ready) return;
  ready = prefs.begin("pala", false);
  if (!ready) return;
  enabledRadios = prefs.getUChar("radios", kBothRadios);
  // Migrate the original single-network setting without losing it.
  if (!storedString("wifi0s").length()) {
    const String legacySsid = storedString("wifi_ssid");
    if (legacySsid.length()) {
      storeWifiPair(0, legacySsid, storedString("wifi_pass"));
    }
  }
}

static String value(const char* key) {
  palaSettingsBegin();
  return ready ? storedString(key) : String();
}

bool palaWifiEnabled() { return (enabledRadios & kWifiRadio) != 0; }
bool palaBluetoothEnabled() { return (enabledRadios & kBluetoothRadio) != 0; }

bool palaSetWirelessRadios(bool wifi, bool bluetooth) {
  palaSettingsBegin();
  if (!ready) return false;
  const uint8_t selected = (wifi ? kWifiRadio : 0) | (bluetooth ? kBluetoothRadio : 0);
  if (selected == enabledRadios) return true;
  if (prefs.putUChar("radios", selected) != sizeof(selected)) return false;
  enabledRadios = selected;
  return true;
}

static bool validNoteName(const String& name) {
  if (!name.length() || name.length() > kPalaNoteNameMax || name[name.length() - 1] == ' ') return false;
  for (size_t i = 0; i < name.length(); ++i) {
    const char c = name[i];
    if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) continue;
    if (!i || (c != ' ' && c != '-')) return false;
  }
  return true;
}

bool palaNoteNameLocallyManaged() { return value("note_device").indexOf('|') > 0; }

bool palaPrefersWeeklyCodexLimit() { return value("note_device").endsWith("|weekly"); }

bool palaSaveLocalNoteSettings(const String& name, const String& language, bool weeklyCodexLimit) {
  palaSettingsBegin();
  if (!ready || !validNoteName(name) || !supportedLanguage(language)) return false;
  const String settings = name + "|" + language + (weeklyCodexLimit ? "|weekly" : "");
  return storedString("note_device") == settings ||
      (prefs.putString("note_device", settings) == settings.length() && storedString("note_device") == settings);
}

String palaNoteName() {
  const String local = value("note_device");
  const int separator = local.indexOf('|');
  const String name = separator > 0 ? local.substring(0, separator) : value("note_name");
  return validNoteName(name) ? name : String("PALA");
}

bool palaSetNoteName(const String& name) {
  if (!validNoteName(name)) return false;
  if (value("note_name") == name) return true;
  if (!ready || prefs.putString("note_name", name) == 0) return false;
  return storedString("note_name") == name;
}

String palaStoredDeliveryCounts() {
  return value("home_delivery");
}

bool palaStoreDeliveryCounts(const String& counts) {
  if (palaStoredDeliveryCounts() == counts) return true;
  if (!ready || prefs.putString("home_delivery", counts) == 0) return false;
  return storedString("home_delivery") == counts;
}

String palaSavedHolidays() { return value("home_holidays"); }

bool palaStoreHolidays(const String& snapshot) {
  palaSettingsBegin();
  return ready && prefs.putString("home_holidays", snapshot) == snapshot.length() &&
      storedString("home_holidays") == snapshot;
}

int palaWifiProfileCount() {
  palaSettingsBegin();
  if (!ready) return 0;
  if (cachedWifiCount >= 0) return cachedWifiCount;
  int count = 0;
  for (int i = 0; i < PALA_MAX_WIFI_PROFILES; i++) {
    if (storedString(wifiKey(i, "s")).length()) count++;
  }
  cachedWifiCount = count;
  return cachedWifiCount;
}

PalaWifiProfile palaWifiProfile(int index) {
  palaSettingsBegin();
  if (!ready || index < 0 || index >= PALA_MAX_WIFI_PROFILES) return {};
  return {storedString(wifiKey(index, "s")), storedString(wifiKey(index, "p"))};
}

bool palaSaveWifiProfile(const String& ssid, const String& password) {
  palaSettingsBegin();
  if (!ready || !ssid.length() || ssid.length() > 32 || password.length() > 63) return false;
  int target = -1;
  for (int i = 0; i < PALA_MAX_WIFI_PROFILES; i++) {
    const String stored = storedString(wifiKey(i, "s"));
    if (stored == ssid) { target = i; break; }
    if (target < 0 && !stored.length()) target = i;
  }
  if (target < 0) return false;
  const bool ok = storeWifiPair(target, ssid, password);
  cachedWifiCount = -1;
  // Keep legacy keys synchronized for downgrade compatibility.
  if (ok && target == 0) {
    if (!storeLegacyWifi({ssid, password})) Serial.println("[Settings] legacy Wi-Fi mirror write failed");
  }
  return ok;
}

bool palaRemoveWifiProfile(const String& ssid) {
  palaSettingsBegin();
  if (!ready || !ssid.length()) return false;
  int found = -1;
  for (int i = 0; i < PALA_MAX_WIFI_PROFILES; i++) {
    if (storedString(wifiKey(i, "s")) == ssid) { found = i; break; }
  }
  if (found < 0) return false;
  PalaWifiProfile before[PALA_MAX_WIFI_PROFILES];
  for (int i = 0; i < PALA_MAX_WIFI_PROFILES; ++i) before[i] = palaWifiProfile(i);
  bool ok = true;
  for (int i = found; i < PALA_MAX_WIFI_PROFILES && ok; ++i) {
    const PalaWifiProfile next = i + 1 < PALA_MAX_WIFI_PROFILES ? before[i + 1] : PalaWifiProfile{};
    ok = storeWifiPair(i, next.ssid, next.password);
  }
  if (ok) ok = storeLegacyWifi(palaWifiProfile(0));
  if (!ok) {
    for (int i = 0; i < PALA_MAX_WIFI_PROFILES; ++i) storeWifiPair(i, before[i].ssid, before[i].password);
    storeLegacyWifi(before[0]);
    cachedWifiCount = -1;
    Serial.println("[Settings] Wi-Fi removal failed; rollback attempted");
    return false;
  }
  cachedWifiCount = -1;
  return true;
}

String palaWifiSsid() { return palaWifiProfile(0).ssid; }
String palaWifiPassword() { return palaWifiProfile(0).password; }
String palaLastWifiSsid() { return value("last_wifi"); }
bool palaSetLastWifiSsid(const String& ssid) {
  palaSettingsBegin();
  if (!ready || !ssid.length() || ssid.length() > 32) return false;
  return prefs.putString("last_wifi", ssid) > 0;
}
String palaOpenAiKey() { return value("openai_key"); }
String palaBridgeAddress() { return value("bridge_ip"); }
bool palaSetBridgeAddress(const String& address) {
  palaSettingsBegin();
  if (!ready) return false;
  uint8_t octets[4];
  if (address.length() && !parseBridgeAddress(address.c_str(), octets)) return false;
  if (storedString("bridge_ip") == address) return true;
  if (!address.length()) return prefs.remove("bridge_ip");
  return prefs.putString("bridge_ip", address) == address.length() &&
         storedString("bridge_ip") == address;
}

SyncTiming palaSyncTiming() {
  if (cachedSyncTiming < 0) {
    palaSettingsBegin();
    if (!ready) return SyncTiming::BeforeSleep;
    const int mode = prefs.getUChar("sync_timing", 0);
    cachedSyncTiming = validSyncTiming(mode) ? mode : 0;
  }
  return static_cast<SyncTiming>(cachedSyncTiming);
}

bool palaSetSyncTiming(SyncTiming mode) {
  const auto stored = static_cast<uint8_t>(mode);
  if (!validSyncTiming(stored)) return false;
  palaSettingsBegin();
  if (!ready) return false;
  if (palaSyncTiming() == mode) return true;
  if (prefs.putUChar("sync_timing", stored) != sizeof(stored)) return false;
  cachedSyncTiming = -1;
  return palaSyncTiming() == mode;
}

int palaIdleSleepMinutes() {
  if (cachedIdleSleepMinutes < 0) {
    palaSettingsBegin();
    if (!ready) return 0;
    const int minutes = prefs.getUChar("sleep_minutes", 0);
    cachedIdleSleepMinutes = validIdleSleepMinutes(minutes) ? minutes : 0;
  }
  return cachedIdleSleepMinutes;
}

bool palaSetIdleSleepMinutes(int minutes) {
  if (!validIdleSleepMinutes(minutes)) return false;
  palaSettingsBegin();
  if (!ready) return false;
  if (palaIdleSleepMinutes() == minutes) return true;
  const auto stored = static_cast<uint8_t>(minutes);
  if (prefs.putUChar("sleep_minutes", stored) != sizeof(stored) ||
      prefs.getUChar("sleep_minutes", 0xff) != stored) {
    cachedIdleSleepMinutes = -1;
    return false;
  }
  cachedIdleSleepMinutes = minutes;
  return true;
}

String palaDisplayPages() {
#if PALA_BRIDGE_DISCOVERY
  palaSettingsBegin();
  const String combined = storedString("display_v2");
  const String order = combined.length() == kDisplayPageCount + 1
      ? combined.substring(0, kDisplayPageCount) : storedString("display_pages");
  if (!validDisplayPages(order.c_str(), order.length())) return String(kDefaultDisplayPages);
  return order.length() == kLegacyDisplayPageCount ? order + 'm' : order;
#else
  // The standalone provider has no shared settings portal.
  return "Tecnm";
#endif
}

bool palaDisplayPagesWrap() {
  palaSettingsBegin();
  const String combined = storedString("display_v2");
  return combined.length() != kDisplayPageCount + 1 || combined[kDisplayPageCount] != '0';
}

bool palaSetDisplayPages(const String& order, bool wrap) {
  palaSettingsBegin();
  if (!ready || order.length() != kDisplayPageCount ||
      !validDisplayPages(order.c_str(), order.length())) return false;
  const String combined = order + (wrap ? '1' : '0');
  return storedString("display_v2") == combined ||
         (prefs.putString("display_v2", combined) == combined.length() &&
          storedString("display_v2") == combined);
}

bool palaPreferBluetooth() {
  if (cachedPreferBluetooth < 0) {
    palaSettingsBegin();
    if (!ready) return false;
    cachedPreferBluetooth = prefs.getBool("prefer_ble", false);
  }
  return cachedPreferBluetooth != 0;
}

bool palaSetPreferBluetooth(bool preferred) {
  palaSettingsBegin();
  if (!ready) return false;
  if (palaPreferBluetooth() == preferred) return true;
  if (prefs.putBool("prefer_ble", preferred) != 1) return false;
  cachedPreferBluetooth = -1;
  return palaPreferBluetooth() == preferred;
}

String palaSpeechVoice() {
  String voice = value("tts_voice");
  return voice == "coral" ? "coral" : "cedar";
}
String palaUiLanguage() {
  const String local = value("note_device");
  const int separator = local.indexOf('|');
  const int suffix = local.indexOf('|', separator + 1);
  const String language = separator > 0
      ? local.substring(separator + 1, suffix > separator ? suffix : local.length())
      : value("ui_lang");
  return supportedLanguage(language) ? language : (PALA_MAC_BRIDGE ? "sk" : "en");
}
bool palaSetUiLanguage(const String& language) {
  palaSettingsBegin();
  if (!ready || !supportedLanguage(language)) return false;
  if (palaNoteNameLocallyManaged())
    return palaSaveLocalNoteSettings(palaNoteName(), language, palaPrefersWeeklyCodexLimit());
  return prefs.putString("ui_lang", language) > 0;
}
int palaPlaybackVolume() {
  palaSettingsBegin();
  if (!ready) return 80;
  const int stored = prefs.getInt("volume", 80);
  const int step = constrain((stored + 10) / 20, 1, 5);
  return step * 20;
}
bool palaSetPlaybackVolume(int volume) {
  palaSettingsBegin();
  if (!ready) return false;
  const int step = constrain((volume + 10) / 20, 1, 5);
  return prefs.putInt("volume", step * 20) == sizeof(int32_t);
}
bool palaHasWifi() { return palaWifiProfileCount() > 0; }
bool palaHasOpenAiKey() {
  if (cachedApiConfigured < 0) cachedApiConfigured = palaOpenAiKey().length() > 10 ? 1 : 0;
  return cachedApiConfigured == 1;
}

bool palaSaveSettings(const String& ssid, const String& password, const String& apiKey,
                      const String& speechVoice) {
  palaSettingsBegin();
  if (!ready || ssid.length() > 32 || password.length() > 63 || (!ssid.length() && password.length()) ||
      apiKey.length() > 200 || (apiKey.length() && apiKey.length() < 11) ||
      (speechVoice != "cedar" && speechVoice != "coral")) return false;
  // Updating only the voice/key must not require adding a new network.
  bool ok = ssid.length() ? palaSaveWifiProfile(ssid, password) : true;
  if (!ok) return false;
  if (apiKey.length() > 0) {
    const bool stored = prefs.putString("openai_key", apiKey) == apiKey.length();
    ok = stored && ok;
    if (stored) {
      cachedApiConfigured = apiKey.length() > 10 ? 1 : 0;
      resumeBlockedCloudNotes();
    }
  }
  ok = (prefs.putString("tts_voice", speechVoice) == speechVoice.length()) && ok;
  return ok;
}

bool palaClearOpenAiKey() {
  palaSettingsBegin();
  if (!ready) return false;
  if (prefs.isKey("openai_key") && !prefs.remove("openai_key")) return false;
  cachedApiConfigured = 0;
  return true;
}
