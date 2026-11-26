#pragma once
#include <Arduino.h>
#include "recording_sync_schedule.h"

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
constexpr size_t kPalaNoteNameMax = 12;
String palaNoteName();
bool palaSetNoteName(const String& name);
bool palaNoteNameLocallyManaged();
bool palaPrefersWeeklyCodexLimit();
bool palaSaveLocalNoteSettings(const String& name, const String& language, bool weeklyCodexLimit);
String palaStoredDeliveryCounts();
bool palaStoreDeliveryCounts(const String& counts);
String palaSavedHolidays();
bool palaStoreHolidays(const String& snapshot);
String palaBridgeAddress();
bool palaSetBridgeAddress(const String& address);
String palaUiLanguage();
SyncTiming palaSyncTiming();
bool palaSetSyncTiming(SyncTiming mode);
int palaIdleSleepMinutes();
bool palaSetIdleSleepMinutes(int minutes);
String palaDisplayPages();
bool palaDisplayPagesWrap();
bool palaSetDisplayPages(const String& order, bool wrap);
bool palaPreferBluetooth();
bool palaSetPreferBluetooth(bool preferred);
bool palaWifiEnabled();
bool palaBluetoothEnabled();
bool palaSetWirelessRadios(bool wifi, bool bluetooth);
bool palaSetUiLanguage(const String& language);
int palaPlaybackVolume();
bool palaSetPlaybackVolume(int volume);
bool palaHasWifi();
bool palaHasOpenAiKey();
bool palaSaveSettings(const String& ssid, const String& password, const String& apiKey,
                      const String& speechVoice);
bool palaClearOpenAiKey();
