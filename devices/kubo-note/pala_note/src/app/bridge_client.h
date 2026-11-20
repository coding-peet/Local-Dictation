#pragma once
#include "provider.h"
#include "backlog_transport.h"
#include <WiFiClientSecure.h>

#if PALA_BRIDGE_DISCOVERY
class ProviderClient final : public WiFiClientSecure {
public:
  using WiFiClientSecure::connect;
  int connect(const char* host, uint16_t port) override;
  int connect(const char* host, uint16_t port, int32_t timeout) override;
};
void resetBridgeAddressCache();
WifiBacklog pairedWifiBacklog();
#else
using ProviderClient = WiFiClientSecure;
#endif
