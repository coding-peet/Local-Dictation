#include "home_weather.h"
#include "owner_trace.h"
#include "home_bridge_status.h"
#include "home_almanac.h"
#include "bridge_client.h"
#include "storage_commit.h"
#include "settings.h"

#include <ArduinoJson.h>
#include <SD_MMC.h>

#if PALA_BRIDGE_DISCOVERY
#include <HTTPClient.h>
#endif

namespace {
constexpr size_t kForecastDays = 7;
constexpr size_t kLocationLength = 16;
constexpr int64_t kForecastRefreshSeconds = 3 * 3600;

struct ForecastDay {
  char date[11];
  HomeForecast weather;
};

struct ForecastCache {
  int64_t fetchedAt = 0;
  bool hasLocation = false;
  char location[kLocationLength + 1]{};
  ForecastDay days[kForecastDays];
  size_t count = 0;
};

ForecastCache cache;
bool noteNameSynced = false;

bool decodeForecast(const String& payload, ForecastCache& candidate) {
  if (!payload.length() || payload.length() > kHomeForecastMaxBytes) return false;
  JsonDocument doc;
  if (deserializeJson(doc, payload) || !doc["fetched_at"].is<int64_t>()) return false;
  const int64_t fetchedAt = doc["fetched_at"].as<int64_t>();
  if (moonPhaseAt(fetchedAt) == MoonPhase::Unknown) return false;

  const JsonArrayConst days = doc["days"].as<JsonArrayConst>();
  if (days.isNull() || days.size() > kForecastDays) return false;
  const JsonVariantConst locationValue = doc["location"];
  candidate.hasLocation = !locationValue.isUnbound();
  if (candidate.hasLocation) {
    const JsonString location = locationValue.as<JsonString>();
    const size_t length = location.size();
    if (location.isNull() || (length == 0 && days.size() != 0) ||
        (length != 0 && length != kLocationLength)) return false;
    for (size_t i = 0; i < length; ++i) {
      const char c = location.c_str()[i];
      if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
    }
    memcpy(candidate.location, location.c_str(), length);
  } else if (days.size() == 0) {
    return false;
  }
  candidate.fetchedAt = fetchedAt;

  for (const JsonObjectConst day : days) {
    const char* date = day["date"] | "";
    if (!validForecastDate(date) || (candidate.count &&
        strcmp(date, candidate.days[candidate.count - 1].date) <= 0)) return false;
    if (!day["minimum"].is<int>() || !day["maximum"].is<int>() || !day["code"].is<int>()) return false;
    const HomeForecast weather{day["minimum"].as<int>(), day["maximum"].as<int>(), day["code"].as<int>()};
    if (weather.minimum < -80 || weather.maximum > 65 || weather.minimum > weather.maximum ||
        weatherGlyph(weather.code) == WeatherGlyph::Unknown) return false;
    auto& next = candidate.days[candidate.count++];
    memcpy(next.date, date, sizeof(next.date));
    next.weather = weather;
  }
  return candidate.count == days.size();
}

#if PALA_BRIDGE_DISCOVERY
bool persistForecast(const String& payload) {
  const String path(kHomeForecastPath);
  const String temporary = path + ".tmp";
  File file = SD_MMC.open(temporary.c_str(), FILE_WRITE);
  if (!file) return false;
  const auto written = file.write(reinterpret_cast<const uint8_t*>(payload.c_str()), payload.length());
  file.close();
  if (written == payload.length() && commitStoredFile(SD_MMC, path, temporary)) return true;
  SD_MMC.remove(temporary.c_str());
  return false;
}
#endif

String readHomeForecastFile() {
  if (!recoverStoredFile(SD_MMC, String(kHomeForecastPath))) return "";
  File file = SD_MMC.open(kHomeForecastPath, FILE_READ);
  if (!file || file.isDirectory() || file.size() > kHomeForecastMaxBytes) return "";
  return file.readString();
}
}  // namespace

bool homeForecastFor(const String& date, HomeForecast& forecast) {
  for (size_t i = 0; i < cache.count; ++i) {
    if (date != cache.days[i].date) continue;
    forecast = cache.days[i].weather;
    return true;
  }
  return false;
}

void loadHomeForecast() {
  owner_trace::WorkScope ownerWork(owner_trace::Work::Forecast);
  ForecastCache candidate{};
  if (decodeForecast(readHomeForecastFile(), candidate)) cache = candidate;
}

HomeForecastWrite storeHomeForecast(const String& payload) {
#if PALA_BRIDGE_DISCOVERY
  ForecastCache candidate{};
  if (!decodeForecast(payload, candidate)) return HomeForecastWrite::Rejected;
  const bool sameLocation = candidate.hasLocation == cache.hasLocation &&
                            strcmp(candidate.location, cache.location) == 0;
  // An unavailable reply for the configured location keeps its valid cache.
  // Repeated empty controls for the same location never rewrite the SD card.
  if (candidate.count == 0 && sameLocation) return HomeForecastWrite::Existing;
  if (candidate.fetchedAt < cache.fetchedAt) return HomeForecastWrite::Rejected;
  if (candidate.fetchedAt == cache.fetchedAt && sameLocation && cache.count > 0) {
    return readHomeForecastFile() == payload ? HomeForecastWrite::Existing : HomeForecastWrite::Rejected;
  }
  if (!persistForecast(payload)) return HomeForecastWrite::Rejected;
  cache = candidate;
  return HomeForecastWrite::Updated;
#else
  return HomeForecastWrite::Rejected;
#endif
}

bool homeForecastNeedsRefresh(const String& date, int64_t utc) {
  if (!validForecastDate(date.c_str())) return false;
  HomeForecast unused{};
  return !homeForecastFor(date, unused) || utc < cache.fetchedAt ||
         utc - cache.fetchedAt >= kForecastRefreshSeconds;
}

bool homeNoteNameNeedsRefresh() { return !palaNoteNameLocallyManaged() && !noteNameSynced; }

bool refreshHomeNoteName() {
#if PALA_BRIDGE_DISCOVERY
  if (palaNoteNameLocallyManaged()) return false;
  ProviderClient client;
  client.setHandshakeTimeout(2);
  HTTPClient http;
  http.setConnectTimeout(1500);
  http.setTimeout(2000);
  const String url = client.url("/pala/name");
  if (url.isEmpty() || !http.begin(client, url)) return false;
  http.addHeader("Authorization", "Bearer " + client.bridge().token);
  const int status = http.GET();
  const int length = http.getSize();
  String payload;
  if (status == HTTP_CODE_OK && length > 0 && length <= 64) payload = http.getString();
  http.end();
  if (!payload.length() || payload.length() != static_cast<size_t>(length)) return false;
  JsonDocument document;
  if (deserializeJson(document, payload) || !document["name"].is<const char*>()) return false;
  const String name = document["name"].as<String>();
  const bool changed = name != palaNoteName();
  noteNameSynced = palaSetNoteName(name);
  if (noteNameSynced) homeBridgeStatus.verified(HomeBridgeLink::Wifi);
  return noteNameSynced && changed;
#else
  return false;
#endif
}

bool refreshHomeForecast() {
#if PALA_BRIDGE_DISCOVERY
  ProviderClient client;
  client.setHandshakeTimeout(2);
  HTTPClient http;
  http.setConnectTimeout(1500);
  http.setTimeout(5500);
  const String url = client.url("/pala/weather");
  if (url.isEmpty() || !http.begin(client, url)) return false;
  http.addHeader("Authorization", "Bearer " + client.bridge().token);
  const int status = http.GET();
  const int length = http.getSize();
  String payload;
  if (status == HTTP_CODE_OK && length > 0 && length <= static_cast<int>(kHomeForecastMaxBytes)) {
    payload = http.getString();
  }
  http.end();
  if (length <= 0 || payload.length() != static_cast<size_t>(length)) return false;
  const auto saved = storeHomeForecast(payload);
  if (saved != HomeForecastWrite::Rejected) homeBridgeStatus.verified(HomeBridgeLink::Wifi);
  return saved == HomeForecastWrite::Updated;
#else
  return false;
#endif
}
