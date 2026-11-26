#include "../pala_note/src/app/monthly_recordings.h"
#include <cassert>
#include <string>

static std::string path(int id, const char* extension, const char* root = "/notes") {
  char result[96];
  monthly_recordings::path(result, sizeof(result), root, id, extension);
  return result;
}

int main() {
  using monthly_recordings::makeId;
  const int november = makeId(1, "2026-11-30T23:59:59Z");
  const int december = makeId(2, "2026-12-01T00:00:00Z");
  const int rollback = makeId(3, "2026-11-30T12:00:00Z");
  const int clockUnset = makeId(4, "");
  assert(0 < november && november < december && december < rollback && rollback < clockUnset);
  assert(path(november, "wav") == "/notes/202611/note_" + std::to_string(november) + ".wav");
  assert(path(december, "txt.bak", "/sdcard/notes") ==
      "/sdcard/notes/202612/note_" + std::to_string(december) + ".txt.bak");
  assert(path(rollback, "wav").find("/202611/") != std::string::npos);
  assert(path(clockUnset, "txt").find("/000000/") != std::string::npos);
  assert(path(november, "queue") == "/notes/note_" + std::to_string(november) + ".queue");
  assert(path(42, "wav") == "/notes/note_042.wav");
  assert(path(42, "txt.bak") == "/notes/note_042.txt.bak");
  assert(monthly_recordings::sequence(42) == 0);
  assert(monthly_recordings::sequence(clockUnset) == 4);
  assert(makeId(0, "") == 0);
  assert(makeId(monthly_recordings::maxSequence + 1, "") == 0);
  assert(makeId(5, "2026-02-30T00:00:00Z") == 0);
  assert(path(makeId(monthly_recordings::maxSequence, "2100-12-31T23:59:59Z"), "wav")
      .find("/210012/") != std::string::npos);

  // Mixed Test 1 and Test 2 card after restart, with a later RTC rollback.
  monthly_recordings::firstNewSequence = 2;
  assert(path(42, "wav") == "/notes/note_042.wav");
  assert(path(november, "wav").find("/notes/202611/") == 0);
  assert(path(november, "moments").find("/notes/note_") == 0);
  assert(path(december, "wav").find("/recordings/202612/") == 0);
  assert(path(december, "queue").find("/notes/note_") == 0);
  assert(path(december, "txt.bak", "/sdcard/notes").find("/sdcard/recordings/202612/") == 0);
  assert(path(rollback, "wav.meta", "/sdcard/notes").find("/sdcard/recordings/202611/") == 0);
  assert(path(rollback, "moments").find("/recordings/202611/") == 0);
  assert(path(rollback, "delivery").find("/recordings/202611/") == 0);
  assert(path(clockUnset, "wav").find("/recordings/000000/") == 0);
}
