#include "bridge_client.h"
#include "wifi_trial.h"

#if PALA_BRIDGE_DISCOVERY
#include <ArduinoJson.h>
#include <ESPmDNS.h>
#include <HTTPClient.h>
#include <WiFi.h>
#include "bridge_address.h"
#include "companion_host.h"
#include "network.h"
#include "usb_sync.h"
#include "home_bridge_status.h"
#include "settings.h"

namespace {
IPAddress cachedAddress;
uint16_t cachedPort = 0;
IPAddress cachedLocal;
IPAddress cachedGateway;
String cachedSsid;
String cachedHost;
uint32_t cachedAt = 0;
uint32_t cacheDuration = 0;
bool mdnsStarted = false;
bool httpAdvertised = false;

String selectedId;
IPAddress selectedLocal;
IPAddress selectedGateway;
String selectedSsid;
uint32_t selectedAt = 0;
WifiBacklog selectedStatus = WifiBacklog::Unavailable;
bool selectedTranscribeOnly = false;

bool onWifiLink(const IPAddress& address) {
  const IPAddress local = WiFi.localIP();
  const IPAddress mask = WiFi.subnetMask();
  return privateBridgeAddress(address[0], address[1]) &&
         mask != IPAddress() && local != address &&
         (uint32_t(address) & uint32_t(mask)) == (uint32_t(local) & uint32_t(mask));
}

bool configured(const NotePeer& peer) {
  return !peer.id.isEmpty() && !peer.host.isEmpty() && !peer.cert.isEmpty() &&
         !peer.token.isEmpty() && peer.port != 0;
}

IPAddress lookupPairedBridge(const String& host) {
  if (!host.endsWith(".local")) {
    uint8_t literal[4];
    return parseBridgeAddress(host.c_str(), literal) ? IPAddress(literal) : IPAddress();
  }
  if (!mdnsStarted) {
    char localName[19];
    snprintf(localName, sizeof(localName), "palanote-%08lx",
             static_cast<unsigned long>(ESP.getEfuseMac() & 0xffffffffULL));
    mdnsStarted = MDNS.begin(localName);
  }
  return mdnsStarted ? MDNS.queryHost(host.substring(0, host.length() - 6).c_str(), 1500) : IPAddress();
}

WifiBacklog probe(const NotePeer& peer, bool& transcribeOnly, bool homeOnly) {
  if (!configured(peer)) return WifiBacklog::Unavailable;
  ProviderClient client(peer);
  client.setHandshakeTimeout(2);
  HTTPClient http;
  if (!http.begin(client, client.url("/pala/ready"))) return WifiBacklog::Unavailable;
  http.setConnectTimeout(1500);
  http.setTimeout(2000);
  http.addHeader("Authorization", "Bearer " + peer.token);
  if (homeOnly) http.addHeader("X-Pala-Home", "1");
  else http.addHeader("X-Pala-Receipts", "1");
  const int status = http.GET();
#if PALA_WIFI_TRIAL
  if (wifi_trial::trace().sampling) wifi_trial::trace().httpStatus = static_cast<uint32_t>(status);
#endif
  int receipt = 0;
  const int length = http.getSize();
  WifiBacklog readiness = WifiBacklog::Unavailable;
  if ((status == HTTP_CODE_OK || status == HTTP_CODE_SERVICE_UNAVAILABLE ||
       (homeOnly && status == HTTP_CODE_BAD_GATEWAY)) && length > 0 && length <= 96) {
    const String payload = http.getString();
    JsonDocument document;
    if (payload.length() == static_cast<size_t>(length) && !deserializeJson(document, payload)) {
      const JsonVariantConst ready = document["ready"];
      readiness = homeOnly && ready.is<bool>()
          ? WifiBacklog::Ready : classifyWifiReadiness(status, ready.is<bool>(), ready.as<bool>());
      receipt = document["receipt"] | 0;
      transcribeOnly = readiness != WifiBacklog::Unavailable && document["processing"] == "transcribe";
    }
  }
  http.end();
  if (receipt > 0 && recordingSyncAllowed()) confirmWifiTranscriptReceipt(receipt, peer.id);
  if (readiness != WifiBacklog::Unavailable) homeBridgeStatus.verified(HomeBridgeLink::Wifi);
  return readiness;
}

void remember(const String& id, WifiBacklog status) {
  selectedId = id;
  selectedStatus = status;
  selectedLocal = WiFi.localIP();
  selectedGateway = WiFi.gatewayIP();
  selectedSsid = WiFi.SSID();
  selectedAt = millis();
}
}

WifiBacklog pairedWifiBacklog(bool homeOnly) {
  if (WiFi.status() != WL_CONNECTED) return WifiBacklog::Unavailable;
  const String previous = noteLastBridge();
  const int count = notePeerCount();
  int previousIndex = -1;
  for (int index = 0; index < count; ++index) {
    if (notePeerAt(index).id == previous) {
      previousIndex = index;
      break;
    }
  }
  bool transcribeOnly[kNoteMaxPeers] = {};
  const auto selected = selectWifiBacklog(count, previousIndex,
      [&](int index) { return probe(notePeerAt(index), transcribeOnly[index], homeOnly); });
  selectedTranscribeOnly = selected.index >= 0 && transcribeOnly[selected.index];
  const String id = selected.index >= 0 ? notePeerAt(selected.index).id : String();
  remember(id, selected.status);
  if (!homeOnly && selected.status == WifiBacklog::Ready && id != previous) noteSetLastBridge(id);
  return selected.status;
}

NotePeer selectedWifiBridge() {
  if (WiFi.status() != WL_CONNECTED) return {};
  const bool sameLink = selectedLocal == WiFi.localIP() && selectedGateway == WiFi.gatewayIP() &&
                        selectedSsid == WiFi.SSID();
  const auto peer = notePeerById(selectedId);
  if (sameLink && millis() - selectedAt < 15000 &&
      (configured(peer) || selectedStatus == WifiBacklog::Unavailable)) return peer;
  if (pairedWifiBacklog() == WifiBacklog::Unavailable) return {};
  return notePeerById(selectedId);
}

bool wifiBridgeTranscriptionOnly() {
  return configured(selectedWifiBridge()) && selectedTranscribeOnly;
}

void stopNoteMdns() {
  if (mdnsStarted) MDNS.end();
  mdnsStarted = false;
  httpAdvertised = false;
}

void startCompanionMdns() {
  // A background Bridge lookup can already own the single mDNS responder.
  stopNoteMdns();
  const auto name = companion_host::label(palaNoteName().c_str());
  mdnsStarted = MDNS.begin(name.c_str());
  if (mdnsStarted) httpAdvertised = MDNS.addService("http", "tcp", 80);
  else MDNS.end();
}

String companionLocalAddress() {
  char name[MDNS_NAME_BUF_LEN]{};
  if (!httpAdvertised || mdns_hostname_get(name) != ESP_OK || !name[0]) return {};
  return String(name) + ".local";
}

void resetBridgeAddressCache() {
  cacheDuration = 0;
  cachedAddress = IPAddress();
  selectedId = "";
  selectedAt = 0;
  selectedSsid = "";
  selectedTranscribeOnly = false;
  selectedStatus = WifiBacklog::Unavailable;
}

ProviderClient::ProviderClient() : peer(selectedWifiBridge()) {}

ProviderClient::ProviderClient(const NotePeer& pairedPeer) : peer(pairedPeer) {}

String ProviderClient::url(const char* path) const {
  if (!configured(peer)) return "";
  return "https://" + peer.host + ":" + String(peer.port) + path;
}

int ProviderClient::connect(const char* host, uint16_t port) {
  return connect(host, port, _timeout);
}

int ProviderClient::connect(const char* host, uint16_t port, int32_t timeout) {
  if (!host || !configured(peer) || peer.host != host || port != peer.port ||
      WiFi.status() != WL_CONNECTED) return 0;

  const IPAddress local = WiFi.localIP();
  const IPAddress gateway = WiFi.gatewayIP();
  const String ssid = WiFi.SSID();
  const auto tryAddress = [&](const IPAddress& address, uint16_t destinationPort, uint32_t duration) {
    if (!onWifiLink(address)) {
#if PALA_WIFI_TRIAL
      wifi_trial::disallowed();
#endif
      return false;
    }
    // The address comes from local discovery, while TLS verifies the stored
    // hostname and certificate for this particular paired Bridge.
    _timeout = timeout > 0 && timeout < 3500 ? timeout : 3500;
    const bool verified = WiFiClientSecure::connect(address, destinationPort, peer.host.c_str(),
                                                    peer.cert.c_str(), nullptr, nullptr);
    _timeout = timeout > 0 ? timeout : 30000;
#if PALA_WIFI_TRIAL
    if (wifi_trial::trace().sampling) {
      auto& trace = wifi_trial::trace();
      trace.socketIp = static_cast<uint32_t>(address);
      trace.socketPort = destinationPort;
      if (verified) trace.tcpReached = 1;
      else if (trace.phase == 4) {
        // Distinguish a reachable HTTPS socket from a failure to authenticate its certificate.
        WiFiClient socket;
        trace.tcpReached = socket.connect(address, destinationPort, 1500) ? 1U : 0U;
        socket.stop();
      }
      char detail[48]{};
      wifi_trial::tls(verified, verified ? 0 : lastError(detail, sizeof(detail)));
    }
#endif
    if (!verified) return false;
    cachedLocal = local;
    cachedGateway = gateway;
    cachedSsid = ssid;
    cachedHost = peer.host;
    cachedAddress = address;
    cachedPort = destinationPort;
    cachedAt = millis();
    cacheDuration = duration;
    return true;
  };

  IPAddress tried;
  uint16_t triedPort = 0;
  if (cachedHost == peer.host && cachedLocal == local && cachedGateway == gateway &&
      cachedSsid == ssid && millis() - cachedAt < cacheDuration) {
    tried = cachedAddress;
    triedPort = cachedPort;
    const auto started = cachedAt;
    if (tryAddress(tried, triedPort, cacheDuration)) {
      cachedAt = started;
      return 1;
    }
  }

  const IPAddress discovered = lookupPairedBridge(peer.host);
#if PALA_WIFI_TRIAL
  wifi_trial::resolved(static_cast<uint32_t>(discovered));
#endif
  if (discovered != IPAddress() && (discovered != tried || port != triedPort) &&
      tryAddress(discovered, port, 30000)) return 1;

  uint8_t manual[4];
  uint16_t manualPort = 0;
  if (!parseBridgeEndpoint(palaBridgeAddress().c_str(), manual, manualPort)) return 0;
  // A global fallback is unambiguous for the legacy Bridge or the only Wi-Fi peer.
  if (peer.id != "legacy") {
    for (int i = 0; i < notePeerCount(); ++i) {
      const NotePeer other = notePeerAt(i);
      if (other.id != peer.id && configured(other)) return 0;
    }
  }
  const IPAddress address(manual);
  if (!manualPort) manualPort = port;
  if ((address == discovered && manualPort == port) ||
      (address == tried && manualPort == triedPort)) return 0;
  return tryAddress(address, manualPort, 8000) ? 1 : 0;
}
#endif
