#pragma once
#include <stdint.h>

constexpr bool privateBridgeAddress(uint8_t first, uint8_t second) {
  return first == 10 || (first == 172 && second >= 16 && second <= 31) ||
         (first == 192 && second == 168);
}

constexpr bool parseBridgeEndpoint(const char* value, uint8_t (&octets)[4], uint16_t& port) {
  for (int i = 0; i < 4; ++i) {
    unsigned number = 0;
    unsigned digits = 0;
    if (value[0] == '0' && value[1] >= '0' && value[1] <= '9') return false;
    while (*value >= '0' && *value <= '9') {
      if (++digits > 3) return false;
      number = number * 10 + (*value++ - '0');
    }
    if (!digits || number > 255 || (i != 3 && *value != '.')) return false;
    octets[i] = static_cast<uint8_t>(number);
    if (i != 3) ++value;
  }
  if (!privateBridgeAddress(octets[0], octets[1])) return false;
  port = 0;
  if (!*value) return true;
  if (*value++ != ':' || *value < '1' || *value > '9') return false;
  unsigned number = 0;
  while (*value >= '0' && *value <= '9') {
    number = number * 10 + (*value++ - '0');
    if (number > UINT16_MAX) return false;
  }
  if (*value) return false;
  port = static_cast<uint16_t>(number);
  return true;
}

constexpr bool parseBridgeAddress(const char* value, uint8_t (&octets)[4]) {
  uint16_t port = 0;
  return parseBridgeEndpoint(value, octets, port) && !port;
}
