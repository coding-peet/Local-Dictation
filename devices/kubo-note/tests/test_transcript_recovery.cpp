#include "../pala_note/src/app/transcript_storage.h"
#include <cassert>
#include <chrono>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <iterator>

namespace fs = std::filesystem;
using namespace note_transcript;

std::string bytes(const char* path) {
  std::ifstream file(path, std::ios::binary);
  return {std::istreambuf_iterator<char>(file), std::istreambuf_iterator<char>()};
}

int main() {
  const auto before = fs::current_path();
  const auto nonce = std::chrono::steady_clock::now().time_since_epoch().count();
  const auto temp = fs::temp_directory_path() / ("pala-transcript-" + std::to_string(nonce));
  assert(fs::create_directory(temp));
  fs::current_path(temp);

  const char* journal = "transcript.pending";
  const char* path = "note_017.txt";
  const std::string original = " \xC4\x8E" "akujem\r\noriginal bytes  \n";
  unsigned catalogWrites = 0, uploads = 0;
  std::string route = "transcribing";
  bool allowCatalog = true;
  auto replayNote = [&] {
    return replay(journal, [&](int id) {
      assert(id == 17);
      return repairCommitted(path, [&] {
        if (!allowCatalog) return false;
        ++catalogWrites;
        return true;
      }, [&] { return route; }, [&] { route = "organizing"; return true; });
    });
  };

  // Before the text rename, a partial uncommitted temp remains eligible for ASR.
  assert(reserve(journal, 17));
  assert(writeClosed("note_017.txt.tmp", "partial", 7));
  assert(replayNote() && catalogWrites == 0 && !fs::exists(journal));
  assert(canSendAudio(inspect(path), false));
  assert(useCommittedOrTranscribe(inspect(path), [] { return false; }, [&] { ++uploads; return true; }));
  assert(uploads == 1);

  // After the rename, a crashed or failed catalog write must be replayable.
  assert(reserve(journal, 17));
  assert(storeOnce(path, original));
  allowCatalog = false;
  assert(!replayNote() && fs::exists(journal));
  assert(!canSendAudio(inspect(path), false));  // USB and BLE WAV selection
  assert(!useCommittedOrTranscribe(inspect(path), replayNote, [&] { ++uploads; return true; })); // Wi-Fi
  assert(!storeOnce(path, "a later model result") && bytes(path) == original);
  allowCatalog = true;
  assert(replayNote() && catalogWrites == 1 && route == "organizing");
  assert(bytes(path) == original && !fs::exists(journal));

  // Old FAT interrupted-rename state. A local-only route survives every retry.
  fs::rename(path, "note_017.txt.bak");
  route = "usb-local";
  assert(reserve(journal, 17));
  assert(!canSendAudio(inspect(path), false));
  assert(!storeOnce(path, "conflicting replay"));
  assert(storeOnce(path, original) && bytes("note_017.txt.bak") == original);
  assert(useCommittedOrTranscribe(inspect(path), replayNote, [&] { ++uploads; return true; }));
  assert(uploads == 1 && !fs::exists(journal) && bytes(path) == original);
  assert(catalogWrites == 2 && route == "usb-local");

  // A directory where a regular transcript was expected must inhibit all transports.
  fs::rename(path, "saved-for-test");
  assert(fs::create_directory(path));
  assert(inspect(path) == Stored::Error && !canSendAudio(inspect(path), false));
  assert(!useCommittedOrTranscribe(inspect(path), replayNote, [&] { ++uploads; return true; }));
  assert(!storeOnce(path, original) && uploads == 1);
  fs::current_path(before);
  fs::remove_all(temp);
  std::cout << "Transcript publication, failed catalog write, replay, backup and transport guards passed.\n";
}
