#include "aquarium_view.h"

#include "../../globals.h"
#include "draw.h"
#include <miniz.h>

namespace {
struct FrameRange { int first; int count; };
struct PackedFrame { uint32_t offset; uint16_t size; };

#include "aquarium_frames.inc"
constexpr int kAnemoneFrameCount = sizeof(kAquariumFrames) / sizeof(kAquariumFrames[0]);
#include "aquarium_reef_frames.inc"
constexpr int kReefFrameCount = sizeof(kReefFrames) / sizeof(kReefFrames[0]);

// ROM's convenience inflater allocates its large tables on the calling stack.
tinfl_decompressor aquariumInflater;

constexpr FrameRange framesFor(AquariumScene scene, AquariumReef reef) {
  const bool coral = reef == AquariumReef::TorchCoral;
  switch (scene) {
    case AquariumScene::Opening: return kOpeningFrames;
    case AquariumScene::Ready: return coral ? kReefReadyFrames : kReadyFrames;
    case AquariumScene::Feeding: return coral ? kReefFeedingFrames : kFeedingFrames;
    case AquariumScene::Current: return coral ? kReefCurrentFrames : kCurrentFrames;
    case AquariumScene::Travel: return coral ? kTravelToAnemoneFrames : kTravelToCoralFrames;
    case AquariumScene::Hiding: return coral ? kReefHidingFrames : kHidingFrames;
    case AquariumScene::Peek: return coral ? kReefPeekFrames : kPeekFrames;
    case AquariumScene::Closed: return {0, 0};
  }
  return {0, 0};
}
}  // namespace

int aquariumFrame(AquariumScene scene, AquariumReef reef, uint32_t elapsed) {
  const auto frames = framesFor(scene, reef);
  if (!frames.count) return -1;
  if (scene == AquariumScene::Ready) return frames.first + (elapsed / 563) % frames.count;
  const uint32_t duration = aquariumDuration(scene);
  const int index = elapsed >= duration ? frames.count - 1 : elapsed * frames.count / duration;
  return frames.first + index;
}

bool showAquariumFrame(int frame, void (*serviceWhileWaiting)()) {
  static_assert(W == 200 && H == 200 && BPR == 25);
  if (frame < 0 || frame >= kAnemoneFrameCount + kReefFrameCount) return false;
  const bool reefFrame = frame >= kAnemoneFrameCount;
  const auto& packed = reefFrame ? kReefFrames[frame - kAnemoneFrameCount] : kAquariumFrames[frame];
  const auto* pixels = reefFrame ? kReefPixels : kAquariumPixels;
  constexpr size_t bytes = BPR * H;
  uint8_t* const frameBuffer = display->getBuffer();
  size_t inputBytes = packed.size;
  size_t outputBytes = bytes;
  tinfl_init(&aquariumInflater);
  const tinfl_status status = tinfl_decompress(&aquariumInflater, pixels + packed.offset,
                                               &inputBytes, frameBuffer, frameBuffer, &outputBytes,
                                               TINFL_FLAG_PARSE_ZLIB_HEADER |
                                                   TINFL_FLAG_USING_NON_WRAPPING_OUTPUT_BUF);
  if (status != TINFL_STATUS_DONE || inputBytes != packed.size || outputBytes != bytes) return false;
  refreshFast(serviceWhileWaiting);
  return true;
}
