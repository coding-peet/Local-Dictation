#include "../pala_note/src/app/pcm_capture_buffer.h"

#include <cassert>
#include <thread>

using Buffer = pcm_capture::Buffer<4, 2>;

static bool produce(Buffer& buffer, int16_t first, uint32_t timestamp, bool marker = false) {
  const int16_t stereo[] = {first, -100, static_cast<int16_t>(first + 1), -200};
  return buffer.pushStereo(stereo, timestamp, marker);
}

static void checkNext(Buffer& buffer, int16_t first, uint32_t timestamp, bool marker = false) {
  Buffer::Block block{};
  assert(buffer.pop(block));
  assert(block.samples[0] == first);
  assert(block.samples[1] == first + 1);
  assert(block.sampledAt == timestamp);
  assert(block.marker == marker);
}

static void runAcrossThreads() {
  using ThreadBuffer = pcm_capture::Buffer<128, 2>;
  ThreadBuffer::Storage storage;
  ThreadBuffer buffer(storage);
  // Credit limits batches to fewer than 128 outstanding blocks. Run many wraps.
  std::atomic<uint32_t> consumed{0};
  constexpr uint32_t count = 8000;
  std::thread producer([&] {
    for (uint32_t i = 0; i < count; ++i) {
      while (i - consumed.load(std::memory_order_acquire) >= 64) std::this_thread::yield();
      const int16_t stereo[] = {static_cast<int16_t>(i), -100, static_cast<int16_t>(i + 1), -200};
      assert(buffer.pushStereo(stereo, i * 16, i % 7 == 0));
    }
  });
  for (uint32_t i = 0; i < count; ++i) {
    ThreadBuffer::Block block;
    while (!buffer.pop(block)) std::this_thread::yield();
    assert(block.samples[0] == static_cast<int16_t>(i));
    assert(block.samples[1] == static_cast<int16_t>(i + 1));
    assert(block.sampledAt == i * 16);
    assert(block.marker == (i % 7 == 0));
    consumed.store(i + 1, std::memory_order_release);
  }
  producer.join();
}

int main() {
  Buffer::Storage storage;
  Buffer buffer(storage);
  Buffer::Block block{};

  // The writer could be allocating an ID, opening a file or waiting on FAT.
  assert(produce(buffer, 0, 16));
  assert(produce(buffer, 2, 32, true));
  assert(produce(buffer, 4, 48));
  assert(produce(buffer, 6, 64));
  checkNext(buffer, 0, 16);
  checkNext(buffer, 2, 32, true);
  assert(produce(buffer, 8, 80));  // Wrap onto slots a writer already released.
  assert(produce(buffer, 10, 96));
  checkNext(buffer, 4, 48);
  checkNext(buffer, 6, 64);
  checkNext(buffer, 8, 80);
  checkNext(buffer, 10, 96);
  assert(!buffer.pop(block));

  buffer.reset();
  assert(produce(buffer, 10, 16));
  assert(produce(buffer, 12, 32));
  assert(produce(buffer, 14, 48));
  assert(produce(buffer, 16, 64));
  assert(!produce(buffer, 18, 80));
  checkNext(buffer, 10, 16);
  assert(!produce(buffer, 20, 96));  // An overflow ends this contiguous take.
  checkNext(buffer, 12, 32);
  checkNext(buffer, 14, 48);
  checkNext(buffer, 16, 64);
  assert(!buffer.pop(block));

  buffer.reset();
  assert(produce(buffer, 100, 16));
  checkNext(buffer, 100, 16);
  assert(!buffer.pop(block));
  runAcrossThreads();
}
