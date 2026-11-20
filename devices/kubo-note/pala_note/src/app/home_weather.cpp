#include "home_weather.h"
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
constexpr int64_t kForecastRefreshSeconds = 3 * 3600;

struct ForecastDay {
  char date[11];
  HomeForecast weather;
};

struct ForecastCache {
  int64_t fetchedAt = 0;
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
  if (days.isNull() || !days.size() || days.size() > kForecastDays) return false;
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
  ForecastCache candidate{};
  if (decodeForecast(readHomeForecastFile(), candidate)) cache = candidate;
}

HomeForecastWrite storeHomeForecast(const String& payload) {
#if PALA_BRIDGE_DISCOVERY
  ForecastCache candidate{};
  if (!decodeForecast(payload, candidate) || candidate.fetchedAt < cache.fetchedAt)
    return HomeForecastWrite::Rejected;
  if (candidate.fetchedAt == cache.fetchedAt)
    return readHomeForecastFile() == payload ? HomeForecastWrite::Existing : HomeForecastWrite::Rejected;
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

bool homeNoteNameNeedsRefresh() { return !noteNameSynced; }

bool refreshHomeNoteName() {
#if PALA_BRIDGE_DISCOVERY
  ProviderClient client;
  client.setCACert(PALA_BRIDGE_CERT);
  client.setHandshakeTimeout(2);
  HTTPClient http;
  http.setConnectTimeout(1500);
  http.setTimeout(2000);
  if (!http.begin(client, "https://" PALA_AUTHORITY "/pala/name")) return false;
  http.addHeader("Authorization", "Bearer " PALA_BRIDGE_TOKEN);
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
  return noteNameSynced && changed;
#else
  return false;
#endif
}

bool refreshHomeForecast() {
#if PALA_BRIDGE_DISCOVERY
  ProviderClient client;
  client.setCACert(PALA_BRIDGE_CERT);
  client.setHandshakeTimeout(2);
  HTTPClient http;
  http.setConnectTimeout(1500);
  http.setTimeout(5500);
  if (!http.begin(client, "https://" PALA_AUTHORITY "/pala/weather")) return false;
  http.addHeader("Authorization", "Bearer " PALA_BRIDGE_TOKEN);
  const int status = http.GET();
  const int length = http.getSize();
  String payload;
  if (status == HTTP_CODE_OK && length > 0 && length <= static_cast<int>(kHomeForecastMaxBytes)) {
    payload = http.getString();
  }
  http.end();
  return length > 0 && payload.length() == static_cast<size_t>(length) &&
         storeHomeForecast(payload) == HomeForecastWrite::Updated;
#else
  return false;
#endif
}
