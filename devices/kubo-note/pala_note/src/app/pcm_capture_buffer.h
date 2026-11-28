#pragma once

#include <atomic>
#include <cstddef>
#include <cstdint>

namespace pcm_capture {

template<std::size_t BlockCount, std::size_t Frames>
class Buffer {
  static_assert(BlockCount > 0 && (BlockCount & (BlockCount - 1)) == 0);
  static_assert(BlockCount < (uint32_t{1} << 31));

 public:
  struct Block {
    int16_t samples[Frames];
    uint32_t sampledAt;
    bool marker;
  };

  struct Storage { Block blocks[BlockCount]; };

  explicit Buffer(Storage& storage) : storage_(storage) {}

  // Only the producer calls pushStereo and only the consumer calls pop.
  // Release the slot after its entire block has been copied.
  bool pushStereo(const int16_t* samples, uint32_t sampledAt, bool marker) {
    if (overflow_) return false;
    const uint32_t write = write_.load(std::memory_order_relaxed);
    const uint32_t reserved = write - read_.load(std::memory_order_acquire);
    if (reserved == BlockCount) {
      overflow_ = true;
      return false;
    }
    auto& block = storage_.blocks[write & (BlockCount - 1)];
    for (std::size_t i = 0; i < Frames; ++i) block.samples[i] = samples[2 * i];
    block.sampledAt = sampledAt;
    block.marker = marker;
    // Producer-side occupancy at reservation, including this block.
    if (reserved + 1 > highWater_) highWater_ = reserved + 1;
    write_.store(write + 1, std::memory_order_release);
    return true;
  }

  bool pop(Block& block) {
    const uint32_t read = read_.load(std::memory_order_relaxed);
    if (read == write_.load(std::memory_order_acquire)) return false;
    block = storage_.blocks[read & (BlockCount - 1)];
    read_.store(read + 1, std::memory_order_release);
    return true;
  }

  // One write index increment per successfully queued microphone block.
  uint32_t producedBlocks() const { return write_.load(std::memory_order_acquire); }

  // Read on the producer, or after the producer has published completion.
  uint32_t highWaterBlocks() const { return highWater_; }

  // The owner resets only after the producer and consumer have stopped.
  void reset() {
    read_.store(0, std::memory_order_relaxed);
    write_.store(0, std::memory_order_relaxed);
    overflow_ = false;
    highWater_ = 0;
  }

 private:
  Storage& storage_;
  std::atomic<uint32_t> read_{0};
  std::atomic<uint32_t> write_{0};
  bool overflow_ = false;
  uint32_t highWater_ = 0;
};

inline constexpr std::size_t recorderFrames = 256;
// One 256-frame block per 16 ms at 16 kHz. The slow test card has stalled
// for 6.5 s. Reserve 16.384 s once at boot so FAT never blocks the producer.
using RecorderBuffer = Buffer<1024, recorderFrames>;

}  // namespace pcm_capture
