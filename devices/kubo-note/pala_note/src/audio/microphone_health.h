#pragma once
#include "microphone_loss.h"

namespace microphone {

enum class ReadAction { Block, Retry, Stop };

struct Stats {
  bool observed = false;
  uint32_t dmaOverruns = 0;
  uint32_t dmaLostBytes = 0;
  uint32_t reconfigReads = 0;
  uint32_t readErrors = 0;
  uint32_t maxReadGapUs = 0;
  uint32_t ringHighBlocks = 0;

  bool intact() const {
    return observed && !dmaOverruns && !dmaLostBytes && !reconfigReads && !readErrors;
  }
};

class CaptureHealth {
 public:
  explicit CaptureHealth(AudioDmaSnapshot start) : start_(start) {}

  ReadAction read(int status, uint32_t nowUs) {
    if (status == AUDIO_RX_RECONFIGURING) {
      ++stats_.reconfigReads;
      if (!retrying_) { retrying_ = true; retryStartUs_ = nowUs; }
      // Match the existing one-second deadline of a real I2S read.
      return nowUs - retryStartUs_ >= 1000000 ? ReadAction::Stop : ReadAction::Retry;
    }
    if (status) {
      ++stats_.readErrors;
      return ReadAction::Stop;
    }
    if (hasBlock_) {
      const auto gap = nowUs - previousUs_;
      if (gap > stats_.maxReadGapUs) stats_.maxReadGapUs = gap;
    }
    previousUs_ = nowUs;
    hasBlock_ = true;
    retrying_ = false;
    return ReadAction::Block;
  }

  Stats finish(AudioDmaSnapshot end, uint32_t highBlocks) const {
    auto out = stats_;
    out.observed = hasBlock_ && start_.monitored && end.monitored;
    out.dmaOverruns = end.overruns - start_.overruns;
    out.dmaLostBytes = end.bytes - start_.bytes;
    out.ringHighBlocks = highBlocks;
    return out;
  }

 private:
  AudioDmaSnapshot start_;
  Stats stats_;
  bool hasBlock_ = false, retrying_ = false;
  uint32_t previousUs_ = 0, retryStartUs_ = 0;
};

} // namespace microphone
