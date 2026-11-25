#pragma once

#include "sync_queue_file.h"
#include <functional>

struct SyncQueueCounts {
  int audio = 0;
  int organization = 0;
};

void initializeSyncQueue();
void retainSyncQueueRecovery();
bool syncQueueReady();
bool verifySyncQueueSources(bool finalPass = false);
SyncQueueCounts syncQueueCounts();
bool maintainSyncQueue();
int syncQueueRebuildProgressNote();
SyncQueueKind syncQueueKindFor(int note);
bool requestSyncQueueRebuild(bool verifySources = false);
bool visitSyncQueue(bool newestFirst, const std::function<bool(const SyncQueueEntry&)>& visit);

// Invalidates the derived index before authoritative storage changes. Nested
// changes may cover the same note or be enclosed by a global change.
class SyncQueueChange {
 public:
  explicit SyncQueueChange(int note);
  ~SyncQueueChange();
  SyncQueueChange(const SyncQueueChange&) = delete;
  SyncQueueChange& operator=(const SyncQueueChange&) = delete;
  explicit operator bool() const { return accepted_; }
 private:
  bool accepted_ = false;
};
