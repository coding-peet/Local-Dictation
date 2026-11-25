#include "../pala_note/src/app/audit_progress.h"
#include <cassert>
#include <chrono>
#include <filesystem>
#include <fstream>
#include <string>
#include <vector>

using namespace note_maintenance;
using P = NoteStorageAuditPhase;
namespace fs = std::filesystem;

struct Report { AuditState state = AuditState::Never; };
struct Store {
  fs::path path;
  bool write(const Report& report) {
    std::ofstream output(path, std::ios::binary | std::ios::trunc);
    output.write(reinterpret_cast<const char*>(&report), sizeof(report));
    output.close();
    return output && read().state == report.state;
  }
  Report read() {
    Report report;
    std::ifstream input(path, std::ios::binary);
    if (input) input.read(reinterpret_cast<char*>(&report), sizeof(report));
    return report;
  }
};

std::vector<NoteStorageAuditProgress> frames;
void capture(const NoteStorageAuditProgress& frame) { frames.push_back(frame); }

int main() {
  const auto nonce = std::chrono::steady_clock::now().time_since_epoch().count();
  Store store{fs::temp_directory_path() / ("pala-audit-" + std::to_string(nonce) + ".bin")};
  uint32_t cookie = 0;
  constexpr uint32_t token = 87;
  unsigned restarts = 0;
  auto confirm = [&](NoteRestartMode mode, bool yes, bool armWorks = true) {
    RestartConfirmation prompts;
    assert(prompts.request(mode, 100));
    assert(!prompts.answer({mode, 0}, 101));
    assert(prompts.pending(101).mode == mode);
    assert(prompts.answer(prompts.pending(101), 102));
    assert(!prompts.answer(prompts.pending(101), 103));
    return executeConfirmedRestart(mode, yes, cookie, token, [&] {
      return armWorks && store.write({AuditState::Running});
    }, [&] { ++restarts; });
  };
  auto boot = [&](bool software, bool canWrite = true) {
    const bool armed = cookie == token;
    cookie = 0;  // Same early consumption as the firmware.
    auto report = store.read();
    return claimAuditBoot(armed, software, report, [&](const Report& updated) {
      return canWrite && store.write(updated);
    });
  };

  for (auto mode : {NoteRestartMode::Restart, NoteRestartMode::Audit}) assert(confirm(mode, false));
  assert(restarts == 0 && !cookie);
  assert(!confirm(NoteRestartMode::Audit, true, false) && !restarts && !cookie);
  assert(confirm(NoteRestartMode::Restart, true) && !boot(true));
  assert(confirm(NoteRestartMode::Audit, true));
  assert(!boot(false) && store.read().state == AuditState::Incomplete); // Cut before boot.
  assert(!boot(true));
  assert(confirm(NoteRestartMode::Audit, true));
  assert(!boot(true, false));
  assert(visibleAuditState(store.read().state, false) == AuditState::Incomplete);
  assert(!boot(true)); // Even an unexpected software reset cannot reuse a consumed request.

  const P stages[]{P::LoadCatalog, P::Trash, P::LoadQueue, P::CheckCatalog, P::Discovering, P::Checking,
      P::ScanOrphans, P::TaskState, P::AgendaKeys, P::AgendaStates, P::SortStates, P::SortAgenda,
      P::AgendaSources, P::QueueSources, P::BuildAgenda, P::BuildQueue, P::VerifyAgenda,
      P::VerifyQueue, P::VerifyKeys, P::Saving};
  for (auto failAt : stages) {
    assert(confirm(NoteRestartMode::Audit, true) && boot(true));
    assert(store.read().state == AuditState::Incomplete);
    AuditDisplay display(0, capture);
    uint32_t now = 0;
    frames.clear();
    for (auto stage : stages) {
      display.update(now, stage, 0, 50000, 0);
      display.update(now + 5, stage, 1, 50000, 0);
      display.update(now + 3100, stage, 50000, 50000, 0);
      now += 6200;
      if (stage == failAt) break; // No cleanup and no final result write after power loss.
    }
    assert(frames.back().phase == failAt && frames.back().elapsedSeconds > 0);
    for (size_t i = 0; i < frames.size(); ++i) {
      const auto& frame = frames[i];
      if (i) assert(frame.elapsedSeconds >= frames[i - 1].elapsedSeconds + 3);
      char text[64];
      auditCountText(frame, text, sizeof(text));
      assert(std::strstr(text, "50000 / 50000") == nullptr);
      assert(auditBarPercent(frame) < 100);
    }
    assert(!boot(true) && store.read().state == AuditState::Incomplete);
    assert(confirm(NoteRestartMode::Restart, true) && !boot(true));
    assert(store.read().state == AuditState::Incomplete);
  }

  assert(confirm(NoteRestartMode::Audit, true) && boot(true));
  auto report = store.read();
  assert(!finishAudit(false, report, [&](auto r) { return store.write(r); }));
  assert(!finishAudit(true, report, [&](auto r) { return r.state != AuditState::Complete && store.write(r); }));
  assert(store.read().state == AuditState::Incomplete && !boot(true));
  // A write accepted by flash but failed during readback must not emit a
  // finished frame. The fallback publishes the incomplete report again.
  assert(!finishAudit(true, report, [&](auto r) {
    const bool wrote = store.write(r);
    return wrote && r.state != AuditState::Complete;
  }));
  assert(store.read().state == AuditState::Incomplete);
  assert(confirm(NoteRestartMode::Audit, true) && boot(true));
  frames.clear();
  AuditDisplay display(0, capture);
  display.update(0, P::LoadCatalog, 0, 0, 0);
  display.update(1, P::Saving, 50000, 0, 0, true);
  const bool saved = finishAudit(true, report, [&](auto r) {
    assert(frames.back().phase == P::Saving);
    return store.write(r);
  });
  assert(saved && store.read().state == AuditState::Complete);
  display.update(2, saved ? P::Finished : P::Incomplete, 50000, 50000, 0, true);
  assert(auditBarPercent(frames.back()) == 100);
  char text[64];
  auditCountText(frames.back(), text, sizeof(text));
  assert(std::strcmp(text, "50000 / 50000 notes") == 0);
  assert(!boot(true) && store.read().state == AuditState::Complete);
  fs::remove(store.path);
  std::puts("20 audit phase power cuts, confirmation, report I/O failures and throttled e-paper frames passed.");
}
