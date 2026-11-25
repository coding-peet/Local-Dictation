#include "Arduino.h"
#include "../../config.h"
#include "../../globals.h"
#include "../../types.h"
#include "notes.h"
#include "record.h"
#include "local_transcript_policy.h"
#include "rtc.h"
#include "draw.h"
#include "brain.h"
#include "SD_MMC.h"
#include <algorithm>
#include <map>
#include <dirent.h>
#include <cerrno>
#include "retry_policy.h"
#include "storage_commit.h"
#include "catalog_storage.h"
#include "audit_progress.h"
#include "capture_wav_recovery.h"
#include <sys/stat.h>
#include "sync_queue.h"
#include "note_trash_policy.h"

// ─── Index ────────────────────────────────────────────────────────────────

namespace {
constexpr const char* kIndexTemp = "/notes/index.tmp";
constexpr char catalogMigratedPath[] = NOTES_DIR "/catalog.fast.v2";
note_catalog::Snapshot catalogSnapshot;
bool catalogChanged = false;

note_catalog::Source catalogVisibility(int num) {
  yieldToPriorityMicrophone();
  using note_catalog::Source;
  if (noteIsTrashed(num)) return Source::Absent;
  if (num > highestTrashedNoteNumber()) return Source::Present;
  // An old catalog may include a purged note. Only filter it when the card
  // positively reports that every possible live bundle member is absent.
  for (const char* extension : note_trash::bundleExtensions) {
    for (const char* suffix : note_trash::commitSuffixes) {
      char path[96];
      snprintf(path, sizeof(path), "/sdcard" NOTES_DIR "/note_%03d.%s%s", num, extension, suffix);
      struct stat info;
      if (stat(path, &info) == 0) return Source::Present;
      if (errno != ENOENT) return Source::Error;
    }
  }
  return Source::Absent;
}

bool replaceNoteFileAtomically(const String& path, const String& temp) {
  return commitStoredFile(SD_MMC, path, temp);
}
}

bool saveIndex() {
  if (!catalogSnapshot.loaded) return false;
  if (SD_MMC.exists(kIndexTemp)) SD_MMC.remove(kIndexTemp);
  File f = SD_MMC.open(kIndexTemp, FILE_WRITE);
  if (!f) return false;
  size_t expected = 0;
  bool complete = note_catalog::writeRows(noteIndex, [&](const char* data, size_t size) {
    return f.write(reinterpret_cast<const uint8_t*>(data), size);
  }, expected);
  f.flush();
  complete &= f.size() == expected;
  f.close();
  return complete && replaceNoteFileAtomically(INDEX_FILE, kIndexTemp);
}

bool addToIndex(int num, const char* tag, bool hasText) {
  NoteEntry e;
  e.num = num;
  strncpy(e.tag, tag, sizeof(e.tag)-1);
  e.tag[sizeof(e.tag)-1]='\0';
  e.hasText = hasText;
  e.momentCount = (uint16_t)noteMomentCount(num);
  noteIndex.push_back(e);
  if (!saveIndex()) { noteIndex.pop_back(); return false; }
  removeBrainSearchIndexEntry(num);
  removeBrainVectorIndexEntry(num);
  return true;
}

bool updateIndexHasText(int num) {
  auto item = std::lower_bound(noteIndex.begin(), noteIndex.end(), num,
                               [](const NoteEntry& row, int id) { return row.num < id; });
  if (item == noteIndex.end() || item->num != num) return false;
  if (!item->hasText) {
    item->hasText = true;
    if (!saveIndex()) { item->hasText = false; return false; }
  }
  return writeNoteMeta(num, item->tag);
}

namespace {
constexpr char transcriptJournal[] = "/sdcard" NOTES_DIR "/transcript.pending";
bool transcriptRepairPending = false;

void transcriptPath(int num, char (&path)[64]) {
  snprintf(path, sizeof(path), "/sdcard" NOTES_DIR "/note_%03d.txt", num);
}

bool replayTranscript() {
  transcriptRepairPending = !note_transcript::replay(transcriptJournal, [](int id) {
    if (noteIsTrashed(id)) return true;
    char path[64];
    transcriptPath(id, path);
    SyncQueueChange change(id);
    return change && note_transcript::repairCommitted(path,
        [&] { return updateIndexHasText(id); },
        [&] { return noteQueueState(id); },
        [&] { return writeNoteQueueState(id, "organizing", nullptr, false, true); });
  });
  return !transcriptRepairPending;
}
}

note_transcript::Stored storedNoteTranscript(int num) {
  char path[64];
  transcriptPath(num, path);
  return note_transcript::inspect(path);
}

bool reconcileNoteTranscript(int num) {
  if (!replayTranscript() || !note_transcript::reserve(transcriptJournal, num)) return false;
  return replayTranscript();
}

bool saveNoteTranscript(int num, const String& text) {
  if (noteIsTrashed(num) || text.isEmpty() || !replayTranscript()) return false;
  SyncQueueChange change(num);
  if (!change || !note_transcript::reserve(transcriptJournal, num)) return false;
  // Either the journal or the dirty queue survives every boundary until the
  // catalog, route metadata and derived queue agree with the published bytes.
  transcriptRepairPending = true;
  char path[64];
  transcriptPath(num, path);
  const bool stored = note_transcript::storeOnce(path, {text.c_str(), text.length()});
  const bool repaired = replayTranscript();
  return stored && repaired;
}

namespace {
constexpr char captureIntentPath[] = NOTES_DIR "/capture.pending";
enum class CatalogRecovery { Load, Intent, Directory, Transcripts, Publish, Ready };
CatalogRecovery catalogRecovery = CatalogRecovery::Load;
bool recoverCatalogSources = true;
DIR* recoveryDirectory = nullptr;
int checkedTranscript = 0;

bool repairCatalogRecording(int num, bool interrupted = false) {
  if (num <= 0 || noteIsTrashed(num)) return true;
  char wavPath[64];
  snprintf(wavPath, sizeof(wavPath), "/sdcard" NOTES_DIR "/note_%03d.wav", num);
  return note_catalog::recoverRecording(noteIndex, num, wavPath, [&](auto pos) {
    SyncQueueChange queueChange(num);
    if (!queueChange) return false;
    NoteEntry recovered{};
    recovered.num = num;
    strncpy(recovered.tag, "Inbox", sizeof(recovered.tag) - 1);
    recovered.hasText = noteHasCommittedTranscript(num);
    recovered.momentCount = static_cast<uint16_t>(noteMomentCount(num));
    noteIndex.insert(pos, recovered);
    catalogChanged = true;
    return true;
  }, std::fopen, interrupted ? 2 : 1001);
}

bool recoverCaptureIntent() {
  return note_catalog::recoverReservation("/sdcard" NOTES_DIR "/capture.pending", [](int note, const char* utc) {
    if (noteIsTrashed(note)) return true;
    char wav[72];
    snprintf(wav, sizeof(wav), "/sdcard" NOTES_DIR "/note_%03d.wav", note);
    const auto state = capture_wav::recover(wav);
    if (state == capture_wav::State::Empty || state == capture_wav::State::Missing) return true;
    if (state != capture_wav::State::Ready) return false;
    const String meta = String("/sdcard") + noteMetaPath(note);
    if (!capture_wav::restoreTimestamp(meta.c_str(), utc, [&](const char* captured) {
          return writeNoteMeta(note, "Inbox", captured);
        })) return false;
    if (!repairCatalogRecording(note, true) || (catalogChanged && !saveIndex())) return false;
    catalogChanged = false;
    return true;
  });
}

bool loadCatalogBase() {
  bool filtered = false;
  uint32_t examined = 0;
  if (!catalogSnapshot.load(noteIndex, "/sdcard" INDEX_FILE, "/sdcard" INDEX_FILE ".bak",
                            "/sdcard" NOTES_DIR "/index.bak", "/sdcard" NOTES_DIR "/catalog.fast.v2",
                            [&](int num) {
                              noteAuditWork(NoteStorageAuditPhase::LoadCatalog, ++examined);
                              const auto visible = catalogVisibility(num);
                              if (visible == note_catalog::Source::Absent) filtered = true;
                              return visible;
                            })) return false;
  catalogChanged |= filtered || catalogSnapshot.needsDirectory;
  if (recoverCatalogSources) recoverNoteTrash();
  return true;
}

bool startCatalogRecovery() {
  if (catalogRecovery == CatalogRecovery::Load) {
    if (!loadCatalogBase()) return false;
    catalogRecovery = CatalogRecovery::Intent;
  }
  if (recoverCatalogSources && (!recoverCaptureIntent() || !recoverSpooledRecordings() || !replayTranscript())) return false;
  File marker = SD_MMC.open(catalogMigratedPath, FILE_READ);
  if (!catalogChanged && !catalogSnapshot.needsDirectory && marker && marker.size() == 1 && marker.read() == 1) {
    catalogRecovery = CatalogRecovery::Ready;
  } else {
    if (!requestSyncQueueRebuild()) return false;
    catalogRecovery = CatalogRecovery::Directory;
  }
  return true;
}
}  // namespace

void initializeNoteCatalog(bool recoverSources) {
  recoverCatalogSources = recoverSources;
  discoverNoteTrash();
  startCatalogRecovery();
}

bool noteCatalogBaseLoaded() { return catalogSnapshot.loaded; }

bool noteCatalogReady() { return catalogRecovery == CatalogRecovery::Ready && !transcriptRepairPending; }

bool maintainNoteCatalog() {
  if (transcriptRepairPending && !replayTranscript()) return false;
  if (catalogRecovery == CatalogRecovery::Load || catalogRecovery == CatalogRecovery::Intent) {
    if (!startCatalogRecovery()) return false;
  }
  if (catalogRecovery == CatalogRecovery::Directory) {
    if (!recoveryDirectory) recoveryDirectory = opendir("/sdcard" NOTES_DIR);
    if (!recoveryDirectory) return false;
    // Enumerate directory entries without reopening each named WAV. Only
    // candidates missing from the sorted catalog need to be opened.
    for (int examined = 0; examined < 24; ++examined) {
      if (pendingPriorityRecording()) return false;
      errno = 0;
      const auto* entry = readdir(recoveryDirectory);
      if (!entry) {
        const bool complete = errno == 0;
        closedir(recoveryDirectory);
        recoveryDirectory = nullptr;
        if (complete) catalogRecovery = CatalogRecovery::Transcripts;
        break;
      }
      const std::string_view name = entry->d_name;
      if (name.size() >= 4 && note_trash::equalsAscii(name.substr(name.size() - 4), ".wav") &&
          !repairCatalogRecording(note_trash::fileNumber(name))) {
        closedir(recoveryDirectory);
        recoveryDirectory = nullptr;
        return false;
      }
    }
  } else if (catalogRecovery == CatalogRecovery::Transcripts) {
    auto pos = std::upper_bound(noteIndex.begin(), noteIndex.end(), checkedTranscript,
                                [](int num, const NoteEntry& item) { return num < item.num; });
    for (int examined = 0; pos != noteIndex.end() && examined < 4; ++pos, ++examined) {
      if (pendingPriorityRecording()) return false;
      const auto source = storedNoteTranscript(pos->num);
      if (source == note_transcript::Stored::Error) return false;
      const bool hasText = note_transcript::committed(source);
      if (hasText && recoverCatalogSources && (!pos->hasText || source == note_transcript::Stored::Backup)) {
        if (!reconcileNoteTranscript(pos->num)) return false;
      } else if (pos->hasText != hasText) {
        pos->hasText = hasText;
        catalogChanged = true;
      }
      checkedTranscript = pos->num;
    }
    if (pos == noteIndex.end()) catalogRecovery = CatalogRecovery::Publish;
  }
  if (catalogRecovery == CatalogRecovery::Publish) {
    if (catalogChanged && (!requestSyncQueueRebuild() || !saveIndex())) return false;
    File marker = SD_MMC.open(catalogMigratedPath, FILE_WRITE);
    if (!marker || marker.write(static_cast<uint8_t>(1)) != 1) return false;
    marker.flush();
    catalogChanged = false;
    catalogRecovery = CatalogRecovery::Ready;
  }
  return noteCatalogReady();
}

bool beginNoteRecording(int num, const String& capturedUtc) {
  return catalogSnapshot.loaded && recoverCaptureIntent() &&
      note_catalog::reserveRecording("/sdcard" NOTES_DIR "/capture.pending", num, capturedUtc.c_str());
}

int nextNoteNumber() {
  if (!catalogSnapshot.loaded) return -1;
  int maxNum = highestTrashedNoteNumber();
  for (const NoteEntry& note : noteIndex) maxNum = std::max(maxNum, note.num);
  do {
    if (maxNum == INT32_MAX) return -1;
    ++maxNum;
  } while (noteHasLiveBundle(maxNum));
  return maxNum;
}

bool saveTag(int num, const char* tag, const String& capturedUtc) {
  SyncQueueChange change(num);
  if (!change) return false;
  if (!writeNoteMeta(num, tag, capturedUtc)) return false;
  if (!addToIndex(num, tag, false)) return false;
  if (!writeNoteQueueState(num, "queued")) return false;
  if (SD_MMC.exists(captureIntentPath) && !SD_MMC.remove(captureIntentPath))
    Serial.println("[Storage] capture recovery marker will be reconciled at next startup");
  return true;
}

// ─── Tags ─────────────────────────────────────────────────────────────────

// ─── Meta ─────────────────────────────────────────────────────────────────

String noteMetaPath(int num) {
  char path[64];
  snprintf(path, sizeof(path), "%s/note_%03d.meta", NOTES_DIR, num);
  return String(path);
}

String readNoteMetaValue(int num, const char* key) {
  String path = noteMetaPath(num);
  recoverStoredFile(SD_MMC, path);
  File f = SD_MMC.open(path.c_str());
  if (!f) return "";
  String prefix = String(key) + "=";
  while (f.available()) {
    String line = f.readStringUntil('\n'); line.trim();
    if (line.startsWith(prefix)) { f.close(); return line.substring(prefix.length()); }
  }
  f.close();
  return "";
}

bool writeNoteMeta(int num, const char* tag, const String& capturedUtc) {
  String path = noteMetaPath(num);
  String temp = path + ".tmp";
  std::string priorTime;
  if (!note_transcript::readField((String("/sdcard") + path).c_str(), "created_utc=", priorTime)) return false;
  const String existingCreated(priorTime.c_str());
  // Only the recording caller supplies a capture time. A later index repair or
  // transcription cannot turn an unknown capture time into its processing time.
  const String& created = existingCreated.length() ? existingCreated : capturedUtc;
  SD_MMC.remove(temp.c_str());
  File f = SD_MMC.open(temp.c_str(), FILE_WRITE);
  if (!f) return false;
  bool hasText = false;
  for (int i = 0; i < (int)noteIndex.size(); i++)
    if (noteIndex[i].num == num) { hasText = noteIndex[i].hasText; break; }
  const String contents = "created_utc=" + created + "\ntag=" + (tag ? tag : "") + "\nsynced=" + (hasText ? "1\n" : "0\n");
  const size_t written = f.print(contents);
  f.flush();
  const bool complete = written == contents.length() && f.size() == contents.length();
  f.close();
  return complete && replaceNoteFileAtomically(path, temp);
}

static String queuePath(int num) {
  char path[64];
  snprintf(path, sizeof(path), "%s/note_%03d.queue", NOTES_DIR, num);
  return String(path);
}

static String readQueueValue(int num, const char* key) {
  recoverStoredFile(SD_MMC, queuePath(num));
  File file = SD_MMC.open(queuePath(num));
  if (!file) return "";
  const String prefix = String(key) + "=";
  while (file.available()) {
    String line = file.readStringUntil('\n');
    line.trim();
    if (line.startsWith(prefix)) {
      file.close();
      return line.substring(prefix.length());
    }
  }
  file.close();
  return "";
}

bool noteHasCommittedTranscript(int num) {
  return note_transcript::committed(storedNoteTranscript(num));
}

String noteQueueState(int num) {
  std::string stored;
  if (!note_transcript::readField((String("/sdcard") + queuePath(num)).c_str(), "state=", stored))
    return "storage-error";
  const String state(stored.c_str());
  if (state == "usb-local") {
    // A local-route marker intentionally reaches the card before the text.
    // Without committed text the recording must remain eligible after a failure.
    const auto source = storedNoteTranscript(num);
    if (source == note_transcript::Stored::Error) return "storage-error";
    return note_transcript::localRouteState(note_transcript::committed(source));
  }
  return state.length() ? state : "queued";
}

bool notePendingTranscription(const NoteEntry& note) {
  if (!note_transcript::canSendAudio(storedNoteTranscript(note.num), note.hasText)) return false;
  const String state = noteQueueState(note.num);
  return note_transcript::pendingRecording(note.hasText, state.c_str());
}

int pendingTranscriptionCount() {
  return syncQueueCounts().audio;
}

String noteQueueError(int num) { return readQueueValue(num, "error"); }
int noteQueueRetries(int num) { return readQueueValue(num, "retries").toInt(); }
int noteQueueFailureCount(int num) { return readQueueValue(num, "failures").toInt(); }
String noteQueueUpdatedUtc(int num) { return readQueueValue(num, "updated_utc"); }

static time_t queueUtcEpoch(const String& iso) {
  if (iso.length() < 19) return 0;
  struct tm value = {};
  value.tm_year = iso.substring(0, 4).toInt() - 1900;
  value.tm_mon = iso.substring(5, 7).toInt() - 1;
  value.tm_mday = iso.substring(8, 10).toInt();
  value.tm_hour = iso.substring(11, 13).toInt();
  value.tm_min = iso.substring(14, 16).toInt();
  value.tm_sec = iso.substring(17, 19).toInt();
  return utcTmToEpoch(value);
}

static std::map<int, uint32_t> retryStartedMs;
void resumeBlockedCloudNotes() {
  for (const NoteEntry& note : noteIndex) {
    if (noteQueueState(note.num) != "blocked") continue;
    const String error = noteQueueError(note.num);
    if (error == "upload limit exceeded" || error == "empty recording" || error == "no speech recognized" || error == "recording unavailable" ||
        error == "organization: transcript exceeds 32000 bytes") continue;
    writeNoteQueueState(note.num, "queued", nullptr, false, true);
  }
}

bool noteQueueRetryDue(int num) {
  const String state = noteQueueState(num);
  if (state == "blocked" || state == "usb-local" || state == "storage-error") return false;
  if (state != "retry" && noteQueueFailureCount(num) <= 0) return true;
  const int failures = max(1, noteQueueFailureCount(num));
  String failedUtc = readQueueValue(num, "last_failure_utc");
  if (!failedUtc.length()) failedUtc = noteQueueUpdatedUtc(num); // old queue files
  const time_t updated = queueUtcEpoch(failedUtc);
  const time_t now = time(nullptr);
  // After a reboot without wall-clock time, allow one recovery attempt after
  // two minutes; otherwise a 30-minute backoff would outlast the awake budget.
  const uint32_t recoveredStart = millis() - (queueRetryDelaySeconds(failures) - 120U) * 1000U;
  const auto start = retryStartedMs.emplace(num, recoveredStart);
  return queueRetryElapsed(failures, updated, now, start.first->second, millis());
}

bool writeNoteQueueState(int num, const char* state, const char* error, bool incrementRetry, bool progressMade) {
  SyncQueueChange change(num);
  if (!change) return false;
  const String path = queuePath(num);
  const String temp = path + ".tmp";
  int retries = noteQueueRetries(num);
  int failures = noteQueueFailureCount(num);
  if (incrementRetry && retries < 1000000) retries++;
  progressMade = progressMade || (state && String(state) == "ready");
  failures = queueNextFailures(failures, incrementRetry, progressMade);
  String failedUtc = readQueueValue(num, "last_failure_utc");
  if (!failedUtc.length() && failures > 0) failedUtc = noteQueueUpdatedUtc(num);
  const uint32_t failureStarted = millis();
  if (incrementRetry) failedUtc = currentUtcIso();
  if (progressMade) failedUtc = "";
  SD_MMC.remove(temp.c_str());
  File file = SD_MMC.open(temp.c_str(), FILE_WRITE);
  if (!file) { Serial.printf("[Storage] queue open failed #%d\n", num); return false; }
  const String contents = String("state=") + (state ? state : "queued") + "\nretries=" + String(retries) +
      "\nfailures=" + String(failures) + "\nlast_failure_utc=" + failedUtc + "\nerror=" +
      (error ? error : "") + "\nupdated_utc=" + currentUtcIso() + "\n";
  const size_t written = file.print(contents);
  file.flush();
  const bool complete = written == contents.length() && file.size() == contents.length();
  file.close();
  const bool saved = complete && replaceNoteFileAtomically(path, temp);
  // In-memory backoff must describe committed metadata, not a failed write.
  if (saved) {
    if (progressMade) retryStartedMs.erase(num);
    else if (incrementRetry) retryStartedMs[num] = failureStarted;
  }
  if (!saved) Serial.printf("[Storage] queue commit failed #%d\n", num);
  return saved;
}

// ─── Moments ────────────────────────────────────────────────────────────────

static String noteMomentsPath(int num) {
  char path[64];
  snprintf(path, sizeof(path), "%s/note_%03d.moments", NOTES_DIR, num);
  return String(path);
}

bool saveNoteMoments(int num, const std::vector<uint32_t>& momentsMs) {
  String path = noteMomentsPath(num);
  String temp = path + ".tmp";
  if (SD_MMC.exists(temp.c_str())) SD_MMC.remove(temp.c_str());
  File f = SD_MMC.open(temp.c_str(), FILE_WRITE);
  if (!f) return false;
  size_t expected = 0;
  bool complete = true;
  for (uint32_t momentMs : momentsMs) {
    const String line = String(momentMs) + "\n";
    expected += line.length();
    complete &= f.print(line) == line.length();
  }
  f.flush();
  complete &= f.size() == expected;
  f.close();
  if (!complete || !replaceNoteFileAtomically(path, temp)) return false;
  updateIndexMomentCount(num, (int)momentsMs.size());
  return true;
}

std::vector<uint32_t> loadNoteMoments(int num) {
  std::vector<uint32_t> result;
  String path = noteMomentsPath(num);
  recoverStoredFile(SD_MMC, path);
  File f = SD_MMC.open(path.c_str());
  if (!f) return result;
  while (f.available()) {
    String line = f.readStringUntil('\n');
    line.trim();
    if (line.length()) result.push_back((uint32_t)strtoul(line.c_str(), nullptr, 10));
  }
  f.close();
  return result;
}

int noteMomentCount(int num) {
  return (int)loadNoteMoments(num).size();
}

void updateIndexMomentCount(int num, int count) {
  for (int i = 0; i < (int)noteIndex.size(); i++) {
    if (noteIndex[i].num == num) {
      noteIndex[i].momentCount = (uint16_t)max(0, count);
      saveIndex();
      return;
    }
  }
}

// ─── Time helpers ─────────────────────────────────────────────────────────

String noteCreatedUtc(int num) {
  return readNoteMetaValue(num, "created_utc");
}

String utcToLocalDeviceLabel(const String& utcIso) {
  if (utcIso.length() < 16) return "time not set";
  struct tm utc;
  memset(&utc, 0, sizeof(struct tm));
  utc.tm_year = utcIso.substring(0, 4).toInt() - 1900;
  utc.tm_mon  = utcIso.substring(5, 7).toInt() - 1;
  utc.tm_mday = utcIso.substring(8, 10).toInt();
  utc.tm_hour = utcIso.substring(11, 13).toInt();
  utc.tm_min  = utcIso.substring(14, 16).toInt();
  utc.tm_sec  = 0;
  time_t epoch = utcTmToEpoch(utc);
  struct tm localTm;
  setenv("TZ", LOCAL_TZ_RULE, 1);
  tzset();
  localtime_r(&epoch, &localTm);
  char buf[22];
  strftime(buf, sizeof(buf), "%Y-%m-%d %H:%M", &localTm);
  return String(buf);
}

int localUtcOffsetMinutes(const String& utcIso) {
  if (utcIso.length() < 16) return 0;
  struct tm utc = {};
  utc.tm_year = utcIso.substring(0, 4).toInt() - 1900;
  utc.tm_mon  = utcIso.substring(5, 7).toInt() - 1;
  utc.tm_mday = utcIso.substring(8, 10).toInt();
  utc.tm_hour = utcIso.substring(11, 13).toInt();
  utc.tm_min  = utcIso.substring(14, 16).toInt();
  const time_t epoch = utcTmToEpoch(utc);
  setenv("TZ", LOCAL_TZ_RULE, 1);
  tzset();
  struct tm localTm = {};
  localtime_r(&epoch, &localTm);
  return static_cast<int>((utcTmToEpoch(localTm) - epoch) / 60);
}

String noteCreatedDeviceLabel(int num) {
  String utc = noteCreatedUtc(num);
  if (utc.length() < 16) return "time not set";
  return utcToLocalDeviceLabel(utc);
}

String currentUtcIso() {
  String fromRtc = rtcUtcIso();
  if (fromRtc.length() > 0) { timeReady = true; return fromRtc; }
  time_t now = time(nullptr);
  if (!timeReady || now < 1700000000) return "";
  struct tm utc;
  gmtime_r(&now, &utc);
  char buf[25];
  strftime(buf, sizeof(buf), "%Y-%m-%dT%H:%M:%SZ", &utc);
  return String(buf);
}

// ─── Ticker + preview ─────────────────────────────────────────────────────

String notePreviewText(int num, size_t maxLen) {
  char txtPath[64];
  snprintf(txtPath, sizeof(txtPath), "%s/note_%03d.txt", NOTES_DIR, num);
  File f = SD_MMC.open(txtPath);
  if (!f) return "";
  String out = "";
  while (f.available() && out.length() < maxLen) {
    char c = (char)f.read();
    if (c == '\n' || c == '\r' || c == '\t') c = ' ';
    out += c;
  }
  f.close();
  return normalizeForDisplay(out);
}

// ─── Filtering ────────────────────────────────────────────────────────────
