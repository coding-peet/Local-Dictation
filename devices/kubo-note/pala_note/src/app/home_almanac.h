#pragma once

#include "date_validation.h"
#include <cstdint>

enum class WeatherGlyph { Unknown, Sun, PartlyCloudy, Cloud, Fog, Rain, Snow, Thunder };
enum class MoonPhase {
  New, WaxingCrescent, FirstQuarter, WaxingGibbous,
  Full, WaningGibbous, LastQuarter, WaningCrescent, Unknown
};

constexpr WeatherGlyph weatherGlyph(int code) {
  switch (code) {
    case 0: return WeatherGlyph::Sun;
    case 1: case 2: return WeatherGlyph::PartlyCloudy;
    case 3: return WeatherGlyph::Cloud;
    case 45: case 48: return WeatherGlyph::Fog;
    case 51: case 53: case 55: case 56: case 57: case 61: case 63: case 65: case 66: case 67:
    case 80: case 81: case 82: return WeatherGlyph::Rain;
    case 71: case 73: case 75: case 77: case 85: case 86: return WeatherGlyph::Snow;
    case 95: case 96: case 99: return WeatherGlyph::Thunder;
    default: return WeatherGlyph::Unknown;
  }
}

constexpr MoonPhase moonPhaseAt(int64_t utc) {
  if (utc < 1577836800) return MoonPhase::Unknown;
  // Mean synodic month anchored at the 2000-01-06 18:14 UTC new Moon.
  // This is an eight-phase display estimate, suitable away from exact transition times.
  constexpr int64_t newMoon = 947182440;
  constexpr int64_t lunation = 2551443;
  const int64_t age = (utc - newMoon) % lunation;
  return static_cast<MoonPhase>(((8 * age + lunation / 2) / lunation) % 8);
}

constexpr bool validForecastDate(const char* date) {
  if (!date) return false;
  int year = 0, month = 0, day = 0;
  for (int i = 0; i < 10; ++i) {
    if (i == 4 || i == 7) {
      if (date[i] != '-') return false;
      continue;
    }
    if (date[i] < '0' || date[i] > '9') return false;
    const int digit = date[i] - '0';
    if (i < 4) year = 10 * year + digit;
    else if (i < 7) month = 10 * month + digit;
    else day = 10 * day + digit;
  }
  return date[10] == '\0' && validCalendarDate(year, month, day);
}
