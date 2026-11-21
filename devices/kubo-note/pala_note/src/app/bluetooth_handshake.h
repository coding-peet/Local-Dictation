#pragma once

#include <array>
#include <cstddef>
#include <cstdint>
#include <string_view>

namespace pala_ble {
constexpr uint8_t kProtocolVersion = 0x02;
constexpr uint8_t kBridgeHelloOpcode = 0x01;
constexpr uint8_t kPalaReplyOpcode = 0x02;
constexpr size_t kNonceSize = 16;
constexpr size_t kProofSize = 32;
constexpr size_t kSessionKeySize = 32;
constexpr size_t kCapabilitySize = 1 + kNonceSize;
constexpr size_t kBridgeHelloSize = 1 + kNonceSize + kProofSize;
constexpr size_t kPalaReplySize = 1 + kProofSize;
constexpr std::string_view kKdfInfo = "KUBO_BLE_V2 keys";
using Capability = std::array<uint8_t, kCapabilitySize>;

constexpr bool authorizesNativeEnrollment(bool confirmed, bool authenticated, bool secureConnections, bool mitm) {
  return confirmed && authenticated && secureConnections && mitm;
}

constexpr bool acceptsBridgeHello(const Capability& capability, const uint8_t* hello, size_t length) {
  return capability[0] == kProtocolVersion && hello && length == kBridgeHelloSize &&
         hello[0] == kBridgeHelloOpcode;
}

constexpr bool validFrameLength(size_t length, bool authenticated, size_t maxPacket, size_t tagSize) {
  return authenticated ? length > tagSize && length <= maxPacket : length == kBridgeHelloSize;
}

constexpr auto handshakeSalt(const Capability& capability, const uint8_t* bridgeNonce) {
  std::array<uint8_t, 2 * kNonceSize> salt{};
  for (size_t i = 0; i < kNonceSize; ++i) {
    salt[i] = capability[1 + i];
    salt[kNonceSize + i] = bridgeNonce[i];
  }
  return salt;
}

template<size_t RoleLength>
constexpr auto proofMessage(const char (&role)[RoleLength], const Capability& capability,
                            const uint8_t* bridgeNonce) {
  constexpr size_t roleBytes = RoleLength - 1;
  std::array<uint8_t, roleBytes + kCapabilitySize + kNonceSize> message{};
  for (size_t i = 0; i < roleBytes; ++i) message[i] = role[i];
  for (size_t i = 0; i < capability.size(); ++i) message[roleBytes + i] = capability[i];
  for (size_t i = 0; i < kNonceSize; ++i) message[roleBytes + capability.size() + i] = bridgeNonce[i];
  return message;
}
}  // namespace pala_ble
