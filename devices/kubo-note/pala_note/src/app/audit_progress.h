#pragma once

#include "storage_audit_policy.h"
#include <cstdio>

enum class NoteStorageAuditPhase : uint8_t {
  LoadCatalog, Trash, LoadQueue, CheckCatalog, Discovering, Checking, ScanOrphans,
  TaskState, AgendaKeys, AgendaStates, SortStates, SortAgenda, AgendaSources, QueueSources,
  BuildAgenda, BuildQueue, VerifyAgenda, VerifyQueue, VerifyKeys, Saving, Finished, Incomplete
};
struct NoteStorageAuditProgress {
  NoteStorageAuditPhase phase;
  uint32_t checked, total, problems, elapsedSeconds;
};
using NoteStorageAuditProgressCallback = void (*)(const NoteStorageAuditProgress&);

// These hooks are inactive outside the exclusive, physically confirmed audit.
void noteAuditWork(NoteStorageAuditPhase phase, uint32_t done, uint32_t total = 0);

namespace note_maintenance {
class AuditDisplay {
 public:
  AuditDisplay(uint32_t startedAt, NoteStorageAuditProgressCallback notify)
      : startedAt_(startedAt), notify_(notify) {}
  void update(uint32_t now, NoteStorageAuditPhase phase, uint32_t done, uint32_t total,
              uint32_t problems, bool force = false) {
    if (shown_ && !force && now - lastAt_ < 3000) return;
    shown_ = true;
    lastAt_ = now;
    if (notify_) notify_({phase, done, total, problems, (now - startedAt_) / 1000});
  }
 private:
  uint32_t startedAt_, lastAt_ = 0;
  NoteStorageAuditProgressCallback notify_;
  bool shown_ = false;
};

struct AuditCaption { const char* phase; const char* unit; };
inline AuditCaption auditCaption(NoteStorageAuditPhase phase) {
  using P = NoteStorageAuditPhase;
  switch (phase) {
    case P::LoadCatalog: return {"Reading catalog", "rows"};
    case P::Trash: return {"Checking trash", "entries"};
    case P::LoadQueue: return {"Reading queue", "rows"};
    case P::CheckCatalog: return {"Checking catalog", "rows"};
    case P::Discovering: return {"Finding notes", "files"};
    case P::Checking: return {"Checking notes", "notes"};
    case P::ScanOrphans: return {"Checking orphans", "files"};
    case P::TaskState: return {"Checking task state", "rows"};
    case P::AgendaKeys: return {"Checking agenda", "keys"};
    case P::AgendaStates: return {"Reading task states", "rows"};
    case P::SortStates: return {"Sorting task states", "keys"};
    case P::SortAgenda: return {"Sorting agenda", "keys"};
    case P::AgendaSources: return {"Checking task sources", "notes"};
    case P::QueueSources: return {"Checking queue sources", "notes"};
    case P::BuildAgenda: return {"Rebuilding agenda", "notes"};
    case P::BuildQueue: return {"Rebuilding queue", "notes"};
    case P::VerifyAgenda: return {"Verifying agenda", "notes"};
    case P::VerifyQueue: return {"Verifying queue", "notes"};
    case P::VerifyKeys: return {"Verifying agenda keys", "keys"};
    case P::Saving: return {"Saving result", "notes"};
    case P::Finished: return {"Check finished", "notes"};
    case P::Incomplete: return {"Check incomplete", "notes"};
  }
  return {"Checking storage", "items"};
}

inline void auditCountText(const NoteStorageAuditProgress& frame, char* text, size_t capacity) {
  const auto done = static_cast<unsigned long>(frame.checked);
  if (frame.total && (frame.checked < frame.total || frame.phase == NoteStorageAuditPhase::Finished)) {
    std::snprintf(text, capacity, "%lu / %lu %s", done, static_cast<unsigned long>(frame.total),
                  auditCaption(frame.phase).unit);
  } else {
    std::snprintf(text, capacity, "%lu %s", done, auditCaption(frame.phase).unit);
  }
}

inline unsigned auditBarPercent(const NoteStorageAuditProgress& frame) {
  if (frame.phase == NoteStorageAuditPhase::Finished) return 100;
  if (!frame.total) return 0;
  const auto percent = uint64_t{frame.checked} * 100 / frame.total;
  return percent > 99 ? 99 : static_cast<unsigned>(percent);
}

template<class Arm, class Restart>
bool executeConfirmedRestart(NoteRestartMode mode, bool accepted, uint32_t& cookie, uint32_t token,
                             Arm markRunning, Restart restart) {
  if (!accepted) return true;
  cookie = 0;
  if (mode == NoteRestartMode::Audit) {
    if (!markRunning()) return false;
    cookie = token;
  }
  restart();
  return true;
}

// Consume the retained cookie *before* long SD operations. Persist Incomplete
// first so every power cut thereafter already has the correct Settings state.
template<class Report, class Save>
bool claimAuditBoot(bool cookie, bool softwareReset, Report& report, Save save) {
  const bool claimed = shouldRunConfirmedAudit(cookie, softwareReset, report.state);
  if (report.state != AuditState::Running) return false;
  report.state = AuditState::Incomplete;
  return save(report) && claimed;
}

template<class Report, class Save>
bool finishAudit(bool verified, Report& report, Save save) {
  if (verified) {
    report.state = AuditState::Complete;
    if (save(report)) return true;
  }
  report.state = AuditState::Incomplete;
  save(report);
  return false;
}

constexpr AuditState visibleAuditState(AuditState state, bool active) {
  if (active) return AuditState::Running;
  return state == AuditState::Running ? AuditState::Incomplete : state;
}
}  // namespace note_maintenance
