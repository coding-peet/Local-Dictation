#pragma once

#include "sync_queue_file.h"

// Compare two ordered streams exactly, including the remaining source tail
// when a queue is empty or its last row has been lost.
template<class Notes, class Classify, class Visit>
bool queueMatchesSource(const Notes& notes, Classify classify, Visit visit) {
  auto next = notes.begin();
  bool healthy = true;
  const auto expected = [&]() {
    while (next != notes.end()) {
      const auto& note = *next++;
      SyncQueueKind kind;
      if (!classify(note, kind)) { healthy = false; break; }
      if (kind != SyncQueueKind::None) return SyncQueueEntry{note.num, kind};
    }
    return SyncQueueEntry{};
  };
  const bool read = visit([&](const auto& actual) {
    const auto row = expected();
    healthy = healthy && row.note == actual.note && row.kind == actual.kind;
    return healthy;
  });
  if (!read || !healthy) return false;
  const auto remaining = expected();
  return healthy && !remaining.note;
}

// One recent-order row per source task, including its original per-note ordinal.
// The caller independently verifies the due-order permutation and payload fields.
template<class Notes, class Load, class Compare, class Tasks>
bool agendaMatchesSource(const Notes& notes, uint32_t count, Load load, Compare compare, Tasks& tasks) {
  uint32_t position = 0;
  for (const auto& note : notes) {
    tasks.clear();
    if (!load(note.num, tasks)) return false;
    for (size_t ordinal = 0; ordinal < tasks.size(); ++ordinal) {
      if (position == count || !compare(position, ordinal, tasks[ordinal])) return false;
      ++position;
    }
  }
  return position == count;
}
