#pragma once

#include "../../config.h"
#include <cstdint>
#if PALA_STORAGE_PROBE
#include "storage_probe.h"
#include <cerrno>
#include <esp_timer.h>
#endif

namespace stage2 {
enum class WavOpenCall : uint8_t { Path, PosixOpen, Fdopen, Setvbuf, Close };
enum class WavOpenResult : uint8_t { Opened = 1, OpenFailed, FdopenFailed, SetvbufFailed };
enum class WavIoPhase : uint8_t { Open, Header };

#if PALA_STORAGE_PROBE
using WavOpenParts = uint32_t[5];
void captureWavIo(uint32_t edge, WavIoPhase phase, const storage_probe::Sample& sample,
                  const WavOpenParts& parts, uint32_t result, uint32_t error);

// Each writer measures its own calls. Subcall intervals overlap the lock
// and SDMMC buckets and are exported separately.
class WavIoTiming {
 public:
  WavIoTiming(uint32_t edge, bool recording) : edge_(edge), enabled_(recording) {}

  void begin() {
    if (!enabled_) return;
    const int saved = errno;
    storage_probe::begin();
    previous_ = static_cast<uint32_t>(esp_timer_get_time());
    errno = saved;
  }
  void after(WavOpenCall call) {
    if (!enabled_) return;
    const int saved = errno;
    const auto now = static_cast<uint32_t>(esp_timer_get_time());
    parts_[static_cast<unsigned>(call)] = now - previous_;
    previous_ = now;
    errno = saved;
  }
  void finishOpen(WavOpenResult status, int error = 0) {
    finish(WavIoPhase::Open, static_cast<uint32_t>(status), error);
  }
  void finishHeader(bool complete) {
    finish(WavIoPhase::Header, complete ? 1 : 2, complete ? 0 : errno);
  }

 private:
  void finish(WavIoPhase phase, uint32_t result, int error) {
    if (!enabled_) return;
    const int saved = errno;
    const auto sample = storage_probe::finish();
    captureWavIo(edge_, phase, sample, parts_, result, static_cast<uint32_t>(error));
    errno = saved;
  }
  uint32_t edge_, previous_ = 0;
  bool enabled_;
  WavOpenParts parts_{};
};
#else
class WavIoTiming {
 public:
  WavIoTiming(uint32_t, bool) {}
  void begin() {}
  void after(WavOpenCall) {}
  void finishOpen(WavOpenResult, int = 0) {}
  void finishHeader(bool) {}
};
#endif
} // namespace stage2
