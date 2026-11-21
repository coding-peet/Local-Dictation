#include "aquarium_view.h"

#include "../../globals.h"
#include "draw.h"
#include <miniz.h>

namespace {
struct FrameRange { int first; int count; };
struct PackedFrame { uint32_t offset; uint16_t size; };

#include "aquarium_frames.inc"

// ROM's convenience inflater allocates its large tables on the calling stack.
tinfl_decompressor aquariumInflater;

constexpr FrameRange framesFor(AquariumScene scene) {
  switch (scene) {
    case AquariumScene::Opening: return kOpeningFrames;
    case AquariumScene::Ready: return kReadyFrames;
    case AquariumScene::Feeding: return kFeedingFrames;
    case AquariumScene::Current: return kCurrentFrames;
    case AquariumScene::Hiding: return kHidingFrames;
    case AquariumScene::Peek: return kPeekFrames;
    case AquariumScene::Closed: return {0, 0};
  }
  return {0, 0};
}
}  // namespace

int aquariumFrame(AquariumScene scene, uint32_t elapsed) {
  const auto frames = framesFor(scene);
  if (!frames.count) return -1;
  if (scene == AquariumScene::Ready) return frames.first + (elapsed / 563) % frames.count;
  const uint32_t duration = aquariumDuration(scene);
  const int index = elapsed >= duration ? frames.count - 1 : elapsed * frames.count / duration;
  return frames.first + index;
}

bool showAquariumFrame(int frame, void (*serviceWhileWaiting)()) {
  static_assert(W == 200 && H == 200 && BPR == 25);
  if (frame < 0 || frame >= static_cast<int>(sizeof(kAquariumFrames) / sizeof(kAquariumFrames[0]))) return false;
  const auto& packed = kAquariumFrames[frame];
  constexpr size_t bytes = BPR * H;
  uint8_t* const frameBuffer = display->getBuffer();
  size_t inputBytes = packed.size;
  size_t outputBytes = bytes;
  tinfl_init(&aquariumInflater);
  const tinfl_status status = tinfl_decompress(&aquariumInflater, kAquariumPixels + packed.offset,
                                               &inputBytes, frameBuffer, frameBuffer, &outputBytes,
                                               TINFL_FLAG_PARSE_ZLIB_HEADER |
                                                   TINFL_FLAG_USING_NON_WRAPPING_OUTPUT_BUF);
  if (status != TINFL_STATUS_DONE || inputBytes != packed.size || outputBytes != bytes) return false;
  refreshFast(serviceWhileWaiting);
  return true;
}
