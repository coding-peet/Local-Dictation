#include "notes.h"
#include "audit_progress.h"
#include "sync_queue.h"
#include "tasks.h"
#include "../../config.h"
#include "../../globals.h"
#include "../../types.h"
#include "brain.h"
#include "date_validation.h"
#include "note_trash_policy.h"
#include "rtc.h"
#include "storage_commit.h"
#include <ArduinoJson.h>
#include <Preferences.h>
#include <SD_MMC.h>
#include <algorithm>
#include <climits>

namespace {
constexpr const char* kTrashRoot = "/trash";
constexpr const char* kHighWater = "/trash/high-water";
constexpr const char* kHighWaterTemp = "/trash/high-water.tmp";
constexpr const char* kJournalTemp = "/.journal.tmp";
constexpr const char* kAgeAnchor = "/age-anchor";
using note_trash::Phase;
constexpr Phase kPhases[] = {Phase::Moving, Phase::Parked, Phase::Restoring, Phase::Purging};

std::vector<int> hidden;
int highestReserved = 0;

const char* markerName(Phase phase) {
  switch (phase) {
    case Phase::Moving: return "/.moving";
    case Phase::Parked: return "/.parked";
    case Phase::Restoring: return "/.restoring";
    case Phase::Purging: return "/.purging";
    default: return "";
  }
}

String noteBase(int num) {
  char name[20];
  snprintf(name, sizeof(name), "note_%03d", num);
  return String(name);
}

String trashFolder(int num) { return String(kTrashRoot) + "/" + noteBase(num); }

String entryBasename(const char* path) {
  const String name(path);
  return name.substring(name.lastIndexOf('/') + 1);
}

bool directoryNumbers(const char* path, bool folders, std::vector<int>& numbers) {
  if (folders && !SD_MMC.exists(path)) return true;
  File dir = SD_MMC.open(path);
  if (!dir || !dir.isDirectory()) return false;
  uint32_t examined = 0;
  for (File file = dir.openNextFile(); file; file = dir.openNextFile()) {
    noteAuditWork(NoteStorageAuditPhase::Trash, ++examined);
    if (folders != file.isDirectory()) continue;
    const String name = entryBasename(file.name());
    const int num = folders ? note_trash::folderNumber(name.c_str()) : note_trash::fileNumber(name.c_str());
    if (num) numbers.push_back(num);
  }
  std::sort(numbers.begin(), numbers.end());
  numbers.erase(std::unique(numbers.begin(), numbers.end()), numbers.end());
  return true;
}

bool ensureDirectory(const String& path) {
  if (!SD_MMC.exists(path.c_str()) && !SD_MMC.mkdir(path.c_str())) return false;
  File folder = SD_MMC.open(path.c_str());
  return folder && folder.isDirectory();
}

Phase phaseOf(const String& folder) {
  Phase state = Phase::None;
  for (Phase phase : kPhases) {
    if (!SD_MMC.exists((folder + markerName(phase)).c_str())) continue;
    if (state != Phase::None) return Phase::Ambiguous;
    state = phase;
  }
  return state;
}

bool advancePhase(const String& folder, Phase from, Phase to) {
  const String before = folder + markerName(from);
  const String after = folder + markerName(to);
  return !SD_MMC.exists(after.c_str()) && SD_MMC.rename(before.c_str(), after.c_str());
}

template<class Operation>
bool visitFiles(int num, const String& folder, Operation operation) {
  const String prefix = folder + "/" + noteBase(num) + ".";
  for (const char* extension : note_trash::bundleExtensions) {
    for (const char* suffix : note_trash::commitSuffixes) {
      if (!operation(prefix + extension + suffix, String(extension) + suffix)) return false;
    }
  }
  return true;
}

bool hasFiles(int num, const String& folder) {
  return !visitFiles(num, folder, [](const String& name, const String&) { return !SD_MMC.exists(name.c_str()); });
}

bool moveFiles(int num, const String& source, const String& destination) {
  const String target = destination + "/" + noteBase(num) + ".";
  return visitFiles(num, source, [&](const String& path, const String& suffix) {
    if (!SD_MMC.exists(path.c_str())) return true;
    File entry = SD_MMC.open(path.c_str(), FILE_READ);
    const bool regular = entry && !entry.isDirectory();
    if (entry) entry.close();
    return regular && note_trash::relocate(SD_MMC, path.c_str(), (target + suffix).c_str());
  });
}

bool writeSmallFile(const String& path, const String& content) {
  if (SD_MMC.exists(path.c_str()) && !SD_MMC.remove(path.c_str())) return false;
  File file = SD_MMC.open(path.c_str(), FILE_WRITE);
  if (!file) return false;
  const size_t written = file.print(content);
  file.flush();
  return written == content.length() && file.size() == content.length();
}

int64_t readIntegerFile(const String& path) {
  if (!recoverStoredFile(SD_MMC, path)) return 0;
  File file = SD_MMC.open(path.c_str());
  if (!file || file.isDirectory() || file.size() > 16) return 0;
  const String value = file.readString();
  char* end = nullptr;
  const long long num = strtoll(value.c_str(), &end, 10);
  return end && *end == '\0' && num > 0 ? num : 0;
}

int readHighWater() {
  const int64_t value = readIntegerFile(String(kHighWater));
  return value <= INT_MAX ? static_cast<int>(value) : 0;
}

bool persistHighWater(int num) {
  if (readHighWater() >= num) return true;
  return writeSmallFile(String(kHighWaterTemp), String(num))
      && commitStoredFile(SD_MMC, String(kHighWater), String(kHighWaterTemp));
}

int64_t trashClock() {
  struct tm utc{};
  if (!rtcReadUtcTm(&utc)) return 0;
  const int64_t now = utcTmToEpoch(utc);
  return note_trash::validEpoch(now) ? now : 0;
}

bool removeRegularFile(const String& path) {
  if (!SD_MMC.exists(path.c_str())) return true;
  File entry = SD_MMC.open(path.c_str(), FILE_READ);
  const bool regular = entry && !entry.isDirectory();
  if (entry) entry.close();
  return regular && SD_MMC.remove(path.c_str());
}

bool clearAgeAnchor(const String& folder) {
  const String path = folder + kAgeAnchor;
  return removeRegularFile(path + ".tmp") && removeRegularFile(path + ".bak") && removeRegularFile(path);
}

int64_t ageForAutomaticPurge(const String& folder, const TrashedNote& entry) {
  if (note_trash::validEpoch(entry.trashedAt)) return entry.trashedAt;
  return note_trash::retentionStart(entry.trashedAt, readIntegerFile(folder + kAgeAnchor));
}

bool startRetentionClock(const String& folder, int64_t now) {
  char value[24];
  snprintf(value, sizeof(value), "%lld", static_cast<long long>(now));
  const String path = folder + kAgeAnchor;
  const String temp = path + ".tmp";
  return writeSmallFile(temp, String(value)) && commitStoredFile(SD_MMC, path, temp);
}

bool readJournal(const String& folder, int num, Phase phase, TrashedNote& entry) {
  File file = SD_MMC.open((folder + markerName(phase)).c_str());
  if (!file || file.isDirectory() || file.size() > 512) return false;
  JsonDocument doc;
  if (deserializeJson(doc, file) || !doc["num"].is<int>() || doc["num"].as<int>() != num
      || !doc["trashedAt"].is<int64_t>() || !doc["tag"].is<const char*>()
      || !doc["capturedUtc"].is<const char*>()) return false;
  const int64_t time = doc["trashedAt"].as<int64_t>();
  const String tag = doc["tag"].as<String>();
  const String captured = doc["capturedUtc"].as<String>();
  if (tag.length() >= sizeof(NoteEntry{}.tag) || (time != 0 && !note_trash::validEpoch(time))
      || (captured.length() && !validUtcTimestamp(captured.c_str()))) return false;
  entry = {num, tag, time, captured};
  return true;
}

bool beginJournal(int num, const String& folder) {
  const auto indexed = std::find_if(noteIndex.begin(), noteIndex.end(),
                                    [num](const NoteEntry& item) { return item.num == num; });
  String tag = indexed == noteIndex.end() ? String() : String(indexed->tag);
  String captured;
  const String metaPath = noteMetaPath(num);
  File meta = SD_MMC.open(metaPath.c_str(), FILE_READ);
  if (!meta) meta = SD_MMC.open((metaPath + ".bak").c_str(), FILE_READ);
  if (meta && !meta.isDirectory() && meta.size() <= 1024) {
    while (meta.available()) {
      String line = meta.readStringUntil('\n');
      line.trim();
      if (tag.isEmpty() && line.startsWith("tag=")) tag = line.substring(4);
      if (line.startsWith("created_utc=")) captured = line.substring(12);
    }
  }
  if (meta) meta.close();
  if (tag.isEmpty() || tag.length() >= sizeof(NoteEntry{}.tag)) tag = "Inbox";
  if (!validUtcTimestamp(captured.c_str())) captured = "";
  JsonDocument doc;
  doc["num"] = num;
  doc["tag"] = tag;
  doc["trashedAt"] = trashClock();
  doc["capturedUtc"] = captured;
  const String temp = folder + kJournalTemp;
  String text;
  if (!serializeJson(doc, text) || !writeSmallFile(temp, text)) return false;
  const String moving = folder + markerName(Phase::Moving);
  return !SD_MMC.exists(moving.c_str()) && SD_MMC.rename(temp.c_str(), moving.c_str());
}

void markHidden(int num) {
  const auto at = std::lower_bound(hidden.begin(), hidden.end(), num);
  if (at == hidden.end() || *at != num) hidden.insert(at, num);
  highestReserved = std::max(highestReserved, num);
}

void markVisible(int num) {
  const auto at = std::lower_bound(hidden.begin(), hidden.end(), num);
  if (at != hidden.end() && *at == num) hidden.erase(at);
}

void dropFromLiveIndex(int num) {
  noteIndex.erase(std::remove_if(noteIndex.begin(), noteIndex.end(),
                                 [num](const NoteEntry& item) { return item.num == num; }), noteIndex.end());
  if (lastRecNum == num) lastRecNum = -1;
}

bool finishMove(int num, const String& folder) {
  TrashedNote journal{};
  return readJournal(folder, num, Phase::Moving, journal)
      && moveFiles(num, String(NOTES_DIR), folder)
      && advancePhase(folder, Phase::Moving, Phase::Parked);
}

bool parkNote(int num) {
  if (num <= 0) return false;
  const String folder = trashFolder(num);
  const Phase state = phaseOf(folder);
  if (state == Phase::Parked) {
    TrashedNote journal{};
    if (!readJournal(folder, num, Phase::Parked, journal)) return false;
    markHidden(num);
    dropFromLiveIndex(num);
    return true;
  }
  if (state == Phase::None) {
    if (!hasFiles(num, String(NOTES_DIR)) || hasFiles(num, folder)
        || !ensureDirectory(String(kTrashRoot)) || !ensureDirectory(folder)
        || !clearAgeAnchor(folder) || !beginJournal(num, folder)) return false;
  } else if (state != Phase::Moving) {
    return false;
  }
  markHidden(num);
  dropFromLiveIndex(num);
  return finishMove(num, folder);
}

bool finishRestore(int num, const String& folder) {
  TrashedNote journal{};
  if (!readJournal(folder, num, Phase::Restoring, journal)
      || !moveFiles(num, folder, String(NOTES_DIR))
      || !hasFiles(num, String(NOTES_DIR))) return false;

  NoteEntry restored{};
  restored.num = num;
  strncpy(restored.tag, journal.tag.c_str(), sizeof(restored.tag) - 1);
  const String transcript = String(NOTES_DIR) + "/" + noteBase(num) + ".txt";
  if (SD_MMC.exists((transcript + ".bak").c_str()) && !recoverStoredFile(SD_MMC, transcript)) return false;
  File text = SD_MMC.open(transcript.c_str(), FILE_READ);
  restored.hasText = text && !text.isDirectory() && text.size() > 0;
  if (text) text.close();
  restored.momentCount = static_cast<uint16_t>(noteMomentCount(num));
  const auto at = std::find_if(noteIndex.begin(), noteIndex.end(),
                               [num](const NoteEntry& note) { return note.num == num; });
  if (at == noteIndex.end()) noteIndex.push_back(restored);
  else *at = restored;
  std::sort(noteIndex.begin(), noteIndex.end(),
            [](const NoteEntry& first, const NoteEntry& second) { return first.num < second.num; });
  if (!saveIndex() || !clearAgeAnchor(folder)
      || !SD_MMC.remove((folder + markerName(Phase::Restoring)).c_str())) {
    dropFromLiveIndex(num);
    return false;
  }
  markVisible(num);
  refreshRestoredNoteBrainIndexes(num);
  SD_MMC.rmdir(folder.c_str()); // A folder with unrecognized entries is preserved.
  return true;
}

bool purgeOne(int num, const String& folder, Phase state) {
  if (!persistHighWater(num)) return false;
  if (state == Phase::Parked && !advancePhase(folder, Phase::Parked, Phase::Purging)) return false;
  if (!visitFiles(num, folder, [](const String& path, const String&) {
        return removeRegularFile(path);
      })) return false;
  if (!clearAgeAnchor(folder) || !SD_MMC.remove((folder + markerName(Phase::Purging)).c_str())) return false;
  markVisible(num);
  SD_MMC.rmdir(folder.c_str());
  return true;
}

bool purgeBatch(int64_t now, bool manual, bool& more) {
  more = false;
  if (!manual && !note_trash::validEpoch(now)) return true;
  std::vector<int> numbers;
  if (!directoryNumbers(kTrashRoot, true, numbers)) return false;
  const int days = trashRetentionDays();
  int attempted = 0;
  bool success = true;
  for (const int num : numbers) {
    const String folder = trashFolder(num);
    const Phase state = phaseOf(folder);
    if (!note_trash::clearable(state)) continue;
    TrashedNote entry{};
    if (!readJournal(folder, num, state, entry)) { success = false; continue; }
    bool needsClock = false;
    if (state == Phase::Parked && !manual) {
      const int64_t from = ageForAutomaticPurge(folder, entry);
      needsClock = from == 0;
      if (!needsClock && !note_trash::expired(from, now, days)) continue;
    }
    if (attempted == note_trash::purgeBatchSize) { more = true; break; }
    ++attempted;
    const bool finished = needsClock ? startRetentionClock(folder, now) : purgeOne(num, folder, state);
    success = finished && success;
  }
  return success;
}
}  // namespace

bool noteIsTrashed(int num) { return std::binary_search(hidden.begin(), hidden.end(), num); }

void discoverNoteTrash() {
  hidden.clear();
  highestReserved = readHighWater();
  std::vector<int> numbers;
  if (!directoryNumbers(kTrashRoot, true, numbers)) return;
  uint32_t examined = 0;
  for (const int num : numbers) {
    noteAuditWork(NoteStorageAuditPhase::Trash, examined++, numbers.size());
    highestReserved = std::max(highestReserved, num);
    const String folder = trashFolder(num);
    if (phaseOf(folder) != Phase::None || hasFiles(num, folder)) markHidden(num);
  }
}

void recoverNoteTrash() {
  const auto recovering = hidden;
  for (const int num : recovering) {
    const String folder = trashFolder(num);
    const Phase phase = phaseOf(folder);
    if (phase == Phase::Moving) finishMove(num, folder);
    else if (phase == Phase::Restoring) finishRestore(num, folder);
  }
}

int highestTrashedNoteNumber() { return highestReserved; }
bool noteHasLiveBundle(int num) { return hasFiles(num, String(NOTES_DIR)); }

bool deleteNote(int num) {
  SyncQueueChange change(num);
  if (!change || !beginAgendaIndexChange(num)) return false;
  const bool parked = parkNote(num);
  if (!parked && !noteIsTrashed(num)) return false;
  const bool saved = saveIndex();
  if (saved && parked) finishAgendaIndexChange(num);
  return saved && parked;
}

bool deleteAllNotes() {
  SyncQueueChange change(0);
  if (!change || !beginAgendaIndexChange(0)) return false;
  std::vector<int> numbers;
  if (!directoryNumbers(NOTES_DIR, false, numbers)) return false;
  bool done = true;
  for (const int num : numbers) {
    if (!noteIsTrashed(num) || phaseOf(trashFolder(num)) == Phase::Moving) done = parkNote(num) && done;
  }
  const bool saved = saveIndex();
  if (saved && done) finishAgendaIndexChange(0);
  return saved && done;
}

bool listTrashedNotes(std::vector<TrashedNote>& entries) {
  entries.clear();
  std::vector<int> numbers;
  if (!directoryNumbers(kTrashRoot, true, numbers)) return false;
  for (const int num : numbers) {
    const String folder = trashFolder(num);
    const Phase state = phaseOf(folder);
    if (state == Phase::Ambiguous && SD_MMC.exists((folder + markerName(Phase::Parked)).c_str())) return false;
    if (state != Phase::Parked) continue;
    TrashedNote entry{};
    if (!readJournal(folder, num, Phase::Parked, entry)) return false;
    entries.push_back(entry);
  }
  return true;
}

File openParkedNoteFile(int num, ParkedNoteFile kind) {
  if (num <= 0 || !noteIsTrashed(num)) return {};
  const String folder = trashFolder(num);
  if (phaseOf(folder) != Phase::Parked) return {};
  TrashedNote entry{};
  if (!readJournal(folder, num, Phase::Parked, entry)) return {};
  const String path = folder + "/" + noteBase(num) +
                      (kind == ParkedNoteFile::Transcript ? ".txt" : ".wav");
  File file = SD_MMC.open(path.c_str(), FILE_READ);
  if (!file && kind == ParkedNoteFile::Transcript)
    file = SD_MMC.open((path + ".bak").c_str(), FILE_READ);
  return file && !file.isDirectory() ? file : File{};
}

bool restoreTrashedNote(int num) {
  if (num <= 0) return false;
  SyncQueueChange change(num);
  if (!change || !beginAgendaIndexChange(num)) return false;
  const String folder = trashFolder(num);
  const Phase state = phaseOf(folder);
  if (state == Phase::Parked) {
    TrashedNote entry{};
    if (!readJournal(folder, num, state, entry) || hasFiles(num, String(NOTES_DIR))
        || !advancePhase(folder, Phase::Parked, Phase::Restoring)) return false;
  } else if (state != Phase::Restoring) {
    return false;
  }
  const bool restored = finishRestore(num, folder);
  if (restored) finishAgendaIndexChange(num);
  return restored;
}

int trashRetentionDays() {
  Preferences prefs;
  if (!prefs.begin("pala-trash", true)) return note_trash::defaultRetentionDays;
  const int days = prefs.getInt("retention", note_trash::defaultRetentionDays);
  return note_trash::validRetention(days) ? days : note_trash::defaultRetentionDays;
}

bool setTrashRetentionDays(int days) {
  if (!note_trash::validRetention(days)) return false;
  Preferences prefs;
  return prefs.begin("pala-trash", false)
      && (prefs.getInt("retention", note_trash::defaultRetentionDays) == days
          || prefs.putInt("retention", days) == sizeof(int));
}

bool emptyTrash(bool& more) { return purgeBatch(0, true, more); }

void maintainNoteTrash() {
  static uint32_t nextCheck = 4000;
  const uint32_t now = millis();
  if (static_cast<int32_t>(now - nextCheck) < 0) return;
  bool more = false;
  const bool success = purgeBatch(trashClock(), false, more);
  nextCheck = millis() + (success && more ? 2000UL : 60000UL);
}
