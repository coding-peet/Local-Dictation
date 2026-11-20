#include <Arduino.h>
#include <ArduinoJson.h>
#include <GxEPD2_BW.h>
#include <Fonts/FreeSansBold24pt7b.h>
#include <HTTPClient.h>
#include <Preferences.h>
#include <SPI.h>
#include <WebServer.h>
#include <WiFi.h>
#include <WiFiClientSecure.h>
#include <Wire.h>
#include <atomic>
#include <driver/gpio.h>
#include <esp_sntp.h>
#include <esp_timer.h>
#include <sys/time.h>
#include "WeatherRoot.h"
#include "WifiCard.h"

namespace {
constexpr int EPD_POWER = 6, POWER_HOLD = 17, BOOT_BUTTON = 0, CODEC_POWER = 42;
constexpr int SDA_PIN = 47, SCL_PIN = 48;
constexpr uint8_t RTC = 0x51, SHT = 0x70;
constexpr time_t MIN_TIME = 1735689600;  // 2025-01-01 UTC
constexpr char TZ_RULE[] = "CET-1CEST,M3.5.0,M10.5.0/3";
constexpr uint32_t WEATHER_INTERVAL = 30 * 60 * 1000;
constexpr uint32_t WEATHER_RETRY = 2 * 60 * 1000;
constexpr uint32_t WIFI_TIMEOUT = 12000, WIFI_RETRY = 30000;
constexpr int MAX_NETWORKS = 5;
constexpr uint32_t MEETING_FRESH_MS = 5 * 60 * 1000;

GxEPD2_BW<GxEPD2_154_D67, 200> panel(GxEPD2_154_D67(11, 10, 9, 8));
GFXcanvas1 clockGlyphs(200, 56);
WebServer web(80);
Preferences settings;

struct WiFiProfile { char ssid[33]; char key[65]; };
struct SavedNetworks { WiFiProfile slots[MAX_NETWORKS]; };
struct WifiCardPayload { SavedNetworks networks; char preferred[33]; };
struct Location { double lat; double lon; };
struct SettingsCardPayload {
  SavedNetworks networks;
  char preferred[33], recent[33], panelKey[17], note[237];
  Location location;
};
struct Forecast { float temperature; int rain; int code; bool success; };

SavedNetworks networks{};
WifiCardState cardState = WifiCardState::Missing;
Location location{48.15, 17.11};  // Bratislava, adjustable in the panel.
Forecast forecast{};
QueueHandle_t weatherJobs, weatherResults;
String note, hostname, accessKey, csrf, lastNetwork, requestedNetwork, nextAccessKey;
String preferredNetwork, meetingTitle;
time_t meetingStart = 0;
uint32_t meetingReceivedAt = 0;
bool receivedMeeting = false, meetingDemo = false;
std::atomic<bool> timeWasSynced{false};
time_t lastSync = 0;
int humidity = -1;
const char* humidityStatus = "pending";
int64_t countdownDeadline = 0;
bool timerDone = false, ap = false, joining = false, linked = false;
bool applyNetwork = false, fullRefresh = true;
uint32_t joiningSince = 0, apCloses = 0, nextWeather = 0, lastWeather = 0;
uint32_t sensorAt = 0, buttonSince = 0, showKeyUntil = 0, wifiApplyAt = 0;
uint32_t nextNetworkTry = 0, keyApplyAt = 0;
uint32_t wifiImportUntil = 0;
int joiningSlot = -1;
unsigned triedNetworks = 0;
bool weatherRunning = false, forecastValid = false, buttonDown = false;
unsigned partialCount = 0;
String lastFrame;

bool reached(uint32_t now, uint32_t target) { return int32_t(now - target) >= 0; }

String randomHex() {
  char text[17];
  snprintf(text, sizeof(text), "%08lx%08lx", (unsigned long)esp_random(), (unsigned long)esp_random());
  return text;
}

bool sendI2C(uint8_t address, const uint8_t* data, size_t size) {
  Wire.beginTransmission(address);
  Wire.write(data, size);
  return Wire.endTransmission() == 0;
}

uint8_t checksum(const uint8_t* data) {
  uint8_t crc = 0xff;
  for (int i = 0; i < 2; ++i) {
    crc ^= data[i];
    for (int bit = 0; bit < 8; ++bit) crc = (crc & 128) ? (crc << 1) ^ 0x31 : crc << 1;
  }
  return crc;
}

int readHumidity() {
  const uint8_t wake[] = {0x35, 0x17}, measure[] = {0x78, 0x66}, sleep[] = {0xb0, 0x98};
  uint8_t data[6];
  int result = -1;
  humidityStatus = "wake";
  if (!sendI2C(SHT, wake, sizeof(wake))) return result;
  delay(50);
  humidityStatus = "measure";
  if (sendI2C(SHT, measure, sizeof(measure))) {
    delay(20);
    humidityStatus = "receive";
    if (Wire.requestFrom(SHT, uint8_t(sizeof(data))) == sizeof(data)) {
      Wire.readBytes(data, sizeof(data));
      humidityStatus = "CRC";
      if (checksum(data) == data[2] && checksum(data + 3) == data[5]) {
        result = ((uint32_t(data[3]) * 256 + data[4]) * 100 + 32768) / 65536;
        humidityStatus = "OK";
      }
    }
  }
  sendI2C(SHT, sleep, sizeof(sleep));
  return result;
}

int fromBCD(uint8_t n) { return (n >> 4) * 10 + (n & 15); }
uint8_t toBCD(int n) { return ((n / 10) << 4) | (n % 10); }

void restoreClock() {
  const uint8_t secondsRegister = 4;
  if (!sendI2C(RTC, &secondsRegister, 1) || Wire.requestFrom(RTC, uint8_t(7)) != 7) return;
  uint8_t d[7];
  Wire.readBytes(d, sizeof(d));
  if (d[0] & 0x80) return;  // Oscillator-stop flag.
  tm utc{};
  utc.tm_sec = fromBCD(d[0] & 0x7f);
  utc.tm_min = fromBCD(d[1] & 0x7f);
  utc.tm_hour = fromBCD(d[2] & 0x3f);
  utc.tm_mday = fromBCD(d[3] & 0x3f);
  utc.tm_mon = fromBCD(d[5] & 0x1f) - 1;
  utc.tm_year = fromBCD(d[6]) + 100;
  if (utc.tm_sec > 59 || utc.tm_min > 59 || utc.tm_hour > 23 || utc.tm_mday < 1 ||
      utc.tm_mday > 31 || utc.tm_mon < 0 || utc.tm_mon > 11) return;
  // The process TZ is UTC here. setup() installs the local timezone afterwards.
  const time_t epoch = mktime(&utc);
  if (epoch >= MIN_TIME) {
    timeval clock{epoch, 0};
    settimeofday(&clock, nullptr);
  }
}

bool saveClock(time_t epoch) {
  tm utc{};
  gmtime_r(&epoch, &utc);
  if (utc.tm_year < 100 || utc.tm_year > 199) return false;
  const uint8_t d[] = {4, toBCD(utc.tm_sec), toBCD(utc.tm_min), toBCD(utc.tm_hour),
    toBCD(utc.tm_mday), toBCD(utc.tm_wday), toBCD(utc.tm_mon + 1), toBCD(utc.tm_year - 100)};
  return sendI2C(RTC, d, sizeof(d));
}

void synchronise() { configTzTime(TZ_RULE, "pool.ntp.org", "time.cloudflare.com"); }
void recordSync(struct timeval*) { timeWasSynced.store(true); }

Forecast fetchWeather(Location place) {
  Forecast result{0, -1, -1, false};
  WiFiClientSecure client;
  client.setCACert(WEATHER_ROOT);
  client.setHandshakeTimeout(5);
  HTTPClient http;
  http.setConnectTimeout(4000);
  http.setTimeout(5000);
  http.useHTTP10(true);
  String url = "https://api.open-meteo.com/v1/forecast?latitude=" + String(place.lat, 4) +
    "&longitude=" + String(place.lon, 4) +
    "&current=temperature_2m,weather_code&hourly=precipitation_probability&forecast_hours=3";
  if (!http.begin(client, url) || http.GET() != HTTP_CODE_OK) return result;
  JsonDocument data;
  if (deserializeJson(data, http.getStream())) return result;
  JsonVariant temperature = data["current"]["temperature_2m"];
  if (!temperature.is<float>()) return result;
  result.temperature = temperature.as<float>();
  result.code = data["current"]["weather_code"] | -1;
  for (JsonVariant rain : data["hourly"]["precipitation_probability"].as<JsonArray>()) {
    if (rain.is<int>()) result.rain = max(result.rain, rain.as<int>());
  }
  result.success = true;
  return result;
}

void weatherWorker(void*) {
  Location place;
  while (xQueueReceive(weatherJobs, &place, portMAX_DELAY) == pdTRUE) {
    Forecast result = fetchWeather(place);
    xQueueOverwrite(weatherResults, &result);
  }
  vTaskDelete(nullptr);
}

void updateWeather(uint32_t now) {
  if (weatherResults == nullptr) return;
  Forecast result;
  if (xQueueReceive(weatherResults, &result, 0) == pdTRUE) {
    weatherRunning = false;
    nextWeather = now + (result.success ? WEATHER_INTERVAL : WEATHER_RETRY);
    if (result.success) {
      forecast = result;
      forecastValid = true;
      lastWeather = now;
    }
    Serial.printf("Weather: %s\n", result.success ? "OK" : "unavailable");
  }
  if (linked && !weatherRunning && time(nullptr) >= MIN_TIME && reached(now, nextWeather)) {
    weatherRunning = xQueueSend(weatherJobs, &location, 0) == pdTRUE;
  }
}

String weatherLine() {
  if (!forecastValid) return "Vonku  --";
  String text = "Vonku  " + String(forecast.temperature, 0) + " C";
  if (forecast.rain >= 0) text += " / zrazky " + String(forecast.rain) + "%";
  if (millis() - lastWeather >= 2 * WEATHER_INTERVAL) text += " *";
  return text;
}

unsigned remainingSeconds() {
  if (!countdownDeadline) return 0;
  int64_t remaining = countdownDeadline - esp_timer_get_time();
  return remaining > 0 ? (remaining + 999999) / 1000000 : 0;
}

void startAP() {
  if (ap) return;
  WiFi.mode(WIFI_AP_STA);
  ap = WiFi.softAP(hostname.c_str(), accessKey.c_str());
  apCloses = 0;
  if (ap) {
    showKeyUntil = millis() + 120000;
    Serial.printf("Setup AP: %s at %s\n", hostname.c_str(), WiFi.softAPIP().toString().c_str());
  }
}

int networkSlot(const String& name) {
  for (int i = 0; i < MAX_NETWORKS; ++i) {
    if (networks.slots[i].ssid[0] && name == networks.slots[i].ssid) return i;
  }
  return -1;
}

bool validAccessKey(const String& key) {
  if (key.length() < 8 || key.length() > 16) return false;
  for (unsigned i = 0; i < key.length(); ++i) {
    const char c = key[i];
    if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))) return false;
  }
  return true;
}

void restoreSettingsFromCard() {
  SettingsCardPayload payload{};
  cardState = loadWifiCard(settings, &payload, sizeof(payload), CardRecord::Settings);
  if (cardState != WifiCardState::Ready) return;

  for (WiFiProfile& slot : payload.networks.slots) { slot.ssid[32] = 0; slot.key[64] = 0; }
  payload.preferred[32] = payload.recent[32] = payload.panelKey[16] = payload.note[236] = 0;
  if (settings.getBytesLength("wifi-list") != sizeof(payload.networks) && settings.getBytesLength("wifi") != sizeof(WiFiProfile)) {
    settings.putBytes("wifi-list", &payload.networks, sizeof(payload.networks));
  }
  if (!settings.isKey("wifi-primary")) settings.putString("wifi-primary", payload.preferred);
  if (!settings.isKey("last-network")) settings.putString("last-network", payload.recent);
  if (!settings.isKey("note")) settings.putString("note", payload.note);
  if (!validAccessKey(settings.getString("desk-key", "")) && validAccessKey(payload.panelKey)) {
    settings.putString("desk-key", payload.panelKey);
  }
  if (settings.getBytesLength("location") != sizeof(Location) &&
      isfinite(payload.location.lat) && abs(payload.location.lat) <= 90 &&
      isfinite(payload.location.lon) && abs(payload.location.lon) <= 180) {
    settings.putBytes("location", &payload.location, sizeof(payload.location));
  }
}

void loadNetworks() {
  bool haveProfiles = false;
  if (settings.getBytesLength("wifi-list") == sizeof(networks) &&
      settings.getBytes("wifi-list", &networks, sizeof(networks)) == sizeof(networks)) {
    for (WiFiProfile& slot : networks.slots) { slot.ssid[32] = 0; slot.key[64] = 0; }
    haveProfiles = true;
  } else if (settings.getBytesLength("wifi") == sizeof(WiFiProfile)) {
    WiFiProfile legacy{};
    if (settings.getBytes("wifi", &legacy, sizeof(legacy)) == sizeof(legacy)) {
      legacy.ssid[32] = 0;
      legacy.key[64] = 0;
      networks.slots[0] = legacy;
      haveProfiles = true;
      if (settings.putBytes("wifi-list", &networks, sizeof(networks)) == sizeof(networks)) {
        settings.remove("wifi");
      }
    }
  }
  lastNetwork = settings.getString("last-network", "");
  preferredNetwork = settings.getString("wifi-primary", "");
  if (!haveProfiles) {
    WifiCardPayload payload{};
    cardState = loadWifiCard(settings, &payload, sizeof(payload), CardRecord::LegacyWifi);
    if (cardState == WifiCardState::Ready) {
      for (WiFiProfile& slot : payload.networks.slots) { slot.ssid[32] = 0; slot.key[64] = 0; }
      payload.preferred[32] = 0;
      networks = payload.networks;
      preferredNetwork = payload.preferred;
      settings.putBytes("wifi-list", &networks, sizeof(networks));
      settings.putString("wifi-primary", preferredNetwork);
    }
  }
  if (networkSlot(preferredNetwork) < 0) preferredNetwork = "";
}

void backupSettings() {
  SettingsCardPayload payload{};
  payload.networks = networks;
  preferredNetwork.toCharArray(payload.preferred, sizeof(payload.preferred));
  lastNetwork.toCharArray(payload.recent, sizeof(payload.recent));
  accessKey.toCharArray(payload.panelKey, sizeof(payload.panelKey));
  note.toCharArray(payload.note, sizeof(payload.note));
  payload.location = location;
  cardState = saveWifiCard(settings, &payload, sizeof(payload), CardRecord::Settings);
}

bool saveNetworks(const SavedNetworks& saved) {
  if (settings.putBytes("wifi-list", &saved, sizeof(saved)) != sizeof(saved)) return false;
  networks = saved;
  if (networkSlot(preferredNetwork) < 0) {
    preferredNetwork = "";
    settings.putString("wifi-primary", "");
  }
  backupSettings();
  return true;
}

enum class ProfileSave { Saved, Invalid, Full, StorageError };

ProfileSave saveProfile(const String& name, const String& key) {
  if (!name.length() || name.length() > 32 || key.length() > 64 || (key.length() && key.length() < 8)) {
    return ProfileSave::Invalid;
  }
  int index = networkSlot(name);
  if (index < 0) {
    for (int i = 0; i < MAX_NETWORKS; ++i) {
      if (!networks.slots[i].ssid[0]) { index = i; break; }
    }
  }
  if (index < 0) return ProfileSave::Full;
  SavedNetworks updated = networks;
  WiFiProfile& entry = updated.slots[index];
  entry = {};
  name.toCharArray(entry.ssid, sizeof(entry.ssid));
  key.toCharArray(entry.key, sizeof(entry.key));
  return saveNetworks(updated) ? ProfileSave::Saved : ProfileSave::StorageError;
}

void queueConnection(const String& name) {
  requestedNetwork = name;
  wifiApplyAt = millis() + 650;
  applyNetwork = true;
}

int nextNetwork() {
  const int preferred = networkSlot(preferredNetwork);
  if (preferred >= 0 && !(triedNetworks & (1U << preferred))) return preferred;
  const int recent = networkSlot(lastNetwork);
  if (recent >= 0 && !(triedNetworks & (1U << recent))) return recent;
  for (int i = 0; i < MAX_NETWORKS; ++i) {
    if (networks.slots[i].ssid[0] && !(triedNetworks & (1U << i))) return i;
  }
  return -1;
}

void connectNetwork(int index, uint32_t now) {
  joining = true;
  joiningSlot = index;
  triedNetworks |= 1U << index;
  joiningSince = now;
  apCloses = 0;
  WiFi.disconnect(false, false);
  linked = false;
  const WiFiProfile& choice = networks.slots[index];
  WiFi.begin(choice.ssid, choice.key);
}

void updateNetwork(uint32_t now) {
  if (nextAccessKey.length() && reached(now, keyApplyAt)) {
    accessKey = nextAccessKey;
    nextAccessKey = "";
    csrf = randomHex();
    backupSettings();
    if (ap) { WiFi.mode(WIFI_STA); ap = false; }
    startAP();
  }
  if (applyNetwork && reached(now, wifiApplyAt)) {
    applyNetwork = false;
    joining = false;
    joiningSlot = -1;
    triedNetworks = 0;
    apCloses = 0;
    nextNetworkTry = now;
    const int target = networkSlot(requestedNetwork);
    if (target >= 0) { connectNetwork(target, now); return; }
    WiFi.disconnect(false, false);
    linked = false;
  }
  const bool connected = WiFi.status() == WL_CONNECTED;
  const String current = connected ? WiFi.SSID() : String();
  const bool up = connected && networkSlot(current) >= 0 &&
    (!joining || current == networks.slots[joiningSlot].ssid);
  if (up && !linked) {
    if (lastNetwork != current) {
      lastNetwork = current;
      if (settings.putString("last-network", lastNetwork)) backupSettings();
    }
    joining = false;
    joiningSlot = -1;
    nextWeather = now;
    showKeyUntil = 0;
    if (ap) apCloses = now + 12000;
    synchronise();
    Serial.printf("LAN IP: %s\n", WiFi.localIP().toString().c_str());
  }
  if (!up && linked) {
    triedNetworks = 0;
    nextNetworkTry = now;
    apCloses = 0;
  }
  linked = up;
  if (!up && joining && reached(now, joiningSince + WIFI_TIMEOUT)) {
    joining = false;
    joiningSlot = -1;
    WiFi.disconnect(false, false);
    nextNetworkTry = now;
  }
  if (!up && !joining && reached(now, nextNetworkTry)) {
    const int index = nextNetwork();
    if (index >= 0) connectNetwork(index, now);
    else {
      startAP();
      triedNetworks = 0;
      nextNetworkTry = now + WIFI_RETRY;
    }
  }
  if (up && apCloses && reached(now, apCloses)) {
    WiFi.softAPdisconnect(true);
    WiFi.mode(WIFI_STA);
    ap = false;
    apCloses = 0;
  }
}

String escapeHTML(String value) {
  value.replace("&", "&amp;"); value.replace("<", "&lt;"); value.replace(">", "&gt;");
  value.replace("\"", "&quot;"); value.replace("'", "&#39;");
  return value;
}

bool authorize() {
  if (web.authenticate("desk", accessKey.c_str())) return true;
  web.requestAuthentication(DIGEST_AUTH, "Desk gadget");
  return false;
}

String form(const char* route) {
  return String("<form method='post' action='") + route + "'><input type='hidden' name='token' value='" + csrf + "'>";
}

String passwordField(const char* id, const char* rules) {
  return String("<div class='password-field'><input type='password' autocomplete='new-password' id='") + id +
    "' name='" + id + "' " + rules + "><button type='button' class='password-toggle' aria-controls='" + id +
    "' aria-pressed='false' aria-label='Zobraziť heslo' title='Zobraziť heslo'>"
    "<svg viewBox='0 0 24 24' aria-hidden='true' fill='none' stroke='currentColor' stroke-width='1.75'>"
    "<path d='M2 12s3.6-6 10-6 10 6 10 6-3.6 6-10 6S2 12 2 12Z'/><circle cx='12' cy='12' r='2.5'/>"
    "<path class='password-slash' d='m4 4 16 16'/></svg></button></div>";
}

bool setupClient() {
  return ap && web.client().localIP() == WiFi.softAPIP();
}

bool requireSetupClient() {
  if (setupClient()) return true;
  web.send(403, "text/plain; charset=utf-8", "Pripoj sa na nastavovaciu Wi-Fi zariadenia.");
  return false;
}

String timeText(time_t epoch, const char* format, const char* missing = "--") {
  if (epoch < MIN_TIME) return missing;
  tm here{};
  localtime_r(&epoch, &here);
  char text[32];
  strftime(text, sizeof(text), format, &here);
  return text;
}

bool meetingIsFresh() {
  return receivedMeeting && millis() - meetingReceivedAt < MEETING_FRESH_MS;
}

String meetingCountdown() {
  const time_t now = time(nullptr);
  if (now < MIN_TIME || !meetingIsFresh() || meetingStart <= now) return "";
  const int minutes = int((meetingStart - now + 59) / 60);
  if (minutes >= 1440) return "o " + String(minutes / 1440) + "d " + String((minutes % 1440) / 60) + "h";
  if (minutes >= 60) return "o " + String(minutes / 60) + "h " + String(minutes % 60) + "m";
  return "o " + String(minutes) + " min";
}

struct PageTab { const char* key; const char* label; };
constexpr PageTab pageTabs[] = {
  {"overview", "Čas a počasie"}, {"note", "Poznámka"},
  {"timer", "Časovač"}, {"outlook", "Outlook"}, {"wifi", "Wi-Fi a heslo"},
};

const PageTab& selectedTab(const String& key) {
  for (const PageTab& tab : pageTabs) if (key == tab.key) return tab;
  return pageTabs[0];
}

void appendTabs(String& html, const PageTab& active) {
  html += "<nav aria-label='Sekcie panelu'>";
  for (const PageTab& tab : pageTabs) {
    html += "<a href='/?tab=";
    html += tab.key;
    html += (&tab == &active ? "' aria-current='page'>" : "'>");
    html += tab.label;
    html += "</a>";
  }
  html += "</nav>";
}

void page() {
  if (!authorize()) return;
  const PageTab& active = selectedTab(web.arg("tab"));
  String html = F(R"HTML(<!doctype html><html lang="sk"><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1"><title>Desk gadget</title>
<style>*{box-sizing:border-box}body{margin:0;background:#f0f2ef;color:#202820;font:16px system-ui,sans-serif}
main{max-width:42rem;margin:auto;padding:1.5rem}header{padding:.3rem 0 .7rem}h1{margin:.35rem 0}h2{margin:0 0 .6rem;font-size:1.15rem}
section{background:white;border:1px solid #dce3da;border-radius:14px;padding:1rem;margin:.75rem 0}p{margin:.4rem 0 .7rem}
label{display:block;margin:.7rem 0 .3rem}input,textarea,button{font:inherit;padding:.58rem;border:1px solid #a4b2a3;border-radius:7px}
input,textarea{width:100%}button{background:#254739;color:white;cursor:pointer;margin-top:.6rem}a{color:#254739}small{color:#495748}
nav{display:flex;flex-wrap:wrap;gap:.4rem;margin-bottom:.75rem}nav a{border:1px solid #a4b2a3;border-radius:7px;padding:.55rem .75rem;background:white;text-decoration:none}
nav a[aria-current=page]{background:#254739;color:white;border-color:#254739}nav a:focus-visible,.password-toggle:focus-visible{outline:2px solid #254739;outline-offset:2px}
.password-field{position:relative}.password-field input{padding-right:3rem}.password-toggle{position:absolute;right:0;top:0;bottom:0;width:2.75rem;margin:0;padding:.45rem;background:transparent;color:#254739;border-color:transparent;display:grid;place-items:center}.password-toggle svg{width:1.25rem;height:1.25rem}.password-slash{display:none}.password-toggle[aria-pressed=true] .password-slash{display:block}</style>
<main><header><h1>Desk gadget</h1>)HTML");
  html += "<p>" + timeText(time(nullptr), "%d.%m.%Y &nbsp; %H:%M") + " · " + weatherLine();
  html += humidity < 0 ? " · Vlhkosť --" : " · Vlhkosť " + String(humidity) + "%";
  html += "</p></header>";
  appendTabs(html, active);
  const String tab = active.key;
  if (tab == "note") {
    html += "<section><h2>Poznámka</h2>" + form("/note") +
      "<label for='note'>Text na displeji</label><textarea id='note' name='note' rows='2' maxlength='59'>" +
      escapeHTML(note) + "</textarea><button>Uložiť</button></form></section>";
  } else if (tab == "timer") {
    html += "<section><h2>Časovač</h2><p>";
    unsigned seconds = remainingSeconds();
    if (seconds) html += "Zostáva " + String(seconds / 60) + " min " + String(seconds % 60) + " s";
    else html += timerDone ? "Čas vypršal" : "Pripravený";
    html += "</p>" + form("/timer") + "<label for='minutes'>Minúty</label><input id='minutes' type='number' name='minutes' min='1' max='180' value='15' required><button>Spustiť</button></form>" +
      form("/stop") + "<button>Zrušiť časovač</button></form></section>";
  } else if (tab == "outlook") {
    html += "<section><h2>Najbližšia schôdzka</h2>";
    const String countdown = meetingCountdown();
    if (countdown.length()) {
      html += "<p><b>" + escapeHTML(meetingTitle) + "</b>" + (meetingDemo ? " · skúšobný údaj" : "") +
        "</p><p>" + timeText(meetingStart, "%d.%m. %H:%M") + " · " + countdown + "</p>";
    } else if (!receivedMeeting) html += "<p>Čakám na údaje z PC.</p>";
    else if (!meetingIsFresh()) html += "<p>Údaj z PC je neaktuálny.</p>";
    else if (!meetingStart) html += "<p>V nasledujúcich 30 dňoch nie je schôdzka.</p>";
    else if (time(nullptr) < MIN_TIME) html += "<p>Čakám na synchronizáciu hodín.</p>";
    else html += "<p>Schôdzka už začala. Čakám na ďalšiu.</p>";
    html += "<p><small>Údaje posiela skript OutlookBridge.ps1 z PC cez USB.</small></p></section>";
  } else if (tab == "overview") {
    html += "<section><h2>Čas a počasie</h2><p>NTP: " + timeText(lastSync, "%d.%m. %H:%M:%S", "čaká na synchronizáciu") +
      "</p>" + form("/sync") + "<button>Synchronizovať čas</button></form>" +
      form("/clock") + "<input type='hidden' name='epoch'><button onclick='this.form.epoch.value=Math.floor(Date.now()/1000)'>Nastaviť podľa PC</button></form>" +
      form("/location") + "<label for='lat'>Zemepisná šírka</label><input id='lat' name='lat' type='number' step='0.0001' min='-90' max='90' value='" + String(location.lat, 4) +
      "' required><label for='lon'>Zemepisná dĺžka</label><input id='lon' name='lon' type='number' step='0.0001' min='-180' max='180' value='" + String(location.lon, 4) +
      "' required><button>Uložiť polohu</button></form><p><small><a href='https://open-meteo.com/'>Počasie: Open-Meteo</a> · % je najvyššia hodinová pravdepodobnosť zrážok v nasledujúcich 3 h. * znamená starší údaj.</small></p></section>";
  } else if (setupClient()) {
    html += "<section><h2>Wi-Fi siete</h2><p>Doska udrží až päť sietí a pri strate spojenia skúsi ďalšiu uloženú sieť.</p>";
    html += "<p><small>" + String(wifiCardLabel(cardState)) + "</small></p>";
    if (joining) html += "<p>Pripájanie… Po úspechu sa LAN adresa zobrazí na displeji.</p>";
    const String current = linked ? WiFi.SSID() : String();
    for (int i = 0; i < MAX_NETWORKS; ++i) {
      const char* ssid = networks.slots[i].ssid;
      if (!ssid[0]) continue;
      html += "<p><b>" + escapeHTML(ssid) + "</b>" + (current == ssid ? " · pripojená" : "") +
        (preferredNetwork == ssid ? " · predvolená" : "") + "</p>";
      if (preferredNetwork != ssid) {
        html += form("/wifi-default") + "<input type='hidden' name='slot' value='" + String(i) +
          "'><button>Nastaviť ako predvolenú</button></form>";
      }
      html += form("/wifi-forget") + "<input type='hidden' name='slot' value='" + String(i) +
        "'><button>Zabudnúť sieť</button></form>";
    }
    html += form("/wifi") + "<label for='ssid'>Názov siete (2,4 GHz)</label><input id='ssid' name='ssid' maxlength='32' required>"
      "<label for='key'>Heslo Wi-Fi</label>" + passwordField("key", "maxlength='64'") +
      "<p><small>Rovnaký názov siete zmení heslo k už uloženej sieti.</small></p>"
      "<label><input style='width:auto' type='checkbox' name='connect' value='1'> Pripojiť teraz</label>"
      "<button>Uložiť sieť</button></form></section>";
    html += "<section><h2>Heslo zariadenia</h2><p>Platí pre „Desk“ Wi-Fi aj prihlásenie do webového panelu.</p>" + form("/password") +
      "<label for='newkey'>Nové heslo (8 až 16 písmen alebo číslic)</label>" +
      passwordField("newkey", "pattern='[A-Za-z0-9]{8,16}' maxlength='16' required") +
      "<label for='repeat'>Zopakuj nové heslo</label>" +
      passwordField("repeat", "pattern='[A-Za-z0-9]{8,16}' maxlength='16' required") +
      "<button>Zmeniť heslo</button></form></section>";
  } else {
    html += "<section><h2>Wi-Fi a heslo</h2><p>Podrž BOOT tri sekundy a uvoľni. Pripoj sa k Wi-Fi „" + hostname +
      "“ a otvor <a href='http://192.168.4.1/?tab=wifi'>192.168.4.1</a>. Tam upravíš uložené siete a heslo zariadenia.</p><p>" +
      String(wifiCardLabel(cardState)) + "</p></section>";
  }
  html += F(R"HTML(</main><script>
document.querySelectorAll('.password-toggle').forEach(function (button) {
  button.addEventListener('click', function () {
    const input = document.getElementById(button.getAttribute('aria-controls'));
    const visible = input.type === 'password';
    input.type = visible ? 'text' : 'password';
    const label = visible ? 'Skryť heslo' : 'Zobraziť heslo';
    button.setAttribute('aria-pressed', String(visible));
    button.setAttribute('aria-label', label);
    button.title = label;
  });
});
</script></html>)HTML");
  web.sendHeader("Cache-Control", "no-store");
  web.send(200, "text/html; charset=utf-8", html);
}

bool parseNumber(const String& text, double low, double high, double& value) {
  char* end;
  value = strtod(text.c_str(), &end);
  return end != text.c_str() && *end == '\0' && isfinite(value) && low <= value && value <= high;
}

void redirectTab(const char* tab) {
  web.sendHeader("Location", String("/?tab=") + tab);
  web.send(303, "text/plain", "");
}

void post(const char* route, WebServer::THandlerFunction action) {
  web.on(route, HTTP_POST, [action] {
    if (!authorize()) return;
    if (web.arg("token") != csrf) {
      web.send(403, "text/plain", "Obnov stranku a skus znova.");
      return;
    }
    action();
  });
}

void beginWeb() {
  web.on("/", HTTP_GET, page);
  post("/note", [] {
    String updated = web.arg("note");
    updated.trim();
    updated.replace("\r", " "); updated.replace("\n", " ");
    if (updated.length() > 236) { web.send(400, "text/plain", "Poznamka je prilis dlha."); return; }
    if (updated != note) {
      settings.putString("note", updated);
      if (!settings.isKey("note") || settings.getString("note", "") != updated) {
        web.send(500, "text/plain; charset=utf-8", "Poznámku sa nepodarilo uložiť."); return;
      }
      note = updated;
      backupSettings();
    }
    showKeyUntil = 0;
    redirectTab("note");
  });
  post("/timer", [] {
    double minutes;
    if (!parseNumber(web.arg("minutes"), 1, 180, minutes) || minutes != floor(minutes)) {
      web.send(400, "text/plain", "Zadaj 1 az 180 celych minut."); return;
    }
    countdownDeadline = esp_timer_get_time() + static_cast<int64_t>(minutes) * 60000000;
    timerDone = false;
    showKeyUntil = 0;
    redirectTab("timer");
  });
  post("/stop", [] { countdownDeadline = 0; timerDone = false; redirectTab("timer"); });
  post("/sync", [] {
    if (!linked) { web.send(409, "text/plain", "Najprv pripoj zariadenie na Wi-Fi."); return; }
    synchronise();
    redirectTab("overview");
  });
  post("/clock", [] {
    double epoch;
    if (!parseNumber(web.arg("epoch"), MIN_TIME, 4102444799.0, epoch) || epoch != floor(epoch)) {
      web.send(400, "text/plain", "Neplatny cas prehliadaca."); return;
    }
    timeval clock{static_cast<time_t>(epoch), 0};
    settimeofday(&clock, nullptr);
    if (saveClock(clock.tv_sec)) settings.putBool("rtc-set", true);
    showKeyUntil = 0;
    redirectTab("overview");
  });
  post("/location", [] {
    Location place;
    if (!parseNumber(web.arg("lat"), -90, 90, place.lat) || !parseNumber(web.arg("lon"), -180, 180, place.lon)) {
      web.send(400, "text/plain", "Neplatna poloha."); return;
    }
    if (weatherRunning) { web.send(409, "text/plain", "Pockaj na nacitanie pocasia a skus znova."); return; }
    if (settings.putBytes("location", &place, sizeof(place)) != sizeof(place)) {
      web.send(500, "text/plain; charset=utf-8", "Polohu sa nepodarilo uložiť."); return;
    }
    location = place;
    backupSettings();
    forecastValid = false;
    nextWeather = millis();
    redirectTab("overview");
  });
  post("/wifi", [] {
    if (!requireSetupClient()) return;
    String name = web.arg("ssid"), key = web.arg("key");
    switch (saveProfile(name, key)) {
      case ProfileSave::Invalid: web.send(400, "text/plain; charset=utf-8", "Skontroluj názov siete a heslo."); return;
      case ProfileSave::Full: web.send(409, "text/plain; charset=utf-8", "Päť miest je obsadených. Najprv zabudni jednu sieť."); return;
      case ProfileSave::StorageError: web.send(500, "text/plain; charset=utf-8", "Sieť sa nepodarilo uložiť."); return;
      case ProfileSave::Saved: break;
    }
    if (!linked || web.arg("connect") == "1") {
      queueConnection(name);
      web.send(200, "text/html; charset=utf-8", "<meta charset=utf-8><p>Sieť uložená. Po pripojení otvor novú adresu z displeja.</p><p><a href='/?tab=wifi'>Späť</a></p>");
    } else redirectTab("wifi");
  });
  post("/wifi-forget", [] {
    if (!requireSetupClient()) return;
    double slot;
    if (!parseNumber(web.arg("slot"), 0, MAX_NETWORKS - 1, slot) || slot != floor(slot) || !networks.slots[int(slot)].ssid[0]) {
      web.send(400, "text/plain; charset=utf-8", "Sieť sa nenašla."); return;
    }
    const int index = int(slot);
    const bool restartLink = (linked && WiFi.SSID() == networks.slots[index].ssid) || (joining && joiningSlot == index);
    SavedNetworks updated = networks;
    updated.slots[index] = {};
    if (!saveNetworks(updated)) { web.send(500, "text/plain; charset=utf-8", "Sieť sa nepodarilo zabudnúť."); return; }
    if (restartLink) queueConnection("");
    redirectTab("wifi");
  });
  post("/wifi-default", [] {
    if (!requireSetupClient()) return;
    double slot;
    if (!parseNumber(web.arg("slot"), 0, MAX_NETWORKS - 1, slot) || slot != floor(slot) || !networks.slots[int(slot)].ssid[0]) {
      web.send(400, "text/plain; charset=utf-8", "Sieť sa nenašla."); return;
    }
    const String selected = networks.slots[int(slot)].ssid;
    if (!settings.putString("wifi-primary", selected)) {
      web.send(500, "text/plain; charset=utf-8", "Predvolenú sieť sa nepodarilo uložiť."); return;
    }
    preferredNetwork = selected;
    backupSettings();
    redirectTab("wifi");
  });
  post("/password", [] {
    if (!requireSetupClient()) return;
    const String key = web.arg("newkey");
    if (nextAccessKey.length()) { web.send(409, "text/plain; charset=utf-8", "Zmena už prebieha."); return; }
    if (!validAccessKey(key) || key != web.arg("repeat")) {
      web.send(400, "text/plain; charset=utf-8", "Použi 8 až 16 písmen alebo číslic. Obe heslá sa musia zhodovať."); return;
    }
    if (key == accessKey) { redirectTab("wifi"); return; }
    if (!settings.putString("desk-key", key)) { web.send(500, "text/plain; charset=utf-8", "Heslo sa nepodarilo uložiť."); return; }
    nextAccessKey = key;
    keyApplyAt = millis() + 850;
    web.sendHeader("Cache-Control", "no-store");
    web.send(200, "text/html; charset=utf-8", "<meta charset=utf-8><p>Heslo je uložené. Wi-Fi zariadenia sa obnoví. Pripoj sa a prihlás novým heslom.</p>");
  });
  web.begin();
}

String screenText(String text) {
  constexpr const char* accented[] = {"á","ä","č","ď","é","í","ĺ","ľ","ň","ó","ô","ŕ","š","ť","ú","ý","ž",
                                       "Á","Ä","Č","Ď","É","Í","Ĺ","Ľ","Ň","Ó","Ô","Ŕ","Š","Ť","Ú","Ý","Ž"};
  constexpr const char* ascii = "aacdeillnoorstuyzAACDEILLNOORSTUYZ";
  for (size_t i = 0; i < sizeof(accented) / sizeof(accented[0]); ++i) text.replace(accented[i], String(ascii[i]));
  String cleaned;
  for (unsigned i = 0; i < text.length(); ++i) {
    uint8_t byte = text[i];
    if (byte >= 32 && byte <= 126) cleaned += char(byte);
    else if ((byte & 0xc0) == 0xc0) cleaned += '?';
  }
  if (cleaned.length() > 59) cleaned = cleaned.substring(0, 56) + "...";
  return cleaned;
}

void drawCentered(int y, const String& text, int size = 2) {
  panel.setFont(nullptr);
  panel.setTextSize(size);
  const int width = text.length() * 6 * size;
  panel.setCursor(max(0, (200 - width) / 2), y);
  panel.print(text);
}

void drawClock(const String& clock) {
  if (!clockGlyphs.getBuffer()) { drawCentered(38, clock, 5); return; }
  clockGlyphs.fillScreen(0);
  clockGlyphs.setFont(&FreeSansBold24pt7b);
  clockGlyphs.setTextColor(1);
  clockGlyphs.setTextWrap(false);
  int16_t x, y;
  uint16_t glyphWidth, glyphHeight;
  clockGlyphs.getTextBounds(clock, 0, 0, &x, &y, &glyphWidth, &glyphHeight);
  if (!glyphWidth || !glyphHeight) return;
  clockGlyphs.setCursor(-x, -y);
  clockGlyphs.print(clock);
  constexpr int boxWidth = 188, boxHeight = 62;
  const int width = min(boxWidth, boxHeight * glyphWidth / glyphHeight);
  const int height = width * glyphHeight / glyphWidth;
  const int left = (200 - width) / 2, top = 28 + (boxHeight - height) / 2;
  for (int dy = 0; dy < height; ++dy) {
    const int row = dy * glyphHeight / height;
    int running = -1;
    for (int dx = 0; dx <= width; ++dx) {
      const bool ink = dx < width && clockGlyphs.getPixel(dx * glyphWidth / width, row);
      if (ink && running < 0) running = dx;
      if (!ink && running >= 0) {
        panel.drawFastHLine(left + running, top + dy, dx - running, GxEPD_BLACK);
        running = -1;
      }
    }
  }
}

String compactWeather() {
  if (!forecastValid) return "Vonku --";
  String line = "Vonku " + String(forecast.temperature, 0) + "C";
  if (forecast.rain >= 0) line += " " + String(forecast.rain) + "%";
  if (millis() - lastWeather >= 2 * WEATHER_INTERVAL && line.length() < 16) line += "*";
  return line;
}

void drawNote(const String& note, int top) {
  constexpr unsigned columns = 16;
  if (note.length() <= columns) { drawCentered(top, note); return; }
  int cut = note.lastIndexOf(' ', columns);
  if (cut < 8) cut = columns;
  String second = note.substring(cut);
  second.trim();
  if (second.length() > columns) second = second.substring(0, columns - 3) + "...";
  drawCentered(top, note.substring(0, cut));
  drawCentered(top + 19, second);
}

void drawFrame(uint32_t now) {
  String clock = timeText(time(nullptr), "%H:%M", "--:--");
  String date = timeText(time(nullptr), "%d.%m.%Y", "Cakam na cas");
  String weather = compactWeather();
  String desk = humidity < 0 ? "Vlhkost --" : "Vlhkost " + String(humidity) + "%";
  unsigned seconds = remainingSeconds();
  String timer = timerDone ? "CAS VYPRSAL" : "";
  if (seconds) timer = seconds < 60 ? "Casovac  < 1 min" : "Casovac  " + String((seconds + 59) / 60) + " min";
  bool showKey = showKeyUntil && !reached(now, showKeyUntil);
  const bool showSetup = ap && (!linked || showKey);
  String address = showSetup ? "192.168.4.1" : linked ? WiFi.localIP().toString() : "Wi-Fi pripajam";
  String pinned = screenText(note);
  String countdown = meetingCountdown();
  String appointment = countdown.length() ? timeText(meetingStart, "%H:%M") + " " + countdown : "";
  String title = countdown.length() ? screenText(meetingTitle) : "";
  if (title.length() > 16) title = title.substring(0, 13) + "...";
  String frame = clock + '|' + date + '|' + weather + '|' + desk + '|' + timer + '|' + address + '|' + pinned +
    '|' + appointment + '|' + title + (showKey ? "#" : "");
  if (frame == lastFrame) return;
  lastFrame = frame;
  if (fullRefresh || partialCount >= 24) {
    panel.setFullWindow();
    partialCount = 0;
    fullRefresh = false;
  } else {
    panel.setPartialWindow(0, 0, 200, 200);
    ++partialCount;
  }
  panel.firstPage();
  do {
    panel.fillScreen(GxEPD_WHITE);
    panel.setTextColor(GxEPD_BLACK);
    if (showKey) {
      drawCentered(12, showSetup ? "Nastavenie Wi-Fi" : "Webovy panel");
      if (showSetup) drawCentered(37, hostname, 3);
      drawCentered(70, address);
      drawCentered(103, "Meno: desk");
      drawCentered(132, "Heslo");
      drawCentered(155, accessKey);
    } else {
      drawCentered(7, date);
      drawClock(clock);
      drawCentered(98, weather);
      drawCentered(118, desk);
      if (appointment.length()) {
        drawCentered(142, appointment);
        drawCentered(161, title);
        String footer = timer.length() ? timer : pinned.length() ? pinned : showSetup ? hostname : address;
        if (footer.length() > 16) footer = footer.substring(0, 13) + "...";
        drawCentered(181, footer);
      } else {
        if (timer.length()) drawCentered(142, timer);
        if (pinned.length()) drawNote(pinned, timer.length() ? 161 : 142);
        if (!pinned.length() || !timer.length()) drawCentered(181, showSetup ? hostname : address);
      }
    }
  } while (panel.nextPage());
  Serial.printf("Display updated; local clock %s; humidity %d\n", clock.c_str(), humidity);
}

void updateButton(uint32_t now) {
  bool pressed = digitalRead(BOOT_BUTTON) == LOW;
  if (pressed && !buttonDown) buttonSince = now;
  if (!pressed && buttonDown && now - buttonSince > 30) {
    if (now - buttonSince >= 2500) {
      startAP();
      wifiImportUntil = now + 60000;
    }
    showKeyUntil = now + 35000;
  }
  buttonDown = pressed;
}

void acceptMeeting(const char* line) {
  constexpr char prefix[] = "@meeting ";
  if (strncmp(line, prefix, sizeof(prefix) - 1)) return;
  JsonDocument frame;
  if (deserializeJson(frame, line + sizeof(prefix) - 1) || !frame["start"].is<int64_t>()) return;
  const int64_t start = frame["start"].as<int64_t>();
  const char* title = frame["title"] | "";
  if (start && (start < MIN_TIME || start > 4102444799LL || strlen(title) > 180 || !title[0])) return;
  meetingStart = static_cast<time_t>(start);
  meetingTitle = start ? title : "";
  meetingDemo = frame["demo"] | false;
  meetingReceivedAt = millis();
  receivedMeeting = !meetingDemo || start != 0;
  Serial.println("@meeting-ok");
}

void acceptSerialFrame(const char* line) {
  const bool enabled = wifiImportUntil && !reached(millis(), wifiImportUntil);
  if (!strcmp(line, "@wifi-status")) {
    Serial.println(enabled ? "@wifi-ready" : "@wifi-locked");
    return;
  }
  constexpr char prefix[] = "@wifi ";
  if (strncmp(line, prefix, sizeof(prefix) - 1)) { acceptMeeting(line); return; }
  if (!enabled) { Serial.println("@wifi-locked"); return; }
  JsonDocument request;
  if (deserializeJson(request, line + sizeof(prefix) - 1) ||
      !request["ssid"].is<const char*>() || !request["key"].is<const char*>()) {
    Serial.println("@wifi-invalid");
    return;
  }
  const String name = request["ssid"].as<const char*>();
  switch (saveProfile(name, request["key"].as<const char*>())) {
    case ProfileSave::Invalid: Serial.println("@wifi-invalid"); return;
    case ProfileSave::Full: Serial.println("@wifi-full"); return;
    case ProfileSave::StorageError: Serial.println("@wifi-storage-error"); return;
    case ProfileSave::Saved: break;
  }
  wifiImportUntil = 0;
  if (!linked) queueConnection(name);
  Serial.println(cardState == WifiCardState::Ready ? "@wifi-saved-sd" : "@wifi-saved");
}

bool readSerial() {
  static char frame[512];
  static size_t used = 0;
  static bool overflow = false;
  bool status = false;
  while (Serial.available()) {
    const char c = static_cast<char>(Serial.read());
    if (c == '?' && !used && !overflow) { status = true; continue; }
    if (c == '\r') continue;
    if (c == '\n') {
      if (!overflow && used) {
        frame[used] = 0;
        acceptSerialFrame(frame);
      }
      memset(frame, 0, sizeof(frame));
      used = 0;
      overflow = false;
    } else if (used < sizeof(frame) - 1 && !overflow) frame[used++] = c;
    else overflow = true;
  }
  return status;
}
}

void setup() {
  Serial.begin(115200);
  pinMode(POWER_HOLD, OUTPUT); digitalWrite(POWER_HOLD, HIGH);
  pinMode(EPD_POWER, OUTPUT); digitalWrite(EPD_POWER, LOW);
  // The codec shares SDA/SCL with the sensor and RTC. Follow the board's
  // factory power-up order so the unpowered codec cannot load the I2C bus.
  pinMode(CODEC_POWER, OUTPUT); digitalWrite(CODEC_POWER, LOW);
  delay(50);
  pinMode(BOOT_BUTTON, INPUT_PULLUP);
  if (!Wire.begin(SDA_PIN, SCL_PIN, 400000)) Serial.println("I2C initialization failed");
  Wire.setTimeOut(40);
  settings.begin("desk", false);
  setenv("TZ", "UTC0", 1); tzset();
  if (settings.getBool("rtc-set", false)) restoreClock();
  setenv("TZ", TZ_RULE, 1); tzset();
  sntp_set_time_sync_notification_cb(recordSync);

  WiFi.persistent(false);
  WiFi.mode(WIFI_STA);  // Enable the hardware entropy source before creating an SD encryption key.
  restoreSettingsFromCard();
  loadNetworks();
  if (settings.getBytesLength("location") == sizeof(location)) settings.getBytes("location", &location, sizeof(location));
  note = settings.getString("note", "");
  accessKey = settings.getString("desk-key", "");
  if (!validAccessKey(accessKey)) {
    accessKey = randomHex().substring(0, 12);
    settings.putString("desk-key", accessKey);
  }
  backupSettings();
  csrf = randomHex();
  char name[14];
  snprintf(name, sizeof(name), "Desk-%04x", unsigned(ESP.getEfuseMac() >> 32) & 0xffff);
  hostname = name;
  WiFi.setAutoReconnect(false);
  nextNetworkTry = millis();
  if (nextNetwork() < 0) startAP();
  beginWeb();

  weatherJobs = xQueueCreate(1, sizeof(Location));
  weatherResults = xQueueCreate(1, sizeof(Forecast));
  if (!weatherJobs || !weatherResults || xTaskCreate(weatherWorker, "weather", 8192, nullptr, 1, nullptr) != pdPASS) {
    weatherResults = nullptr;
  }
  SPI.begin(12, -1, 13, 11);
  panel.init(115200, true, 10, false);
  panel.setRotation(0);
  panel.setTextWrap(false);
  humidity = readHumidity();
  sensorAt = millis() + 20000;
  Serial.println("Desk gadget 0.5.1 ready");
}

void loop() {
  uint32_t now = millis();
  web.handleClient();
  if (readSerial()) {
    auto probe = [](uint8_t address) {
      Wire.beginTransmission(address);
      return unsigned(Wire.endTransmission());
    };
    unsigned rtcAck = probe(RTC), shtAck = probe(SHT);
    int sda = gpio_get_level(static_cast<gpio_num_t>(SDA_PIN));
    int scl = gpio_get_level(static_cast<gpio_num_t>(SCL_PIN));
    unsigned count = 0;
    for (const WiFiProfile& entry : networks.slots) if (entry.ssid[0]) ++count;
    const char* outlook = !receivedMeeting ? "pending" : !meetingIsFresh() ? "stale" : !meetingStart ? "empty" : meetingDemo ? "demo" : "synced";
    Serial.printf("Desk gadget 0.5.1 | uptime %lu s | humidity %d (%s) | display %s | AP %s | LAN %s | networks %u | NTP %s | forecast %s | Outlook %s | SD %u | I2C SDA=%d SCL=%d RTC=%u SHT=%u\n",
      (unsigned long)(now / 1000), humidity, humidityStatus, lastFrame.isEmpty() ? "pending" : "updated",
      ap ? hostname.c_str() : "off", linked ? WiFi.localIP().toString().c_str() : "offline", count,
      lastSync ? "synced" : "pending", forecastValid ? "OK" : "pending", outlook, unsigned(cardState), sda, scl, rtcAck, shtAck);
  }
  updateNetwork(now);
  updateButton(now);
  if (timeWasSynced.exchange(false)) {
    lastSync = time(nullptr);
    bool saved = saveClock(lastSync);
    if (saved && !settings.getBool("rtc-set", false)) settings.putBool("rtc-set", true);
    Serial.printf("SNTP synchronised; RTC write %s\n", saved ? "OK" : "failed");
  }
  if (countdownDeadline && !remainingSeconds()) { countdownDeadline = 0; timerDone = true; }
  if (reached(now, sensorAt)) { humidity = readHumidity(); sensorAt = now + 20000; }
  updateWeather(now);
  drawFrame(now);
  delay(90);
}
