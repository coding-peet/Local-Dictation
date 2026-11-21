#include "note_trash_policy.h"

#include <array>
#include <string_view>

using namespace note_trash;

static_assert(fileNumber("note_001.wav") == 1);
static_assert(fileNumber("note_573.brain.json.bak") == 573);
static_assert(fileNumber("note_2147483647.bridge-owner.tmp") == 2147483647);
static_assert(fileNumber("note_2147483648.wav") == 0);
static_assert(fileNumber("note_001.custom.txt") == 0);
static_assert(fileNumber("note_001.wav.bak.bak") == 0);
static_assert(fileNumber("note_001/other.wav") == 0);
static_assert(fileNumber("note_0001.wav") == 0);
static_assert(folderNumber("note_274") == 274);
static_assert(folderNumber("note_123.bak") == 0);
static_assert(validRetention(1) && validRetention(3650));
static_assert(!validRetention(0) && !validRetention(3651));
static_assert(!expired(0, 1705000000, 90));
static_assert(!expired(1705000000, 0, 90));
static_assert(!expired(1705000000, 1704000000, 90));
static_assert(!expired(1705000000, 1705000000 + 90*86400 - 1, 90));
static_assert(expired(1705000000, 1705000000 + 90*86400, 90));

struct Card {
  std::array<bool, 4> existsAt{true, true, false, false};
  bool failMetadata = true;
  constexpr bool exists(const char* path) const { return existsAt[path[0] - 'a']; }
  constexpr bool rename(const char* src, const char* dst) {
    if (failMetadata && src[0] == 'b') return false;
    existsAt[src[0]-'a'] = false;
    existsAt[dst[0]-'a'] = true;
    return true;
  }
};
constexpr bool interruptedMoveAndRestore() {
  Card card;
  if (!relocate(card, "a", "c") || relocate(card, "b", "d")) return false;
  if (card.exists("a") || !card.exists("b") || !card.exists("c") || card.exists("d")) return false;
  card.failMetadata = false;
  if (!relocate(card, "a", "c") || !relocate(card, "b", "d")) return false;
  card.existsAt[0] = true; // A foreign target must survive restore.
  if (relocate(card, "c", "a") || !card.exists("c")) return false;
  card.existsAt[0] = false;
  return relocate(card, "c", "a") && relocate(card, "d", "b") && card.exists("a")
      && card.exists("b") && !card.exists("c") && !card.exists("d");
}
static_assert(interruptedMoveAndRestore());
static_assert(fileNumber("NOTE_024.WAV.BAK") == 24);
static_assert(retentionStart(0, 0) == 0);
static_assert(retentionStart(0, 1705000000) == 1705000000);
static_assert(retentionStart(1706000000, 1705000000) == 1706000000);
static_assert(!clearable(Phase::Moving) && !clearable(Phase::Restoring));
static_assert(clearable(Phase::Parked) && clearable(Phase::Purging));
