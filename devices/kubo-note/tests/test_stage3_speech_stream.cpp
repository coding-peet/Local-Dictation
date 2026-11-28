#include "speech_stream.h"
#include "speech_transfer.h"

#include <algorithm>
#include <cassert>
#include <chrono>
#include <cstdlib>
#include <thread>

using namespace std::chrono_literals;

namespace {
const auto startedAt = std::chrono::steady_clock::now();
std::atomic<int> recordPin{HIGH};
std::atomic<unsigned> audibleBlocks{0};
std::atomic<unsigned> idleCalls{0};
std::atomic<int> volume{0};
std::atomic<unsigned> allocations{0};
std::atomic<unsigned> workers{0};

bool waitFor(const auto& predicate) {
  const auto deadline = std::chrono::steady_clock::now() + 2s;
  while (!predicate() && std::chrono::steady_clock::now() < deadline)
    std::this_thread::sleep_for(1ms);
  return predicate();
}

void reset() {
  assert(waitFor([] { return workers == 0; }));
  assert(allocations == 0);
  audibleBlocks = 0;
  idleCalls = 0;
  volume = 0;
  recordPin = HIGH;
}

void putSamples(PalaSpeechStream& stream, size_t samples) {
  for (size_t i = 0; i < samples; ++i) {
    assert(stream.put(0x0a));
    assert(stream.put(0x05));
  }
}

void testShortCompleteStreamStartsAndReleasesAudio() {
  reset();
  PalaSpeechStream stream;
  assert(stream.begin());
  putSamples(stream, 12);
  assert(!stream.ready());
  assert(stream.finish(true));
  assert(stream.wasStarted() && !stream.wasCancelled());
  assert(audibleBlocks == 1 && idleCalls == 1 && volume == 0);
}

void testCancellationBeforeFirstAudioDoesNotRetryOrPlay() {
  reset();
  PalaSpeechStream stream;
  assert(stream.begin());
  putSamples(stream, 12);
  recordPin = 0;
  assert(waitFor([&] { return !stream.poll(); }));
  assert(stream.wasCancelled() && !stream.wasStarted());
  assert(!speechRetryAllowed(0, 503, false, false, stream.wasCancelled()));
  assert(stream.finish(false));
  assert(audibleBlocks == 0);
}

void testCancellationDuringAudioStopsAndDoesNotReplay() {
  reset();
  PalaSpeechStream stream;
  assert(stream.begin());
  putSamples(stream, kPcmStreamPrebuffer / 2);
  assert(stream.ready());
  stream.start();
  assert(waitFor([] { return audibleBlocks > 0; }));
  recordPin = 0;
  assert(waitFor([&] { return !stream.poll(); }));
  assert(!stream.put(0));
  assert(stream.finish(false));
  assert(stream.wasCancelled() && stream.wasStarted());
  assert(!speechRetryAllowed(0, 503, false, true, stream.wasCancelled()));
  assert(idleCalls == 1 && volume == 0);
}

void testMalformedPcmNeverPlays() {
  reset();
  PalaSpeechStream stream;
  assert(stream.begin() && stream.put(0x05));
  assert(!stream.finish(true));
  assert(audibleBlocks == 0);
}
}

int digitalRead(int) { return recordPin; }
uint32_t millis() {
  return 1 + static_cast<uint32_t>(
      std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now() - startedAt).count());
}
void delay(unsigned ms) { std::this_thread::sleep_for(std::chrono::milliseconds(ms)); }
void vTaskDelay(unsigned ms) { delay(ms); }
void vTaskDelete(void*) { --workers; }
int xTaskCreatePinnedToCore(void (*worker)(void*), const char*, unsigned, void* context, int, void*, int) {
  ++workers;
  std::thread(worker, context).detach();
  return pdPASS;
}
void* heap_caps_malloc(size_t size, int) {
  auto* pointer = std::malloc(size);
  if (pointer) ++allocations;
  return pointer;
}
void heap_caps_free(void* pointer) {
  if (pointer) --allocations;
  std::free(pointer);
}
int palaPlaybackVolume() { return 35; }
void audio_playback_set_vol(int value) { volume = value; }
int audio_playback_write(void* buffer, size_t bytes) {
  const auto* samples = static_cast<const int16_t*>(buffer);
  if (std::any_of(samples, samples + bytes / sizeof(int16_t), [](int16_t sample) { return sample != 0; }))
    ++audibleBlocks;
  delay(1);
  return 0;
}
void audio_idle_suspend() { ++idleCalls; }

int main() {
  testShortCompleteStreamStartsAndReleasesAudio();
  testCancellationBeforeFirstAudioDoesNotRetryOrPlay();
  testCancellationDuringAudioStopsAndDoesNotReplay();
  testMalformedPcmNeverPlays();
  reset();
  assert(speechRetryAllowed(0, 503, false, false, false));
  assert(!speechRetryAllowed(0, 401, false, false, false));
  assert(!speechRetryAllowed(1, 503, false, false, false));
  assert(!speechStreamWithinDeadline(100, 100, 15100, true));
}
