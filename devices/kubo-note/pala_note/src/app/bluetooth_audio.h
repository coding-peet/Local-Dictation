#pragma once

#include <cstddef>
#include <cstdint>

namespace pala_ble {
constexpr uint8_t kAudioRequest = 0x03;
constexpr uint8_t kAudioFragment = 0x02;
constexpr size_t kAudioOffsetBytes = 4;
constexpr size_t kAudioBlockBytes = 4096;

struct AudioRequest {
  uint32_t offset;
  uint16_t length;
};

constexpr AudioRequest parseAudioRequest(const uint8_t* data, size_t size) {
  if (!data || size != 7 || data[0] != kAudioRequest) return {};
  const auto length = static_cast<uint16_t>((data[5] << 8) | data[6]);
  if (length > kAudioBlockBytes) return {};
  const uint32_t offset = (static_cast<uint32_t>(data[1]) << 24) |
                          (static_cast<uint32_t>(data[2]) << 16) | (data[3] << 8) | data[4];
  return {offset, length};
}
}  // namespace pala_ble
