#include "bridge_client.h"

#if PALA_BRIDGE_DISCOVERY
#include <ESPmDNS.h>
#include <HTTPClient.h>
#include <WiFi.h>
#include "bridge_address.h"
#include "settings.h"

namespace {
IPAddress cachedAddress;
IPAddress cachedLocal;
IPAddress cachedGateway;
String cachedSsid;
uint32_t cachedAt = 0;
uint32_t cacheDuration = 0;
bool mdnsStarted = false;

bool onWifiLink(const IPAddress& address) {
  const IPAddress local = WiFi.localIP();
  const IPAddress mask = WiFi.subnetMask();
  return privateBridgeAddress(address[0], address[1]) &&
         mask != IPAddress() && local != address &&
         (uint32_t(address) & uint32_t(mask)) == (uint32_t(local) & uint32_t(mask));
}

IPAddress lookupPairedBridge() {
  if (!mdnsStarted) {
    char localName[19];
    snprintf(localName, sizeof(localName), "palanote-%08lx",
             static_cast<unsigned long>(ESP.getEfuseMac() & 0xffffffffULL));
    mdnsStarted = MDNS.begin(localName);
  }
  return mdnsStarted ? MDNS.queryHost(PALA_BRIDGE_ID, 1500) : IPAddress();
}
}

WifiBacklog pairedWifiBacklog() {
  ProviderClient client;
  client.setCACert(PALA_BRIDGE_CERT);
  client.setHandshakeTimeout(2);
  HTTPClient http;
  if (!http.begin(client, "https://" PALA_AUTHORITY "/pala/ready")) return WifiBacklog::Unavailable;
  http.setConnectTimeout(1500);
  http.setTimeout(3000);
  http.addHeader("Authorization", "Bearer " PALA_BRIDGE_TOKEN);
  const int status = http.GET();
  http.end();
  if (status == 200) return WifiBacklog::Ready;
  if (status == 503) return WifiBacklog::Busy;
  return WifiBacklog::Unavailable;
}

void resetBridgeAddressCache() {
  cacheDuration = 0;
  cachedAddress = IPAddress();
}

int ProviderClient::connect(const char* host, uint16_t port) {
  return connect(host, port, _timeout);
}

int ProviderClient::connect(const char* host, uint16_t port, int32_t timeout) {
  if (!host || strcmp(host, PALA_HOST) != 0 || port != PALA_PORT ||
      WiFi.status() != WL_CONNECTED) return 0;

  const IPAddress local = WiFi.localIP();
  const IPAddress gateway = WiFi.gatewayIP();
  const String ssid = WiFi.SSID();
  const auto tryAddress = [&](const IPAddress& address, uint32_t duration) {
    if (!onWifiLink(address)) return false;
    // Dial the resolved IP, but always authenticate the name stored at pairing.
    _timeout = timeout > 0 && timeout < 3500 ? timeout : 3500;
    const bool verified = WiFiClientSecure::connect(address, port, PALA_HOST,
                                                    PALA_BRIDGE_CERT, nullptr, nullptr);
    _timeout = timeout > 0 ? timeout : 30000;
    if (!verified) return false;
    cachedLocal = local;
    cachedGateway = gateway;
    cachedSsid = ssid;
    cachedAddress = address;
    cachedAt = millis();
    cacheDuration = duration;
    return true;
  };

  IPAddress tried;
  if (cachedLocal == local && cachedGateway == gateway && cachedSsid == ssid &&
      millis() - cachedAt < cacheDuration) {
    tried = cachedAddress;
    const auto started = cachedAt;
    if (tryAddress(tried, cacheDuration)) {
      cachedAt = started;
      return 1;
    }
  }
  resetBridgeAddressCache();
  const IPAddress discovered = lookupPairedBridge();
  if (discovered != tried && tryAddress(discovered, 30000)) return 1;

  uint8_t manual[4];
  if (parseBridgeAddress(palaBridgeAddress().c_str(), manual)) {
    const IPAddress address(manual);
    if (address != discovered && address != tried && tryAddress(address, 8000)) return 1;
  }
  return 0;
}
#endif
