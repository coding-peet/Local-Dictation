#include "bridge_address.h"

constexpr bool acceptedPrivateAddresses() {
  uint8_t octets[4] = {};
  return parseBridgeAddress("10.59.167.226", octets) &&
         octets[0] == 10 && octets[1] == 59 && octets[2] == 167 && octets[3] == 226 &&
         parseBridgeAddress("192.168.1.31", octets) &&
         parseBridgeAddress("172.31.0.2", octets);
}

constexpr bool rejectsInvalidAddresses() {
  uint8_t octets[4] = {};
  const char* invalid[] = {
    "", "http://192.168.1.31", "192.168.1.31:8443", "192.168.1.999",
    "192.168.1", "192.168.1.31.extra", "192.168.01.31", "192.168..31",
    "8.8.8.8", "127.0.0.1", "172.32.0.2", "224.0.0.251"
  };
  for (auto value : invalid) {
    if (parseBridgeAddress(value, octets)) return false;
  }
  return true;
}

static_assert(acceptedPrivateAddresses(), "The Web Companion must accept private IPv4 addresses.");
static_assert(rejectsInvalidAddresses(), "The Web Companion must reject invalid or public addresses.");
