#include "home_holidays.h"
#include "holiday_calendar.h"
#include "home_bridge_status.h"
#include "bridge_client.h"
#include "settings.h"

#if PALA_BRIDGE_DISCOVERY
#include <ArduinoJson.h>
#include <HTTPClient.h>
#endif

namespace {
HolidayCalendar calendar;
}

void loadHomeHolidays() {
  const auto stored = palaSavedHolidays();
  parseHolidaySnapshot({stored.c_str(), stored.length()}, calendar);
}

bool homeHolidayFor(int year, int month, int day) {
  return calendar.contains(year, month, day);
}

bool homeHolidaysNeedRefresh(int currentYear) {
  return validCalendarDate(currentYear, 1, 1) && calendar.year != currentYear;
}

bool storeHomeHolidays(const String& snapshot, bool& changed) {
  HolidayCalendar candidate;
  if (!parseHolidaySnapshot({snapshot.c_str(), snapshot.length()}, candidate)) return false;
  changed = snapshot != palaSavedHolidays();
  if (changed && !palaStoreHolidays(snapshot)) return false;
  calendar = candidate;
  return true;
}

bool refreshHomeHolidays() {
#if PALA_BRIDGE_DISCOVERY
  ProviderClient client;
  client.setHandshakeTimeout(2);
  HTTPClient http;
  http.setConnectTimeout(1500);
  http.setTimeout(2000);
  const String url = client.url("/pala/holidays");
  if (url.isEmpty() || !http.begin(client, url)) return false;
  http.addHeader("Authorization", "Bearer " + client.bridge().token);
  const int response = http.GET();
  const int length = http.getSize();
  String payload;
  if (response == HTTP_CODE_OK && length > 0 &&
      length <= static_cast<int>(kHolidaySnapshotMaxBytes + 32)) payload = http.getString();
  http.end();
  if (length <= 0 || payload.length() != static_cast<size_t>(length)) return false;

  JsonDocument document;
  if (deserializeJson(document, payload) || !document["holidays"].is<const char*>()) return false;
  bool changed = false;
  if (!storeHomeHolidays(document["holidays"].as<String>(), changed)) return false;
  homeBridgeStatus.verified(HomeBridgeLink::Wifi);
  return changed;
#else
  return false;
#endif
}
