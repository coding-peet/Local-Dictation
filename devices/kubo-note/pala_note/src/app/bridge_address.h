#pragma once
#include <stdint.h>

constexpr bool privateBridgeAddress(uint8_t first, uint8_t second) {
  return first == 10 || (first == 172 && second >= 16 && second <= 31) ||
         (first == 192 && second == 168);
}

constexpr bool parseBridgeAddress(const char* value, uint8_t (&octets)[4]) {
  for (int i = 0; i < 4; ++i) {
    unsigned number = 0;
    unsigned digits = 0;
    if (value[0] == '0' && value[1] >= '0' && value[1] <= '9') return false;
    while (*value >= '0' && *value <= '9') {
      if (++digits > 3) return false;
      number = number * 10 + (*value++ - '0');
    }
    if (!digits || number > 255 || *value != (i == 3 ? '\0' : '.')) return false;
    octets[i] = static_cast<uint8_t>(number);
    if (i != 3) ++value;
  }
  return privateBridgeAddress(octets[0], octets[1]);
}
