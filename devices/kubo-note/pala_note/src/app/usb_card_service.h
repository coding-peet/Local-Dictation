#pragma once

#if PALA_USB_CARD_SERVICE
#include <cstdint>
#include <cstring>


void runUsbCardService();

namespace usb_card_service {
constexpr uint32_t kSectorBytes = 512;
constexpr uint32_t kBufferBytes = 4096;
constexpr int32_t kBadRange = -2;
constexpr int32_t kMediaError = -1;

// Accessed only on TinyUSB's device task. Once the host unloads the
// medium, no subsequent request can write while its status is draining.
class Medium {
 public:
  bool present() const { return loaded_; }
  void preventRemoval(bool prevent) { locked_ = prevent; }

  bool startStop(bool start, bool eject) {
    if (!eject || start) return loaded_;
    if (locked_) return false;
    loaded_ = false;
    return true;
  }

  bool ejectStatusSent(uint8_t opcode, uint8_t flags) const {
    return !loaded_ && opcode == 0x1b && (flags & 3) == 2;
  }

 private:
  bool loaded_ = true;
  bool locked_ = false;
};

// Callers provide internal DMA-capable scratch. Bounds are checked before
// the first sector I/O, including for requests spanning the end of the card.
template <typename Card>
int32_t transfer(Card& card, uint32_t sectors, uint32_t lba, uint32_t offset,
                 uint8_t* data, uint32_t size, bool write,
                 uint8_t (&scratch)[kBufferBytes]) {
  uint64_t pos = uint64_t(lba) * kSectorBytes + offset;
  const uint64_t limit = uint64_t(sectors) * kSectorBytes;
  if (pos + size > limit || size > uint32_t(INT32_MAX) ||
      (!data && size)) return kBadRange;
  uint32_t done = 0;
  while (done < size) {
    const uint32_t block = uint32_t(pos / kSectorBytes);
    const uint32_t within = uint32_t(pos % kSectorBytes);
    const uint32_t remaining = size - done;
    uint32_t count;
    uint32_t blocks;
    if (within || remaining < kSectorBytes) {
      count = kSectorBytes - within;
      if (count > remaining) count = remaining;
      blocks = 1;
    } else {
      blocks = remaining / kSectorBytes;
      if (blocks > kBufferBytes / kSectorBytes) blocks = kBufferBytes / kSectorBytes;
      count = blocks * kSectorBytes;
    }
    if ((!write || count < kSectorBytes) && !card.read(block, scratch, blocks)) {
      return kMediaError;
    }
    if (write) {
      std::memcpy(scratch + within, data + done, count);
      if (!card.write(block, scratch, blocks)) return kMediaError;
    } else {
      std::memcpy(data + done, scratch + within, count);
    }
    pos += count;
    done += count;
  }
  return int32_t(size);
}
}  // namespace usb_card_service
#endif
