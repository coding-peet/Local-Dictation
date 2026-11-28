#pragma once

#include <stdint.h>
#if PALA_VFS_OWNER_TRACE
#include "owner_trace.h"
#endif

namespace storage_probe {

// All times are elapsed microseconds on the calling task's WAV operation.
// The six disjoint phase times add to totalUs when valid == 1.
struct Sample {
  uint32_t totalUs;
  uint32_t fatLockUs;
  uint32_t vfsLockUs;
  uint32_t hostLockUs;
  uint32_t commandUs;
  uint32_t eventUs;
  uint32_t otherUs;
  uint32_t lockCalls;
  uint32_t lockBlocks;
  uint32_t commands;
  uint32_t events;
  uint32_t valid;
#if PALA_VFS_OWNER_TRACE
  owner_trace::Evidence owners;
#endif
};

#if PALA_STORAGE_PROBE
// One outer operation at a time; pair begin/finish on its task. A competing
// caller receives valid == 0. The caller retains the sample after finish().
void begin();
Sample finish();
#else
inline void begin() {}
inline Sample finish() { return {}; }
#endif

}  // namespace storage_probe
