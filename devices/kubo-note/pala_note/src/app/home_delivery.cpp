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

bool parseCounts(const String& payload, char (&day)[11], unsigned& delivered, unsigned& waiting) {
  int consumed = 0;
  return payload.length() <= 30 &&
         sscanf(payload.c_str(), "%10[0-9-],%9u,%9u%n", day, &delivered, &waiting, &consumed) == 3 &&
         static_cast<size_t>(consumed) == payload.length() && validForecastDate(day) &&
         delivered <= 999999999 && waiting <= 999999999;
}
}

HomeDeliveryCounts homeDeliveryCounts(const String& today) {
  char day[11]{};
  unsigned delivered = 0, waiting = 0;
  if (!parseCounts(palaStoredDeliveryCounts(), day, delivered, waiting)) return {};
  return {today.isEmpty() ? -1 : today == day ? static_cast<int>(delivered) : 0,
          static_cast<int>(waiting)};
}

bool storeHomeDelivery(const String& payload, bool& changed) {
  char day[11]{};
  unsigned delivered = 0, waiting = 0;
  if (!parseCounts(payload, day, delivered, waiting)) return false;
  changed = payload != palaStoredDeliveryCounts();
  if (!palaStoreDeliveryCounts(payload)) return false;
  refreshSchedule.received(millis(), waiting);
  return true;
}

bool homeDeliveryNeedsRefresh(uint32_t now) {
  return refreshSchedule.needsRoutineRefresh(now);
}

bool homeDeliveryNeedsFollowup(uint32_t now) {
  return refreshSchedule.needsFollowup(now);
}

void refreshHomeDeliveryAfterSubmission() {
  // The Bridge stages the Google write before replying. Confirmation can follow later.
  refreshSchedule.submitted(millis());
  refreshHomeDelivery();
}

bool refreshHomeDelivery() {
#if PALA_BRIDGE_DISCOVERY
  ProviderClient client;
  client.setHandshakeTimeout(2);
  HTTPClient http;
  http.setConnectTimeout(1500);
  http.setTimeout(2000);
  const String url = client.url("/pala/delivery");
  if (url.isEmpty() || !http.begin(client, url)) return false;
  http.addHeader("Authorization", "Bearer " + client.bridge().token);
  const int status = http.GET();
  const int length = http.getSize();
  const String payload = status == HTTP_CODE_OK && length > 0 && length <= 64 ? http.getString() : String();
  http.end();
  if (!payload.length() || payload.length() != static_cast<size_t>(length)) return false;
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
