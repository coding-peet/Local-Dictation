#include "provider.h"
#include "settings.h"
#include <Preferences.h>
#include "bridge_address.h"
#include "notes.h"
#include "settings_pair.h"

static Preferences prefs;
static bool ready = false;
static int cachedWifiCount = -1;
static int cachedApiConfigured = -1;

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

String palaSpeechVoice() {
  String voice = value("tts_voice");
  return voice == "coral" ? "coral" : "cedar";
}
String palaUiLanguage() {
  const String language = value("ui_lang");
  return supportedLanguage(language) ? language : (PALA_MAC_BRIDGE ? "sk" : "en");
}
bool palaSetUiLanguage(const String& language) {
  palaSettingsBegin();
  if (!ready || !supportedLanguage(language)) return false;
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
