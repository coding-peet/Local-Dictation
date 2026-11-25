#include "agenda_index.h"
#include "agenda_index_order.h"
#include "audit_projection.h"
#include "audit_progress.h"
#include "notes.h"
#include "storage_commit.h"
#include "../../config.h"
#include "../../globals.h"

#include <ArduinoJson.h>
#include <SD_MMC.h>
#include <array>
#include <cstring>

namespace {
using agenda_index::Key;
constexpr const char* kManifest = NOTES_DIR "/.agenda-v1.commit";
constexpr const char* kJournal = NOTES_DIR "/.agenda-v1.pending";
constexpr const char* kUnsorted = NOTES_DIR "/.agenda-v1.unsorted";
constexpr const char* kSortA = NOTES_DIR "/.agenda-v1.sort-a";
constexpr const char* kSortB = NOTES_DIR "/.agenda-v1.sort-b";
constexpr const char* kStateRows = NOTES_DIR "/.agenda-v1.states";
constexpr const char* kStateKeys = NOTES_DIR "/.agenda-v1.state-keys";
constexpr uint32_t kMagic = 0x31444741;
constexpr uint32_t kRunWidth = 64;

struct Manifest {
  uint32_t magic = kMagic;
  uint32_t dataSlot = 0;
  uint32_t keySlot = 0;
  uint32_t journalBytes = 0;
  uint32_t count = 0;
  uint32_t tasks = 0;
  uint32_t open = 0;
};

enum class Phase { Idle, States, Build, Runs, Merge, Publish, ChangeStates, Recent, Due };
struct Work {
  Manifest target;
  Phase phase = Phase::Idle;
  int note = 0;
  uint32_t cursor = 0;
  uint32_t width = kRunWidth;
  uint32_t addedAt = 0;
  uint32_t stateOffset = 0;
  uint32_t stateCount = 0;
  uint32_t stateEpoch = 0;
  uint32_t sortCount = 0;
  uint32_t sortPasses = 0, sortPass = 0;
  bool sortingStates = false;
  const char* sorted = kSortA;
  agenda_index::MergeRun run;
  std::vector<Key> added;
  std::vector<BrainTaskRecord> tasks;
};
Manifest active;
Work work;
bool haveActive = false;
bool loaded = false;
bool pending = true;
bool forceRebuild = false;
bool verifyReplacement = false;
bool verifyAgendaSourceRows(const Manifest& target);
uint32_t generation = 1;
uint32_t retryAt = 0;

String dataPath(uint32_t slot) { return String(NOTES_DIR "/.agenda-v1.data-") + slot; }
String keyPath(uint32_t slot, bool recent) {
  return String(recent ? NOTES_DIR "/.agenda-v1.recent-" : NOTES_DIR "/.agenda-v1.due-") + slot;
}

uint32_t journalLength() {
  File file = SD_MMC.open(kJournal);
  return file ? file.size() : 0;
}

bool writeEmpty(const String& path) {
  File file = SD_MMC.open(path, FILE_WRITE);
  return file && file.size() == 0;
}

bool validKeyFile(const String& path, uint32_t count) {
  File file = SD_MMC.open(path);
  return file && file.size() == static_cast<uint64_t>(count) * sizeof(Key);
}

bool validManifest(const Manifest& candidate) {
  return candidate.magic == kMagic && candidate.dataSlot <= 1 && candidate.keySlot <= 1 &&
         SD_MMC.exists(dataPath(candidate.dataSlot)) &&
         validKeyFile(keyPath(candidate.keySlot, true), candidate.count) &&
         validKeyFile(keyPath(candidate.keySlot, false), candidate.count);
}

void loadManifest() {
  if (loaded) return;
  loaded = true;
  recoverStoredFile(SD_MMC, String(kManifest));
  File file = SD_MMC.open(kManifest);
  haveActive = file && file.size() == sizeof(active) &&
      file.read(reinterpret_cast<uint8_t*>(&active), sizeof(active)) == sizeof(active) &&
      validManifest(active);
  pending = !haveActive || active.journalBytes != journalLength();
}

bool publish() {
  if (!validManifest(work.target)) return false;
  const String temp = String(kManifest) + ".tmp";
  File file = SD_MMC.open(temp, FILE_WRITE);
  if (!file) return false;
  const bool written = file.write(reinterpret_cast<const uint8_t*>(&work.target), sizeof(active)) == sizeof(active);
  file.flush();
  const bool complete = written && file.size() == sizeof(active);
  file.close();
  if (!complete || !commitStoredFile(SD_MMC, String(kManifest), temp)) return false;
  verifyReplacement = false;
  active = work.target;
  haveActive = true;
  work = Work{};
  pending = active.journalBytes != journalLength();
  ++generation;
  return true;
}

bool readKey(File& file, uint32_t index, Key& key) {
  const uint64_t offset = static_cast<uint64_t>(index) * sizeof(Key);
  return offset < UINT32_MAX &&
      (file.position() == offset || file.seek(offset)) &&
      file.read(reinterpret_cast<uint8_t*>(&key), sizeof(Key)) == sizeof(Key) &&
      key.note > 0 && key.note <= INT32_MAX && key.due[20] == '\0';
}

bool writeKey(File& file, const Key& key) {
  return file.write(reinterpret_cast<const uint8_t*>(&key), sizeof(key)) == sizeof(key);
}

void countTask(const Key& key) {
  if (!(key.flags & 1)) {
    ++work.target.tasks;
    if (!(key.flags & 2)) ++work.target.open;
  }
}

bool appendNote(const std::vector<BrainTaskRecord>& tasks, uint32_t slot, std::vector<Key>& keys) {
  if (tasks.empty()) return true;
  File data = SD_MMC.open(dataPath(slot), FILE_APPEND);
  if (!data) return false;
  uint32_t ordinal = 0;
  size_t offset = data.size();
  for (const auto& task : tasks) {
    JsonDocument row;
    row["note"] = task.noteNum;
    row["ordinal"] = ordinal;
    row["id"] = task.id;
    row["text"] = task.text;
    row["created"] = task.createdUtc;
    row["due"] = task.dueUtc;
    row["type"] = task.type;
    row["precision"] = task.datePrecision;
    row["updated"] = task.updatedUtc;
    row["done"] = task.done;
    const auto length = measureJson(row);
    if (row.overflowed() || length > UINT32_MAX - 1 || offset >= UINT32_MAX - length - 1) return false;
    Key key{};
    key.note = task.noteNum;
    key.ordinal = ordinal++;
    key.offset = offset;
    key.flags = (task.type == "event" ? 1 : 0) | (task.done ? 2 : 0);
    std::strncpy(key.due, task.dueUtc.c_str(), sizeof(key.due) - 1);
    if (serializeJson(row, data) != length || data.print('\n') != 1) return false;
    keys.push_back(key);
    offset += length + 1;
  }
  data.flush();
  return data.size() == offset;
}

bool startFull(uint32_t journalBytes) {
  work = Work{};
  work.target.dataSlot = haveActive ? 1 - active.dataSlot : 0;
  work.target.keySlot = haveActive ? 1 - active.keySlot : 0;
  work.target.journalBytes = journalBytes;
  if (!writeEmpty(dataPath(work.target.dataSlot)) ||
      !writeEmpty(keyPath(work.target.keySlot, true)) || !writeEmpty(kUnsorted)) return false;
  work.phase = Phase::States;
  return writeEmpty(kStateRows);
}

uint32_t stateHash(const String& id) {
  uint32_t hash = 2166136261U;
  for (size_t i = 0; i < id.length(); ++i)
    hash = (hash ^ static_cast<uint8_t>(id[i])) * 16777619U;
  hash &= INT32_MAX;
  return hash ? hash : 1;
}

struct StateFiles { File& rows; File& keys; size_t offset; };

bool appendState(const String& id, bool done, const String& updated, void* context) {
  auto& files = *static_cast<StateFiles*>(context);
  auto& rows = files.rows;
  Key key{};
  key.note = stateHash(id);
  key.ordinal = work.stateCount;
  key.offset = files.offset;
  JsonDocument row;
  row["id"] = id;
  row["done"] = done;
  row["updated"] = updated;
  const auto length = measureJson(row);
  if (row.overflowed() || length >= UINT32_MAX - files.offset - 1 ||
      serializeJson(row, rows) != length || rows.print('\n') != 1 ||
      !writeKey(files.keys, key)) return false;
  files.offset += length + 1;
  ++work.stateCount;
  return true;
}

bool beginSort(uint32_t count, bool states) {
  work.phase = Phase::Runs;
  work.cursor = 0;
  work.width = kRunWidth;
  work.sorted = kSortA;
  work.sortCount = count;
  work.sortPass = 0;
  work.sortPasses = 1;
  for (uint64_t width = kRunWidth; width < count; width *= 2) ++work.sortPasses;
  work.sortingStates = states;
  return writeEmpty(kSortA);
}

bool buildStates() {
  if (work.stateEpoch != generation) {
    // A completion edit rewrites the source file. Take a fresh consistent
    // copy if its byte offsets could have shifted between slices.
    work.stateEpoch = generation;
    work.stateOffset = work.stateCount = 0;
    if (!writeEmpty(kStateRows) || !writeEmpty(kUnsorted)) return false;
  }
  noteAuditWork(NoteStorageAuditPhase::AgendaStates, work.stateCount);
  File rows = SD_MMC.open(kStateRows, FILE_APPEND);
  File keys = SD_MMC.open(kUnsorted, FILE_APPEND);
  if (!rows || !keys) return false;
  StateFiles files{rows, keys, rows.size()};
  bool end = false;
  if (!visitAgendaStateBatch(work.stateOffset, 32, end, appendState, &files)) return false;
  rows.flush();
  if (rows.size() != files.offset) return false;
  return !end || beginSort(work.stateCount, true);
}

bool applyStoredStates(std::vector<BrainTaskRecord>& tasks) {
  if (!work.stateCount || tasks.empty()) return true;
  File keys = SD_MMC.open(kStateKeys);
  File rows = SD_MMC.open(kStateRows);
  if (!keys || !rows) return false;
  for (auto& task : tasks) {
    const uint32_t hash = stateHash(task.id);
    uint32_t low = 0, high = work.stateCount;
    while (low < high) {
      const uint32_t mid = low + (high - low) / 2;
      Key key{};
      if (!readKey(keys, mid, key)) return false;
      if (key.note < hash) low = mid + 1;
      else high = mid;
    }
    for (uint32_t i = low; i < work.stateCount; ++i) {
      Key key{};
      if (!readKey(keys, i, key)) return false;
      if (key.note != hash) break;
      JsonDocument row;
      if (!rows.seek(key.offset) || deserializeJson(row, rows) || !row["id"].is<const char*>()) return false;
      if (task.id != row["id"].as<const char*>()) continue;
      task.done = row["done"] | false;
      task.updatedUtc = row["updated"] | "";
    }
  }
  return true;
}

bool buildNote() {
  const auto note = std::upper_bound(noteIndex.begin(), noteIndex.end(), work.note,
      [](int num, const NoteEntry& entry) { return num < entry.num; });
  noteAuditWork(NoteStorageAuditPhase::BuildAgenda, static_cast<uint32_t>(note - noteIndex.begin()), noteIndex.size());
  if (note == noteIndex.end()) return beginSort(work.target.count, false);
  work.note = note->num;
  std::vector<BrainTaskRecord> tasks;
  if (!loadAgendaTasksForNote(note->num, tasks)) return false;
  std::vector<Key> keys;
  if (!applyStoredStates(tasks) || !appendNote(tasks, work.target.dataSlot, keys)) return false;
  if (keys.empty()) return true;
  File recent = SD_MMC.open(keyPath(work.target.keySlot, true), FILE_APPEND);
  File unsorted = SD_MMC.open(kUnsorted, FILE_APPEND);
  if (!recent || !unsorted) return false;
  for (const auto& key : keys) {
    if (!writeKey(recent, key) || !writeKey(unsorted, key)) return false;
    countTask(key);
  }
  work.target.count += keys.size();
  return true;
}

void reportSortProgress(uint32_t position) {
  const uint64_t total = uint64_t{work.sortCount} * work.sortPasses;
  const uint64_t done = uint64_t{work.sortCount} * work.sortPass + position;
  noteAuditWork(work.sortingStates ? NoteStorageAuditPhase::SortStates : NoteStorageAuditPhase::SortAgenda,
                static_cast<uint32_t>(std::min<uint64_t>(done, UINT32_MAX)),
                static_cast<uint32_t>(std::min<uint64_t>(total, UINT32_MAX)));
}

bool makeRun() {
  reportSortProgress(work.cursor);
  if (work.cursor == work.sortCount) {
    work.cursor = 0;
    work.run.begin(0, work.width, work.sortCount);
    if (work.width >= work.sortCount) {
      work.phase = Phase::Publish;
      return true;
    }
    work.phase = Phase::Merge;
    ++work.sortPass;
    return writeEmpty(kSortB);
  }
  std::array<Key, kRunWidth> keys{};
  const uint32_t length = std::min(kRunWidth, work.sortCount - work.cursor);
  File input = SD_MMC.open(kUnsorted);
  File output = SD_MMC.open(kSortA, FILE_APPEND);
  if (!input || !output) return false;
  for (uint32_t i = 0; i < length; ++i) if (!readKey(input, work.cursor + i, keys[i])) return false;
  std::sort(keys.begin(), keys.begin() + length, agenda_index::dueBefore);
  if (output.write(reinterpret_cast<const uint8_t*>(keys.data()), length * sizeof(Key)) != length * sizeof(Key))
    return false;
  work.cursor += length;
  return true;
}

bool mergeRun() {
  reportSortProgress(work.run.left + (work.run.right - work.run.endLeft));
  const char* outputPath = work.sorted == kSortA ? kSortB : kSortA;
  if (work.run.done()) {
    work.cursor = work.run.endRight;
    if (work.cursor == work.sortCount) {
      work.sorted = outputPath;
      work.width *= 2;
      ++work.sortPass;
      if (work.width >= work.sortCount) {
        work.phase = Phase::Publish;
        return true;
      }
      work.cursor = 0;
      if (!writeEmpty(work.sorted == kSortA ? kSortB : kSortA)) return false;
      work.run.begin(0, work.width, work.sortCount);
      return true;
    }
    work.run.begin(work.cursor, work.width, work.sortCount);
  }
  File left = SD_MMC.open(work.sorted), right = SD_MMC.open(work.sorted);
  File output = SD_MMC.open(outputPath, FILE_APPEND);
  return left && right && output && work.run.step(kRunWidth,
      [&](uint32_t i, Key& key) { return readKey(left, i, key); },
      [&](uint32_t i, Key& key) { return readKey(right, i, key); },
      [&](const Key& key) { return writeKey(output, key); });
}

bool startIncremental(int note, uint32_t consumed) {
  work = Work{};
  work.note = note;
  work.target = active;
  work.target.keySlot = 1 - active.keySlot;
  work.target.journalBytes = consumed;
  work.target.count = work.target.tasks = work.target.open = 0;
  work.phase = Phase::ChangeStates;
  work.stateEpoch = generation;
  const auto entry = std::lower_bound(noteIndex.begin(), noteIndex.end(), note,
      [](const NoteEntry& a, int number) { return a.num < number; });
  const bool live = entry != noteIndex.end() && entry->num == note;
  if (live && !loadAgendaTasksForNote(note, work.tasks)) return false;
  return writeEmpty(keyPath(work.target.keySlot, true)) &&
         writeEmpty(keyPath(work.target.keySlot, false));
}

bool overlayChangedState(const String& id, bool done, const String& updated, void*) {
  for (auto& task : work.tasks) {
    if (task.id != id) continue;
    task.done = done;
    task.updatedUtc = updated;
  }
  return true;
}

bool finishIncrementalNote() {
  if (work.stateEpoch != generation) return startIncremental(work.note, work.target.journalBytes);
  if (!work.tasks.empty()) {
    bool end = false;
    if (!visitAgendaStateBatch(work.stateOffset, 64, end, overlayChangedState, nullptr)) return false;
    if (!end) return true;
    if (!appendNote(work.tasks, active.dataSlot, work.added)) return false;
    std::vector<BrainTaskRecord>().swap(work.tasks);
  }
  work.phase = Phase::Recent;
  return true;
}

bool replaceNoteKeys(bool recent) {
  File input = SD_MMC.open(keyPath(active.keySlot, recent));
  File output = SD_MMC.open(keyPath(work.target.keySlot, recent), FILE_APPEND);
  if (!input || !output) return false;
  const auto before = recent ? agenda_index::recentBefore : agenda_index::dueBefore;
  uint32_t budget = kRunWidth;
  while (budget && (work.cursor < active.count || work.addedAt < work.added.size())) {
    Key key{};
    const bool hasOld = work.cursor < active.count;
    if (hasOld && !readKey(input, work.cursor, key)) return false;
    if (hasOld && key.note == static_cast<uint32_t>(work.note)) {
      ++work.cursor;
    } else if (work.addedAt < work.added.size() &&
               (!hasOld || before(work.added[work.addedAt], key))) {
      const auto& added = work.added[work.addedAt++];
      if (!writeKey(output, added)) return false;
      if (recent) countTask(added);
    } else {
      if (!writeKey(output, key)) return false;
      if (recent) countTask(key);
      ++work.cursor;
    }
    --budget;
  }
  if (work.cursor < active.count || work.addedAt < work.added.size()) return true;
  output.flush();
  const uint32_t count = output.size() / sizeof(Key);
  output.close();
  if (!recent) return count == work.target.count && publish();
  work.target.count = count;
  work.cursor = work.addedAt = 0;
  std::sort(work.added.begin(), work.added.end(), agenda_index::dueBefore);
  work.phase = Phase::Due;
  return true;
}

bool advance() {
  switch (work.phase) {
    case Phase::States: return buildStates();
    case Phase::Build: return buildNote();
    case Phase::ChangeStates: return finishIncrementalNote();
    case Phase::Runs: return makeRun();
    case Phase::Merge: return mergeRun();
    case Phase::Publish: {
      const String destination = work.sortingStates ? String(kStateKeys) : keyPath(work.target.keySlot, false);
      SD_MMC.remove(destination);
      if (!SD_MMC.rename(work.sorted, destination)) return false;
      SD_MMC.remove(work.sorted == kSortA ? kSortB : kSortA);
      if (work.sortingStates) {
        work.phase = Phase::Build;
        return writeEmpty(kUnsorted);
      }
      SD_MMC.remove(kUnsorted);
      if (verifyReplacement && !verifyAgendaSourceRows(work.target)) return false;
      SD_MMC.remove(kStateKeys);
      SD_MMC.remove(kStateRows);
      return publish();
    }
    case Phase::Recent: return replaceNoteKeys(true);
    case Phase::Due: return replaceNoteKeys(false);
    case Phase::Idle: break;
  }
  const uint32_t end = journalLength();
  if (forceRebuild || !haveActive || active.journalBytes > end) {
    if (!startFull(end)) return false;
    forceRebuild = false;
    return true;
  }
  if (active.journalBytes == end) { pending = false; return true; }
  File journal = SD_MMC.open(kJournal);
  std::array<uint32_t, 2> change{};
  if (!journal || !journal.seek(active.journalBytes) ||
      journal.read(reinterpret_cast<uint8_t*>(change.data()), sizeof(change)) != sizeof(change) ||
      change[0] > INT32_MAX || change[1] != (change[0] ^ kMagic) || !change[0])
    return startFull(end);
  uint32_t consumed = active.journalBytes + sizeof(change);
  // Several source updates for the same note often form one operation.
  // Collapse adjacent journal entries before rewriting the key streams.
  for (unsigned i = 1; i < kRunWidth && consumed + sizeof(change) <= end; ++i) {
    std::array<uint32_t, 2> next{};
    if (journal.read(reinterpret_cast<uint8_t*>(next.data()), sizeof(next)) != sizeof(next) ||
        next != change) break;
    consumed += sizeof(next);
  }
  journal.close();
  return startIncremental(change[0], consumed);
}

void indexFailure() {
  if (work.phase == Phase::Recent || work.phase == Phase::Due) forceRebuild = true;
  work = Work{};
  retryAt = millis() + 5000;
  pending = true;
  ++generation;
}
}  // namespace

bool beginAgendaIndexChange(int noteNum) {
  if (noteNum < 0) return false;
  const uint32_t note = noteNum;
  const uint32_t change[] = {note, note ^ kMagic};
  File file = SD_MMC.open(kJournal, FILE_APPEND);
  if (!file) return false;
  const size_t oldSize = file.size();
  pending = true;
  ++generation;
  const bool written = file.write(reinterpret_cast<const uint8_t*>(change), sizeof(change)) == sizeof(change);
  file.flush();
  return written && file.size() == oldSize + sizeof(change);
}

void finishAgendaIndexChange(int) {
  // Source mutation is synchronous. The persisted dirty journal remains until
  // idle maintenance publishes a complete new index generation.
}

bool requestAgendaIndexRebuild(bool verifySources) {
  if (!beginAgendaIndexChange(0)) return false;
  work = Work{};
  retryAt = 0;
  forceRebuild = true;
  verifyReplacement |= verifySources;
  return true;
}

bool maintainAgendaIndex() {
  loadManifest();
  if (!pending) return true;
  if (retryAt && static_cast<int32_t>(millis() - retryAt) < 0) return false;
  retryAt = 0;
  if (advance()) return true;
  indexFailure();
  return false;
}

int agendaIndexRebuildProgress() {
  if (agendaIndexReady()) return 100;
  switch (work.phase) {
    case Phase::States: return 5;
    case Phase::Build: return noteIndex.empty() ? 60 :
        15 + static_cast<int>(45LL * work.note / noteIndex.back().num);
    case Phase::Runs: return work.sortingStates ? 10 :
        work.sortCount ? 60 + 10LL * work.cursor / work.sortCount : 70;
    case Phase::Merge: return work.sortingStates ? 12 : 70;
    case Phase::Publish: return work.sortingStates ? 14 : 99;
    case Phase::ChangeStates: return 3;
    case Phase::Recent: return active.count ? 5 + 43LL * work.cursor / active.count : 48;
    case Phase::Due: return active.count ? 50 + 48LL * work.cursor / active.count : 98;
    case Phase::Idle: return 0;
  }
  return 0;
}

bool agendaIndexReady() {
  loadManifest();
  return haveActive && !pending;
}

void agendaTaskCounts(uint32_t& total, uint32_t& open) {
  total = active.tasks;
  open = active.open;
}

uint32_t agendaIndexGeneration() { return generation; }

bool visitAgendaDates(AgendaIndexCursor& cursor, AgendaDateVisitor visitor, void* context,
                      uint32_t budget, bool& finished) {
  finished = false;
  if (cursor.recent || !agendaIndexReady() || cursor.generation != generation) return false;
  File keys = SD_MMC.open(keyPath(active.keySlot, false));
  if (!keys) return false;
  while (budget-- && cursor.next < cursor.end) {
    Key key{};
    if (!readKey(keys, cursor.next++, key)) return false;
    if (!visitor(key.due, key.flags & 1, context)) { finished = true; return true; }
  }
  finished = cursor.next == cursor.end;
  return true;
}

namespace {
bool readTaskPayload(File& data, const Key& key, JsonDocument& row) {
  if (key.offset >= data.size() || !data.seek(key.offset) || deserializeJson(row, data)) return false;
  const bool fields = row["id"].is<const char*>() && row["text"].is<const char*>() &&
      row["due"].is<const char*>() && row["type"].is<const char*>() && row["done"].is<bool>();
  if (!fields ||
      row["note"].as<uint32_t>() != key.note || row["ordinal"].as<uint32_t>() != key.ordinal ||
      std::strncmp(row["due"].as<const char*>(), key.due, sizeof(key.due) - 1)) return false;
  const uint8_t flags = (row["type"].as<String>() == "event" ? 1 : 0) |
                        (row["done"].as<bool>() ? 2 : 0);
  return key.flags == flags;
}

bool verifyAgendaSourceRows(const Manifest& target) {
  if (!noteCatalogBaseLoaded()) return false;
  File recent = SD_MMC.open(keyPath(target.keySlot, true));
  File data = SD_MMC.open(dataPath(target.dataSlot));
  if (!recent || !data) return false;
  std::vector<BrainTaskRecord> tasks;
  uint32_t examined = 0, taskCount = 0, openCount = 0;
  const bool exact = agendaMatchesSource(noteIndex, target.count,
      [&](int id, auto& rows) {
        noteAuditWork(&target == &active ? NoteStorageAuditPhase::AgendaSources : NoteStorageAuditPhase::VerifyAgenda,
                      examined, noteIndex.size());
        if ((++examined & 127) == 0) delay(1);
        return loadAgendaTasksForNote(id, rows) && applyStoredStates(rows);
      }, [&](uint32_t position, size_t ordinal, const BrainTaskRecord& task) {
        Key key{};
        JsonDocument row;
        if (!readKey(recent, position, key) || key.note != static_cast<uint32_t>(task.noteNum) ||
            key.ordinal != ordinal || !readTaskPayload(data, key, row)) return false;
        if (task.type != "event") { ++taskCount; if (!task.done) ++openCount; }
        return task.id == row["id"].as<const char*>() && task.text == row["text"].as<const char*>() &&
               task.type == row["type"].as<const char*>() && task.dueUtc == row["due"].as<const char*>() &&
               task.done == row["done"].as<bool>() && row["created"].is<const char*>() &&
               task.createdUtc == row["created"].as<const char*>() && row["updated"].is<const char*>() &&
               task.updatedUtc == row["updated"].as<const char*>() && row["precision"].is<const char*>() &&
               task.datePrecision == row["precision"].as<const char*>();
      }, tasks);
  return exact && taskCount == target.tasks && openCount == target.open;
}

bool keyExistsInRecent(File& recent, uint32_t count, const Key& due) {
  uint32_t low = 0;
  while (low < count) {
    const uint32_t mid = low + (count - low) / 2;
    Key key{};
    if (!readKey(recent, mid, key)) return false;
    if (agenda_index::recentBefore(key, due)) low = mid + 1;
    else if (agenda_index::recentBefore(due, key)) count = mid;
    else return key.offset == due.offset && key.flags == due.flags &&
        std::strcmp(key.due, due.due) == 0;
  }
  return false;
}
}  // namespace

bool verifyAgendaSources() {
  if (!agendaIndexReady() || work.phase != Phase::Idle || !noteCatalogBaseLoaded()) return false;
  // Recreate the disk-based completion lookup from tasks.state. This runs
  // exclusively after a confirmed restart, before background maintenance.
  work = Work{};
  work.phase = Phase::States;
  bool ok = writeEmpty(kStateRows) && writeEmpty(kUnsorted);
  while (ok && work.phase != Phase::Build) { ok = advance(); delay(1); }
  ok = ok && verifyAgendaSourceRows(active);
  work = Work{};
  SD_MMC.remove(kStateKeys);
  SD_MMC.remove(kStateRows);
  SD_MMC.remove(kUnsorted);
  return ok;
}

bool verifyAgendaIndexBatch(uint32_t& position, bool finalPass) {
  noteAuditWork(finalPass ? NoteStorageAuditPhase::VerifyKeys : NoteStorageAuditPhase::AgendaKeys, position, active.count);
  // Checks published projections without touching their or the originals' bytes.
  if (!agendaIndexReady() || position > active.count || active.open > active.tasks ||
      active.tasks > active.count || !validManifest(active)) return false;
  File recent = SD_MMC.open(keyPath(active.keySlot, true));
  File due = SD_MMC.open(keyPath(active.keySlot, false));
  File data = SD_MMC.open(dataPath(active.dataSlot));
  if (!recent || !due || !data) return false;
  Key previousRecent{}, previousDue{};
  if (position && (!readKey(recent, position - 1, previousRecent) ||
                   !readKey(due, position - 1, previousDue))) return false;
  const uint32_t end = position + std::min<uint32_t>(8, active.count - position);
  while (position < end) {
    Key currentRecent{}, currentDue{};
    if (!readKey(recent, position, currentRecent) || !readKey(due, position, currentDue) ||
        (position && (!agenda_index::recentBefore(previousRecent, currentRecent) ||
                      !agenda_index::dueBefore(previousDue, currentDue)))) return false;
    JsonDocument row;
    if (!readTaskPayload(data, currentRecent, row) ||
        !keyExistsInRecent(recent, active.count, currentDue)) return false;
    previousRecent = currentRecent;
    previousDue = currentDue;
    ++position;
  }
  if (position == active.count) position = UINT32_MAX;
  return true;
}

bool openAgendaIndex(AgendaIndexCursor& cursor, bool recent, const char* fromUtc) {
  if (!agendaIndexReady()) return false;
  cursor = {recent ? active.count : 0, recent ? 0 : active.count, generation, recent};
  if (recent || !fromUtc) return true;
  File keys = SD_MMC.open(keyPath(active.keySlot, false));
  return keys && agenda_index::lowerBoundDue(active.count, fromUtc,
      [&](uint32_t i, Key& key) { return readKey(keys, i, key); }, cursor.next);
}

bool visitAgendaIndex(AgendaIndexCursor& cursor, AgendaVisitor visitor, void* context,
                      uint32_t keyBudget, bool* moreKeys) {
  if (moreKeys) *moreKeys = false;
  if (!agendaIndexReady() || cursor.generation != generation) return false;
  File keys = SD_MMC.open(keyPath(active.keySlot, cursor.recent));
  File data = SD_MMC.open(dataPath(active.dataSlot));
  if (!keys || !data) return false;
  while (cursor.next != cursor.end) {
    if (!keyBudget--) {
      if (moreKeys) *moreKeys = true;
      return true;
    }
    Key key{};
    const uint32_t index = cursor.recent ? cursor.next - 1 : cursor.next;
    if (!readKey(keys, index, key)) break;
    const uint32_t next = cursor.recent ? index : index + 1;
    if ((key.flags & cursor.mask) != cursor.match) { cursor.next = next; continue; }
    JsonDocument row;
    if (!readTaskPayload(data, key, row)) break;
    BrainTaskRecord task;
    task.noteNum = key.note;
    task.id = row["id"] | "";
    task.text = row["text"] | "";
    task.createdUtc = row["created"] | "";
    task.dueUtc = row["due"] | "";
    task.type = row["type"] | "todo";
    task.datePrecision = row["precision"] | "none";
    task.updatedUtc = row["updated"] | "";
    task.done = row["done"] | false;
    cursor.next = next;
    if (!visitor(task, context)) return true;
  }
  if (cursor.next == cursor.end) return true;
  if (!requestAgendaIndexRebuild()) indexFailure();
  return false;
}
