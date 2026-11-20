#pragma once

#include <Arduino.h>
#include <Preferences.h>

// The card holds only an authenticated ciphertext. The key stays in ESP32 NVS.
enum class WifiCardState { Missing, Empty, Ready, KeyMissing, Invalid, IoError };
enum class CardRecord { LegacyWifi, Settings };
WifiCardState loadWifiCard(Preferences& prefs, void* data, size_t size, CardRecord record);
WifiCardState saveWifiCard(Preferences& prefs, const void* data, size_t size, CardRecord record);
const char* wifiCardLabel(WifiCardState state);
