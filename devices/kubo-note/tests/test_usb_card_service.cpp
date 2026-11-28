#include "../pala_note/src/app/usb_card_service.h"
#include <algorithm>
#include <array>
#include <cassert>
#include <cstdint>
#include <cstdio>
#include <cstring>

using namespace usb_card_service;

struct MemoryCard {
  static constexpr uint32_t capacity = 14;
  std::array<uint8_t, capacity * 512> bytes{};
  uint32_t reads = 0;
  uint32_t writes = 0;
  bool failReads = false;
  bool failWrites = false;

  bool read(uint32_t lba, uint8_t* data, uint32_t blocks) {
    assert(lba < capacity && blocks <= capacity - lba);
    ++reads;
    if (failReads) return false;
    std::memcpy(data, bytes.data() + 512 * lba, 512 * blocks);
    return true;
  }

  bool write(uint32_t lba, const uint8_t* data, uint32_t blocks) {
    assert(lba < capacity && blocks <= capacity - lba);
    ++writes;
    if (failWrites) return false;
    std::memcpy(bytes.data() + 512 * lba, data, 512 * blocks);
    return true;
  }
};

int main() {
  MemoryCard card;
  uint8_t scratch[kBufferBytes]{};
  for (size_t i = 0; i < card.bytes.size(); ++i) card.bytes[i] = uint8_t(i * 31);

  // Starts inside a sector and spans more than one USB buffer.
  std::array<uint8_t, 5636> data{};
  assert(transfer(card, card.capacity, 1, 513, data.data(), uint32_t(data.size()), false, scratch) == 5636);
  assert(std::equal(data.begin(), data.end(), card.bytes.begin() + 1025));

  auto expected = card.bytes;
  data.fill(0xf1);
  std::fill(expected.begin() + 1025, expected.begin() + 6661, uint8_t(0xf1));
  assert(transfer(card, card.capacity, 1, 513, data.data(), uint32_t(data.size()), true, scratch) == 5636);
  assert(card.bytes == expected);

  // A full-sector write must not depend on reading old data.
  card.failReads = true;
  assert(transfer(card, card.capacity, 13, 0, data.data(), 512, true, scratch) == 512);
  uint32_t writes = card.writes;
  assert(transfer(card, card.capacity, 13, 511, data.data(), 1, true, scratch) < 0);
  assert(card.writes == writes);
  assert(transfer(card, card.capacity, 13, 0, data.data(), 512, false, scratch) < 0);
  card.failReads = false;
  card.failWrites = true;
  assert(transfer(card, card.capacity, 13, 511, data.data(), 1, true, scratch) < 0);

  // Reject the whole request before touching its otherwise valid prefix.
  const uint32_t reads = card.reads;
  writes = card.writes;
  assert(transfer(card, card.capacity, 13, 0, data.data(), 513, true, scratch) < 0);
  assert(transfer(card, card.capacity, UINT32_MAX, UINT32_MAX, data.data(), 1, false, scratch) < 0);
  assert(transfer(card, UINT32_MAX, 0, 0, data.data(), UINT32_MAX, false, scratch) < 0);
  assert(transfer(card, card.capacity, 14, 0, nullptr, 0, false, scratch) == 0);
  assert(card.writes == writes && card.reads == reads);

  Medium medium;
  assert(medium.present());
  assert(medium.startStop(false, false));   // host power management
  assert(medium.present());
  medium.preventRemoval(true);
  assert(!medium.startStop(false, true));
  assert(medium.present());
  medium.preventRemoval(false);
  assert(medium.startStop(false, true));
  assert(!medium.present());
  assert(!medium.ejectStatusSent(0x35, 0)); // SYNCHRONIZE CACHE
  assert(!medium.ejectStatusSent(0x1b, 1)); // START UNIT without eject
  assert(medium.ejectStatusSent(0x1b, 2));
  std::puts("USB card block I/O, failures, bounds and host eject passed");
}
