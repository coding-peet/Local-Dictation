#include "../pala_note/src/app/sync_queue_file.h"

#include <algorithm>
#include <cassert>
#include <cstdio>

namespace {
class QueueFile {
 public:
  QueueFile() {
#ifdef _MSC_VER
    assert(tmpfile_s(&file_) == 0);
#else
    file_ = std::tmpfile();
#endif
    assert(file_);
  }
  ~QueueFile() { std::fclose(file_); }
  bool seek(size_t at) { return std::fseek(file_, static_cast<long>(at), SEEK_SET) == 0; }
  size_t read(uint8_t* output, size_t size) { return std::fread(output, 1, size, file_); }
  size_t write(const uint8_t* input, size_t size) { return std::fwrite(input, 1, size, file_); }
 private:
  std::FILE* file_ = nullptr;
};

void visitsAndUpdatesLargeQueue(int notes, int pending) {
  QueueFile original;
  QueueFile updated;
  const int stride = notes / pending;
  for (int item = 1; item <= pending; ++item) {
    const auto kind = item % 7 == 0 ? SyncQueueKind::Organization : SyncQueueKind::Audio;
    assert(writeSyncQueueEntry(original, {item * stride, kind}));
  }
  int visited = 0;
  assert(visitSyncQueueEntries(original, pending, false, [&](const SyncQueueEntry& entry) {
    assert(entry.note == ++visited * stride);
    return true;
  }));
  assert(visited == pending);
  assert(visitSyncQueueEntries(original, pending, true, [&](const SyncQueueEntry& entry) {
    assert(entry.note == visited-- * stride);
    return true;
  }));
  assert(visited == 0);
  assert(visitSyncQueueEntries(original, pending, true, [&](const SyncQueueEntry& entry) {
    assert(entry.note == notes);
    ++visited;
    return false;
  }));
  assert(visited == 1);
  assert(rewriteSyncQueueEntries(original, pending, updated, {notes / 2, SyncQueueKind::None}));
  visited = 0;
  assert(visitSyncQueueEntries(updated, pending - 1, false, [&](const SyncQueueEntry& entry) {
    assert(entry.note != notes / 2);
    ++visited;
    return true;
  }));
  assert(visited == pending - 1);
  assert(!visitSyncQueueEntries(updated, pending, false, [](const auto&) { return true; }));
}

void interruptedNotePrecedesNewAndExistingBacklog() {
  QueueFile queue;
  for (int note = 1; note <= 1000; ++note)
    assert(writeSyncQueueEntry(queue, {note, SyncQueueKind::Audio}));
  InterruptedSyncResume resume;
  resume.interrupted(340, 511);
  QueueFile withNewRecording;
  assert(rewriteSyncQueueEntries(queue, 1000, withNewRecording, {1001, SyncQueueKind::Audio}));
  int first = 0;
  assert(visitSyncQueueEntries(withNewRecording, 1001, false, [&](const SyncQueueEntry& item) {
    if (item.note < resume.preferred()) return true;
    first = item.note;
    return false;
  }));
  assert(first == 340);
  resume.stageCommitted(340, SyncQueueKind::Organization);
  assert(resume.preferred() == 340);
  resume.stageCommitted(340, SyncQueueKind::Embedding);
  assert(resume.preferred() == 0);
  QueueFile afterResume;
  assert(rewriteSyncQueueEntries(withNewRecording, 1001, afterResume, {340, SyncQueueKind::None}));
  std::array<bool, 1002> seen{};
  seen[340] = true;
  assert(visitSyncQueueEntries(afterResume, 1000, true, [&](const SyncQueueEntry& item) {
    assert(!seen[item.note]);
    seen[item.note] = true;
    return true;
  }));
  assert(std::all_of(seen.begin() + 1, seen.end(), [](bool value) { return value; }));
  assert(InterruptedSyncResume{}.preferred() == 0);
}

void rejectsCorruptAndDuplicateEntries() {
  QueueFile invalid;
  assert(writeSyncQueueEntry(invalid, {3, SyncQueueKind::Audio}));
  assert(writeSyncQueueEntry(invalid, {3, SyncQueueKind::Organization}));
  assert(!visitSyncQueueEntries(invalid, 2, false, [](const auto&) { return true; }));
  assert(!visitSyncQueueEntries(invalid, 2, true, [](const auto&) { return true; }));
}

void insertsAndChangesARecordWithoutRetainingCopies() {
  QueueFile empty;
  QueueFile first;
  QueueFile second;
  QueueFile third;
  assert(rewriteSyncQueueEntries(empty, 0, first, {20, SyncQueueKind::Audio}));
  assert(rewriteSyncQueueEntries(first, 1, second, {10, SyncQueueKind::Audio}));
  assert(rewriteSyncQueueEntries(second, 2, third, {20, SyncQueueKind::Organization}));
  int seen = 0;
  assert(visitSyncQueueEntries(third, 2, false, [&](const auto& entry) {
    if (seen++ == 0) assert(entry.note == 10 && entry.kind == SyncQueueKind::Audio);
    else assert(entry.note == 20 && entry.kind == SyncQueueKind::Organization);
    return true;
  }));
  assert(seen == 2);
}
}

int main() {
  visitsAndUpdatesLargeQueue(10000, 1000);
  visitsAndUpdatesLargeQueue(50000, 500);
  visitsAndUpdatesLargeQueue(100000, 500);
  interruptedNotePrecedesNewAndExistingBacklog();
  rejectsCorruptAndDuplicateEntries();
  insertsAndChangesARecordWithoutRetainingCopies();
}
