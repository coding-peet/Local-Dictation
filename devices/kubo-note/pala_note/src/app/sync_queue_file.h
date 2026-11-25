#pragma once

#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdint>

enum class SyncQueueKind : uint32_t { None, Audio, Organization, Embedding };

struct SyncQueueEntry {
  int32_t note = 0;
  SyncQueueKind kind = SyncQueueKind::None;
};

class InterruptedSyncResume {
 public:
  void interrupted(int transportNote, int activeNote) {
    if (transportNote > 0) preferred_ = transportNote;
    else if (activeNote > 0) preferred_ = activeNote;
  }
  int preferred() const { return preferred_; }
  void stageCommitted(int note, SyncQueueKind remaining) {
    if (preferred_ == note && remaining != SyncQueueKind::Audio &&
        remaining != SyncQueueKind::Organization) preferred_ = 0;
  }
 private:
  int preferred_ = 0;
};

constexpr size_t kSyncQueueReadBlock = 16;
static_assert(sizeof(SyncQueueEntry) == 8);

template <typename File>
bool writeSyncQueueEntry(File& file, const SyncQueueEntry& entry) {
  return file.write(reinterpret_cast<const uint8_t*>(&entry), sizeof(entry)) == sizeof(entry);
}

template <typename File, typename Visitor>
bool visitSyncQueueEntries(File& file, size_t count, bool newestFirst, Visitor&& visit,
                           size_t headerSize = 0) {
  std::array<SyncQueueEntry, kSyncQueueReadBlock> block{};
  int32_t prior = 0;
  for (size_t done = 0; done < count;) {
    const size_t size = std::min(block.size(), count - done);
    const size_t offset = newestFirst ? count - done - size : done;
    const size_t bytes = size * sizeof(SyncQueueEntry);
    if (!file.seek(headerSize + offset * sizeof(SyncQueueEntry)) ||
        file.read(reinterpret_cast<uint8_t*>(block.data()), bytes) != bytes) return false;
    for (size_t i = 0; i < size; ++i) {
      const auto& entry = block[newestFirst ? size - i - 1 : i];
      if (entry.note <= 0 || entry.kind < SyncQueueKind::Audio || entry.kind > SyncQueueKind::Embedding ||
          (prior && (newestFirst ? entry.note >= prior : entry.note <= prior))) return false;
      prior = entry.note;
      if (!visit(entry)) return true;
    }
    done += size;
  }
  return true;
}

template <typename Input, typename Output>
bool rewriteSyncQueueEntries(Input& input, size_t count, Output& output, const SyncQueueEntry& update,
                             size_t headerSize = 0) {
  bool written = update.kind == SyncQueueKind::None;
  bool ok = true;
  const bool readable = visitSyncQueueEntries(input, count, false, [&](const SyncQueueEntry& entry) {
    if (!written && update.note <= entry.note) {
      ok = writeSyncQueueEntry(output, update);
      written = true;
    }
    if (ok && entry.note != update.note) ok = writeSyncQueueEntry(output, entry);
    return ok;
  }, headerSize);
  return readable && ok && (written || writeSyncQueueEntry(output, update));
}
