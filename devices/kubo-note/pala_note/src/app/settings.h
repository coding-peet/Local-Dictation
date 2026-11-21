#pragma once
#include <Arduino.h>

void palaSettingsBegin();
constexpr int PALA_MAX_WIFI_PROFILES = 5;
struct PalaWifiProfile {
  String ssid;
  String password;
};
int palaWifiProfileCount();
PalaWifiProfile palaWifiProfile(int index);
bool palaSaveWifiProfile(const String& ssid, const String& password);
bool palaRemoveWifiProfile(const String& ssid);
String palaWifiSsid();
String palaWifiPassword();
String palaLastWifiSsid();
bool palaSetLastWifiSsid(const String& ssid);
String palaOpenAiKey();
String palaSpeechVoice();
constexpr size_t kPalaNoteNameMax = 10;
String palaNoteName();
bool palaSetNoteName(const String& name);
bool palaNoteNameLocallyManaged();
bool palaSaveLocalNoteSettings(const String& name, const String& language);
String palaStoredDeliveryCounts();
bool palaStoreDeliveryCounts(const String& counts);
String palaBridgeAddress();
bool palaSetBridgeAddress(const String& address);
String palaUiLanguage();
bool palaSetUiLanguage(const String& language);
int palaPlaybackVolume();
bool palaSetPlaybackVolume(int volume);
bool palaHasWifi();
bool palaHasOpenAiKey();
bool palaSaveSettings(const String& ssid, const String& password, const String& apiKey,
                      const String& speechVoice);
bool palaClearOpenAiKey();
