#include "../pala_note/src/app/recorder_work.h"
#include "../pala_note/src/app/catalog_storage.h"

#include <cassert>
#include <chrono>
#include <filesystem>
#include <fstream>
#include <vector>

namespace fs = std::filesystem;
using recorder_work::Claim;

int main() {
  const auto root = fs::temp_directory_path() / ("pala-work-" +
      std::to_string(std::chrono::steady_clock::now().time_since_epoch().count()));
  const auto pending = root / ".pala-work";
  const auto archive = root / "notes" / "202611";
  assert(fs::create_directories(pending));
  assert(fs::create_directories(archive));
  const std::string a(64, 'a'), b(64, 'b');
  const auto job = (pending / "111.job").string();
  const auto claim = (pending / "111.claim").string();
  const auto receipt = (archive / "111.delivery").string();
  constexpr char stamp[] = "2026-11-25T12:30:00Z";

  assert(note_catalog::reserveRecording(job, 111, stamp));
  note_catalog::Reservation recovered;
  assert(note_catalog::readReservation(job.c_str(), recovered) == note_catalog::ReservationState::Valid);
  assert(recovered.id == 111 && std::string(recovered.utc) == stamp && fs::exists(job));
  assert(!recorder_work::complete(claim, receipt, a));
  assert(recorder_work::claim(claim, a) == Claim::Granted);
  assert(recorder_work::claim(claim, a) == Claim::Reconcile);  // even if grant reply was lost
  assert(recorder_work::claim(claim, b) == Claim::Conflict);
  assert(!recorder_work::complete(claim, receipt, b));
  assert(recorder_work::complete(claim, receipt, a));
  assert(fs::exists(job) && fs::exists(receipt));  // power loss before cleanup
  assert(fs::remove(claim));
  assert(recorder_work::complete(claim, receipt, a));  // ack replay after cleanup
  assert(!recorder_work::complete(claim, receipt, b));

  const auto plan = (pending / "111.plan").string();
  std::string chosen;
  assert(recorder_work::plan(plan, claim, "", chosen) && chosen.empty());
  assert(recorder_work::plan(plan, claim, "V2.YWJj", chosen) && chosen == "V2.YWJj");
  assert(recorder_work::claim(claim, a) == Claim::Granted);
  assert(recorder_work::plan(plan, claim, "", chosen) && chosen == "V2.YWJj");
  assert(recorder_work::plan(plan, claim, "V2.ZGVm", chosen) && chosen == "V2.YWJj");
  assert(!recorder_work::plan(plan, claim, "V2.!", chosen));
  assert(fs::remove(plan));
  assert(recorder_work::plan(plan, claim, "", chosen) && chosen == "LEGACY");
  assert(recorder_work::plan(plan, claim, "V2", chosen) && chosen == "LEGACY");
  assert(!fs::exists(plan));

  const auto blocked = pending / "222.claim";
  assert(fs::create_directory(blocked));
  assert(recorder_work::claim(blocked.string(), a) == Claim::Error);
  assert(fs::is_directory(blocked));
  assert(fs::remove(job));
  assert(note_catalog::readReservation(job.c_str(), recovered) == note_catalog::ReservationState::Missing);
  fs::remove_all(root);
}
