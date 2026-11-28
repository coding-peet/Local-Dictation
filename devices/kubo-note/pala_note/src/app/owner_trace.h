#pragma once
#include <stdint.h>

namespace owner_trace {

enum class Work : uint32_t {
  Unknown, Mount, Layout, Home, Catalog, Recovery, Queue, Usb, Sync, Audit,
  Settings, Spool, Archive, Boot, Forecast, Holidays, Delivery, Clock, Space
};
enum class FatOp : uint32_t {
  Unknown, Open, Read, Write, OpenDir, ReadDir, Stat, Close, CloseDir, Sync,
  Seek, Unlink, Rename, Mkdir, Truncate, Free,
  VfsOpen, VfsRead, VfsWrite, VfsClose, VfsStat, VfsFstat, VfsOpenDir,
  VfsReadDir, VfsCloseDir, Fdopen, Setvbuf, Fwrite
};
enum Flags : uint32_t {
  QueueKnown = 1, ReleaseSeen = 2, WorkAtBlock = 4, FatAtBlock = 8,
  CommandAtBlock = 16, EventAtBlock = 32, WorkChanged = 64, FatChanged = 128,
  OpcodeChanged = 256, ReleaseUncertain = 512, DelayAtBlock = 1024, DelayObserved = 2048
};

struct Wait {
  uint32_t mutex, holder, waiterPc, fatPc, work, fatOp, opcode;
  uint32_t waitUs, heldUs, hostUs, eventUs, flags, delayUs;
  char task[16];
};

struct Evidence {
  uint32_t waits, omitted, contextDrops;
  Wait longest[2];
};

class WorkScope {
 public:
#if PALA_VFS_OWNER_TRACE
  explicit WorkScope(Work work);
  ~WorkScope();
  WorkScope(const WorkScope&) = delete;
  WorkScope& operator=(const WorkScope&) = delete;
 private:
  int slot_;
  uint32_t previous_;
#else
  explicit WorkScope(Work) {}
#endif
};

}  // namespace owner_trace
