#include "home_delivery.h"
#include "home_delivery_refresh.h"
#include "home_bridge_status.h"
#include "bridge_client.h"
#include "home_almanac.h"
#include "settings.h"

#if PALA_BRIDGE_DISCOVERY
#include <ArduinoJson.h>
#include <HTTPClient.h>
#endif

namespace {
HomeDeliveryRefresh refreshSchedule;
uint32_t manualGeneration = 0;
uint32_t manualPending = 0;

bool parseCounts(const String& payload, char (&day)[11], unsigned& delivered, unsigned& waiting) {
  int consumed = 0;
  return payload.length() <= 30 &&
         sscanf(payload.c_str(), "%10[0-9-],%9u,%9u%n", day, &delivered, &waiting, &consumed) == 3 &&
         static_cast<size_t>(consumed) == payload.length() && validForecastDate(day) &&
         delivered <= 999999999 && waiting <= 999999999;
}
}

void startManualHomeRefresh() {
  if (++manualGeneration == 0) ++manualGeneration;
  manualPending = manualGeneration;
}

uint32_t manualHomeRefreshRequest() { return manualPending; }

bool completeManualHomeRefresh(uint32_t request) {
  if (!request || request != manualPending) return false;
  manualPending = 0;
  return true;
}

HomeDeliveryCounts homeDeliveryCounts() {
  HomeDeliveryCounts counts;
  const String stored = palaStoredDeliveryCounts();
  if (parseHomeDashboard({stored.c_str(), stored.length()}, counts)) return counts;
  char day[11]{};
  unsigned delivered = 0, waiting = 0;
  if (parseCounts(stored, day, delivered, waiting)) counts.waiting = waiting;
  return counts;
}

void restoreHomeDeliveryRefresh() {
  const unsigned waiting = homeDeliveryCounts().waiting;
  if (waiting > 0) refreshSchedule.received(millis(), waiting);
}

bool storeHomeDelivery(const String& payload, bool& changed) {
  char day[11]{};
  unsigned delivered = 0, waiting = 0;
  HomeDeliveryCounts dashboard;
  const bool dashboardFormat = parseHomeDashboard({payload.c_str(), payload.length()}, dashboard);
  if (dashboardFormat) waiting = dashboard.waiting;
  else if (!parseCounts(payload, day, delivered, waiting)) return false;
  const String stored = palaStoredDeliveryCounts();
  changed = payload != stored;
  HomeDeliveryCounts previous;
  if (changed && dashboardFormat && parseHomeDashboard({stored.c_str(), stored.length()}, previous)) {
    const auto now = time(nullptr);
    const bool weekly = palaPrefersWeeklyCodexLimit();
    changed = dashboard.generalOpen != previous.generalOpen || dashboard.waiting != previous.waiting ||
              strcmp(dashboard.lastDelivery, previous.lastDelivery) != 0 ||
              dashboard.codexAt(now, weekly) != previous.codexAt(now, weekly);
  }
  if (!palaStoreDeliveryCounts(payload)) return false;
  refreshSchedule.received(millis(), waiting);
  return true;
}

void homeDeliveryRefreshAttempted(uint32_t now) {
  refreshSchedule.attempted(now);
}

bool homeDeliveryNeedsRefresh(uint32_t now) {
  return refreshSchedule.needsRoutineRefresh(now);
}

bool homeDeliveryNeedsFollowup(uint32_t now) {
  return refreshSchedule.needsFollowup(now);
}

bool homeDeliveryNeedsBluetoothFollowup(uint32_t now) {
  return refreshSchedule.needsBluetoothFollowup(now);
}

void homeDeliveryBluetoothAttempted(uint32_t now) {
  refreshSchedule.bluetoothAttempted(now);
}

bool homeDeliveryKeepingBluetoothAwake(uint32_t now) {
  return refreshSchedule.keepAwakeForBluetooth(now);
}

void refreshHomeDeliveryAfterSubmission() {
  // The Bridge stages the Google write before replying. Confirmation can follow later.
  refreshSchedule.submitted(millis());
  refreshHomeDelivery();
}

bool refreshHomeDelivery(bool manual) {
#if PALA_BRIDGE_DISCOVERY
  ProviderClient client;
  client.setHandshakeTimeout(2);
  String payload;
  const auto get = [&](const char* path) {
    HTTPClient http;
    http.setConnectTimeout(1500);
    http.setTimeout(manual ? 5500 : 2000);
    const String url = client.url(path);
    if (url.isEmpty() || !http.begin(client, url)) return -1;
    http.addHeader("Authorization", "Bearer " + client.bridge().token);
    if (manual) http.addHeader("X-Pala-Home", "1");
    const int status = http.GET();
    const int length = http.getSize();
    if (status == HTTP_CODE_OK && length > 0 && length <= 96) {
      payload = http.getString();
      if (payload.length() != static_cast<size_t>(length)) payload = "";
    }
    http.end();
    return status;
  };
  int status = get("/pala/home?v=4");
  if (status == 403 || status == 404) status = get("/pala/home?v=3");
  if (status == 403 || status == 404) status = get("/pala/home");
  if (status == 403 || status == 404) get("/pala/delivery");
  if (!payload.length()) return false;
  JsonDocument document;
  if (deserializeJson(document, payload) || !document["counts"].is<const char*>()) return false;
  bool changed = false;
  if (!storeHomeDelivery(document["counts"].as<String>(), changed)) return false;
  homeBridgeStatus.verified(HomeBridgeLink::Wifi);
  return changed;
#else
  return false;
#endif
}
