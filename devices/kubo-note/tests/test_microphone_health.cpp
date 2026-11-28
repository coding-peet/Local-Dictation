#include "../pala_note/src/audio/microphone_health.h"
#include "../pala_note/src/app/pcm_capture_buffer.h"
#include <cassert>
#include <cstdint>

int main() {
  const AudioDmaSnapshot before{true, UINT32_MAX, UINT32_MAX - 959};
  microphone::CaptureHealth take(before);
  assert(take.read(0, UINT32_MAX - 7999) == microphone::ReadAction::Block);
  assert(take.read(AUDIO_RX_RECONFIGURING, 1000) == microphone::ReadAction::Retry);
  assert(take.read(0, 8000) == microphone::ReadAction::Block);
  auto stats = take.finish({true, 1, 960}, 3);
  assert(stats.observed && stats.dmaOverruns == 2 && stats.dmaLostBytes == 1920);
  assert(stats.reconfigReads == 1 && !stats.readErrors && stats.maxReadGapUs == 16000);
  assert(stats.ringHighBlocks == 3 && !stats.intact());

  microphone::CaptureHealth later({true, 9, 18432});
  assert(later.read(0, 55) == microphone::ReadAction::Block);
  assert(later.finish({true, 9, 18432}, 1).intact());
  assert(!later.finish({false, 9, 18432}, 1).observed);
  assert(!microphone::CaptureHealth(before).finish(before, 0).intact());

  microphone::CaptureHealth persistent(before);
  assert(persistent.read(AUDIO_RX_RECONFIGURING, UINT32_MAX - 500000) == microphone::ReadAction::Retry);
  assert(persistent.read(AUDIO_RX_RECONFIGURING, 499999) == microphone::ReadAction::Stop);
  assert(!persistent.finish(before, 0).observed);
  microphone::CaptureHealth hard(before);
  assert(hard.read(-3, 10) == microphone::ReadAction::Stop);
  assert(hard.finish(before, 0).readErrors == 1);

  using Buffer = pcm_capture::Buffer<4,1>;
  Buffer::Storage storage{};
  Buffer buffer(storage);
  const int16_t frame[] = {3, 9};
  Buffer::Block block{};
  for (int i = 0; i < 3; ++i) assert(buffer.pushStereo(frame, 0, false));
  assert(buffer.pop(block));
  assert(buffer.pushStereo(frame, 0, false));
  assert(buffer.highWaterBlocks() == 3);
  assert(buffer.pushStereo(frame, 0, false));
  assert(!buffer.pushStereo(frame, 0, false));
  assert(buffer.highWaterBlocks() == 4);
  buffer.reset();
  assert(buffer.highWaterBlocks() == 0);
}
