#include "../pala_note/src/app/bluetooth_handshake.h"
#include "../pala_note/src/app/bluetooth_audio.h"

namespace {
using namespace pala_ble;

constexpr uint8_t hexDigit(char value) {
  return value <= '9' ? value - '0' : value - 'a' + 10;
}

template<size_t N>
constexpr auto bytes(const char (&hex)[N]) {
  static_assert(N % 2 == 1);
  std::array<uint8_t, (N - 1) / 2> value{};
  for (size_t i = 0; i < value.size(); ++i) {
    value[i] = (hexDigit(hex[2 * i]) << 4) | hexDigit(hex[2 * i + 1]);
  }
  return value;
}

template<size_t N>
constexpr bool same(const std::array<uint8_t, N>& first, const std::array<uint8_t, N>& second) {
  for (size_t i = 0; i < N; ++i) {
    if (first[i] != second[i]) return false;
  }
  return true;
}

constexpr auto capability = bytes("02000102030405060708090a0b0c0d0e0f");
constexpr auto hello = bytes("01101112131415161718191a1b1c1d1e1f5c37d01f1f4e683c36a54fcc7c5d4f6f4edbf9da3111be51511becbb686696aa");
constexpr auto reply = bytes("020cfafff9f9de6fc27f703ec95e4749a2406f66bc58e26fcb99efba86333f675b");

constexpr auto unsupportedCapability = [] {
  auto value = capability;
  value[0] = 1;
  return value;
}();

constexpr auto wrongOpcode = [] {
  auto value = hello;
  value[0] = 2;
  return value;
}();

static_assert(authorizesNativeEnrollment(true, true, true, true));
static_assert(!authorizesNativeEnrollment(false, true, true, true)); // A resumed bond is not fresh approval.
static_assert(!authorizesNativeEnrollment(true, false, true, true));
static_assert(!authorizesNativeEnrollment(true, true, false, true));
static_assert(!authorizesNativeEnrollment(true, true, true, false)); // Just Works cannot enroll a peer.

static_assert(capability.size() == kCapabilitySize && hello.size() == kBridgeHelloSize);
static_assert(reply.size() == kPalaReplySize && reply[0] == kPalaReplyOpcode);
static_assert(acceptsBridgeHello(capability, hello.data(), hello.size()));
static_assert(!acceptsBridgeHello(capability, hello.data(), hello.size() - 1));
static_assert(!acceptsBridgeHello(capability, hello.data(), hello.size() + 1));
static_assert(!acceptsBridgeHello(capability, wrongOpcode.data(), wrongOpcode.size()));
static_assert(!acceptsBridgeHello(unsupportedCapability, hello.data(), hello.size()));
static_assert(!acceptsBridgeHello(capability, nullptr, 0));

static_assert(validFrameLength(49, false, 512, 16));
static_assert(!validFrameLength(48, false, 512, 16));
static_assert(!validFrameLength(50, false, 512, 16));
static_assert(!validFrameLength(16, true, 512, 16));
static_assert(validFrameLength(17, true, 512, 16));
static_assert(validFrameLength(512, true, 512, 16));
static_assert(!validFrameLength(513, true, 512, 16));

constexpr auto audioRead = bytes("03001122331000");
constexpr auto oversizedAudioRead = bytes("03001122331001");
static_assert(parseAudioRequest(audioRead.data(), audioRead.size()).offset == 0x00112233);
static_assert(parseAudioRequest(audioRead.data(), audioRead.size()).length == 4096);
static_assert(!parseAudioRequest(oversizedAudioRead.data(), oversizedAudioRead.size()).length);
static_assert(!parseAudioRequest(audioRead.data(), audioRead.size() - 1).length);
static_assert(!parseAudioRequest(nullptr, audioRead.size()).length);

constexpr const uint8_t* bridgeNonce = hello.data() + 1;
static_assert(same(handshakeSalt(capability, bridgeNonce),
    bytes("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f")));
static_assert(same(proofMessage("bridge", capability, bridgeNonce),
    bytes("62726964676502000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f")));
static_assert(same(proofMessage("pala", capability, bridgeNonce),
    bytes("70616c6102000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f")));
static_assert(kKdfInfo == std::string_view("KUBO_BLE_V2 keys") && kSessionKeySize == 32);
}  // namespace
