#pragma once
#include "provider.h"
#include "backlog_transport.h"
#include <WiFiClientSecure.h>

#if PALA_BRIDGE_DISCOVERY
#include "note_peers.h"

class ProviderClient final : public WiFiClientSecure {
public:
  ProviderClient();
  explicit ProviderClient(const NotePeer& pairedPeer);
  using WiFiClientSecure::connect;
  int connect(const char* host, uint16_t port) override;
  int connect(const char* host, uint16_t port, int32_t timeout) override;
  const NotePeer& bridge() const { return peer; }
  String url(const char* path) const;

private:
  NotePeer peer;
};

NotePeer selectedWifiBridge();
bool wifiBridgeTranscriptionOnly();
void resetBridgeAddressCache();
void startCompanionMdns();
void stopNoteMdns();
String companionLocalAddress();
WifiBacklog pairedWifiBacklog(bool homeOnly = false);
#else
using ProviderClient = WiFiClientSecure;
#endif
