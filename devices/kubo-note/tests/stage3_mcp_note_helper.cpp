#include "../pala_note/src/app/capture_wav_recovery.h"
#include "../pala_note/src/app/catalog_storage.h"
#include "../pala_note/src/app/monthly_recordings.h"
#include "../pala_note/src/app/recorder_work.h"

#include <iostream>
#include <string>
#include <string_view>

int main(int argc, char** argv) {
  if (argc < 2) return 2;
  const std::string_view command = argv[1];
  if (argc == 4 && command == "claim") {
    using recorder_work::Claim;
    switch (recorder_work::claim(argv[2], argv[3])) {
      case Claim::Granted: std::cout << "write"; return 0;
      case Claim::Reconcile: std::cout << "reconcile"; return 0;
      case Claim::Conflict: std::cout << "conflict"; return 0;
      case Claim::Error: return 1;
    }
  }
  if (argc == 5 && command == "complete")
    return recorder_work::complete(argv[2], argv[3], argv[4]) ? 0 : 1;
  if (argc == 4 && command == "check-wave") {
    capture_wav::Info info;
    return capture_wav::inspect(argv[2], info, std::fopen) == capture_wav::State::Ready &&
        info.pcm / 2 == std::stoul(argv[3]) ? 0 : 1;
  }
  if (argc == 5 && command == "check-job") {
    note_catalog::Reservation record;
    return note_catalog::readReservation(argv[2], record) == note_catalog::ReservationState::Valid &&
        record.id == std::stoi(argv[3]) && std::string_view(record.utc) == argv[4] &&
        monthly_recordings::makeId(monthly_recordings::sequence(record.id), record.utc) == record.id ? 0 : 1;
  }
  return 2;
}
