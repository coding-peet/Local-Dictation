#pragma once

#include <Arduino.h>
#include <cstdint>

constexpr char kHomeForecastPath[] = "/.pala-weather.json";
constexpr size_t kHomeForecastMaxBytes = 1024;

enum class HomeForecastWrite { Rejected, Existing, Updated };

struct HomeForecast {
  int minimum;
  int maximum;
  int code;
};

bool homeForecastFor(const String& date, HomeForecast& forecast);
void loadHomeForecast();
HomeForecastWrite storeHomeForecast(const String& payload);
bool homeForecastNeedsRefresh(const String& date, int64_t utc);
bool refreshHomeForecast();
bool homeNoteNameNeedsRefresh();
bool refreshHomeNoteName();
