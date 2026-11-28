#include "sync_queue.h"
#include "owner_trace.h"
#include "audit_projection.h"
#include "audit_progress.h"
#include "record.h"

#include "../../config.h"
#include "../../globals.h"
#include "brain.h"
#include "notes.h"
#include "recorder_store.h"
#include "storage_commit.h"
#include <SD_MMC.h>
#include <esp_attr.h>
#include <esp_system.h>

namespace {
constexpr char queuePath[] = NOTES_DIR "/sync.pending.v1";
constexpr char candidatePath[] = NOTES_DIR "/sync.pending.v1.tmp";
constexpr char dirtyPath[] = NOTES_DIR "/sync.pending.dirty";
constexpr uint32_t queueMagic = 0x31514c50;
constexpr uint32_t retainedMagic = 0x51524c31;
constexpr size_t migrationSlice = 4;

struct RecoveryCursor {
  uint32_t magic = 0;
  int lastNote = 0;
  size_t bytes = 0;
};

RTC_DATA_ATTR RecoveryCursor retained;
RecoveryCursor cursor;
bool initialized = false;
bool ready = false;
bool building = false;
int depth = 0;
int changedNote = 0;
SyncQueueCounts counts;

bool markDirty() {
  if (SD_MMC.exists(dirtyPath)) return true;
  File marker = SD_MMC.open(dirtyPath, FILE_WRITE);
  if (!marker) return false;
  const bool written = marker.write(static_cast<uint8_t>(1)) == 1;
  marker.flush();
  return written && marker.size() == 1;
}

bool queueFileHeader(File& file, size_t& elements) {
  if (!file || file.isDirectory() || file.size() < sizeof(queueMagic)) return false;
  const size_t bytes = file.size() - sizeof(queueMagic);
  uint32_t magic = 0;
  if (bytes % sizeof(SyncQueueEntry) != 0 ||
      file.read(reinterpret_cast<uint8_t*>(&magic), sizeof(magic)) != sizeof(magic) ||
      magic != queueMagic) return false;
  elements = bytes / sizeof(SyncQueueEntry);
  return true;
}

bool inspectQueue() {
  File file = SD_MMC.open(queuePath, FILE_READ);
  size_t elements = 0;
  SyncQueueCounts checked;
  uint32_t rows = 0;
  if (!queueFileHeader(file, elements) ||
      !visitSyncQueueEntries(file, elements, false, [&](const SyncQueueEntry& entry) {
        noteAuditWork(NoteStorageAuditPhase::LoadQueue, ++rows, elements);
        if (entry.kind == SyncQueueKind::Audio) ++checked.audio;
        if (entry.kind == SyncQueueKind::Organization) ++checked.organization;
        return true;
      }, sizeof(queueMagic))) return false;
  counts = checked;
  return true;
}

File openCandidate() {
  File file = SD_MMC.open(candidatePath, FILE_WRITE);
  if (!file || file.write(reinterpret_cast<const uint8_t*>(&queueMagic), sizeof(queueMagic)) != sizeof(queueMagic))
    return {};
  return file;
}

bool verifyReplacement = false;
bool verifyQueueFile(const char* path, bool finalPass = false);

bool publishCandidate() {
  if (verifyReplacement && !verifyQueueFile(candidatePath)) return false;
  if (!commitStoredFile(SD_MMC, String(queuePath), String(candidatePath)) || !inspectQueue()) return false;
  const bool cleared = !SD_MMC.exists(dirtyPath) || SD_MMC.remove(dirtyPath);
  if (cleared) verifyReplacement = false;
  return cleared;
}

bool classify(const NoteEntry& note, SyncQueueKind& kind) {
  kind = SyncQueueKind::None;
  if (noteIsTrashed(note.num)) return true;
  const String route = noteQueueState(note.num);
  if (route == "storage-error") return false;
  if (route == "blocked" || route == "usb-local") return true;
  const auto source = storedNoteTranscript(note.num);
  if (source == note_transcript::Stored::Error || (note.hasText && source == note_transcript::Stored::Missing)) return false;
  if (source == note_transcript::Stored::Missing) { kind = SyncQueueKind::Audio; return true; }
  bool current, embedding;
  if (!readBrainCurrency(note.num, current)) return false;
  if (!current) kind = SyncQueueKind::Organization;
  else if (!readBrainEmbeddingState(note.num, embedding)) return false;
  else if (!embedding) kind = SyncQueueKind::Embedding;
  return true;
}

bool verifyQueueFile(const char* path, bool finalPass) {
  File file = SD_MMC.open(path, FILE_READ);
  size_t count = 0, examined = 0;
  if (!noteCatalogBaseLoaded() || !queueFileHeader(file, count)) return false;
  return queueMatchesSource(noteIndex, [&](const NoteEntry& note, SyncQueueKind& kind) {
    noteAuditWork(finalPass || path == candidatePath ? NoteStorageAuditPhase::VerifyQueue :
                  NoteStorageAuditPhase::QueueSources, examined, noteIndex.size());
    if ((++examined & 127) == 0) delay(1);
    return classify(note, kind);
  }, [&](auto consume) { return visitSyncQueueEntries(file, count, false, consume, sizeof(queueMagic)); });
}

auto firstAfter(int note) {
  return std::upper_bound(noteIndex.begin(), noteIndex.end(), note,
                          [](int number, const NoteEntry& item) { return number < item.num; });
}

bool updateQueue(int note) {
  const auto found = firstAfter(note - 1);
  SyncQueueKind kind = SyncQueueKind::None;
  if (found != noteIndex.end() && found->num == note && !classify(*found, kind)) return false;
  File file = SD_MMC.open(queuePath, FILE_READ);
  size_t elements = 0;
  if (!queueFileHeader(file, elements)) return false;
  SyncQueueKind previous = SyncQueueKind::None;
  if (!visitSyncQueueEntries(file, elements, false, [&](const auto& entry) {
        if (entry.note == note) previous = entry.kind;
        return entry.note < note;
      }, sizeof(queueMagic))) return false;
  if (previous == kind) return SD_MMC.remove(dirtyPath);
  File candidate = openCandidate();
  if (!candidate) return false;
  const bool copied = rewriteSyncQueueEntries(file, elements, candidate, {note, kind}, sizeof(queueMagic));
  candidate.flush();
  candidate.close();
  file.close();
  return copied && publishCandidate();
}

void invalidate() {
  ready = false;
  building = false;
  cursor = {};
}
}

void initializeSyncQueue() {
  owner_trace::WorkScope ownerWork(owner_trace::Work::Queue);
#if PALA_RECORDER_FIRST
  return;
#endif
  initialized = true;
  recoverStoredFile(SD_MMC, String(queuePath));
  ready = !SD_MMC.exists(dirtyPath) && inspectQueue();
  building = false;
  cursor = {};
  if (!ready && esp_reset_reason() == ESP_RST_DEEPSLEEP && retained.magic == retainedMagic) {
    File candidate = SD_MMC.open(candidatePath, FILE_READ);
    size_t elements = 0;
    if (queueFileHeader(candidate, elements) && candidate.size() == retained.bytes) {
      cursor = retained;
      building = true;
    }
  }
  retained = {};
}

void retainSyncQueueRecovery() {
  retained = building ? cursor : RecoveryCursor{};
}

bool requestSyncQueueRebuild(bool verifySources) {
#if PALA_RECORDER_FIRST
  startRecorderWork();
  return true;
#endif
  if (!markDirty()) return false;
  verifyReplacement |= verifySources;
  invalidate();
  retained = {};
  return true;
}

bool syncQueueReady() {
#if PALA_RECORDER_FIRST
  return recorderWorkReady();
#else
  return ready;
#endif
}

bool verifySyncQueueSources(bool finalPass) {
#if PALA_RECORDER_FIRST
  return recorderWorkReady();
#else
  return ready && verifyQueueFile(queuePath, finalPass);
#endif
}
int syncQueueRebuildProgressNote() { return cursor.lastNote; }
SyncQueueCounts syncQueueCounts() {
#if PALA_RECORDER_FIRST
  return recorderWorkCounts();
#else
  return ready ? counts : SyncQueueCounts{};
#endif
}

bool maintainSyncQueue() {
  owner_trace::WorkScope ownerWork(owner_trace::Work::Queue);
#if PALA_RECORDER_FIRST
  return scanRecorderWorkSlice();
#endif
  if (ready || !initialized || depth) return ready;
  if (!building) {
    if (!markDirty()) return false;
    File candidate = openCandidate();
    if (!candidate) return false;
    candidate.flush();
    candidate.close();
    cursor = {retainedMagic, 0, sizeof(queueMagic)};
    building = true;
  }

  std::array<SyncQueueEntry, migrationSlice> pending{};
  size_t written = 0;
  int last = cursor.lastNote;
  auto it = firstAfter(last);
  noteAuditWork(NoteStorageAuditPhase::BuildQueue, static_cast<uint32_t>(it - noteIndex.begin()), noteIndex.size());
  for (size_t attempts = 0; it != noteIndex.end() && attempts < migrationSlice; ++it, ++attempts) {
    if (pendingPriorityRecording()) break;
    last = it->num;
    SyncQueueKind kind;
    if (!classify(*it, kind)) return false;
    if (kind != SyncQueueKind::None) pending[written++] = {last, kind};
  }
  if (written) {
    File file = SD_MMC.open(candidatePath, FILE_APPEND);
    if (!file) { invalidate(); return false; }
    const size_t bytes = written * sizeof(SyncQueueEntry);
    const bool appended = file.write(reinterpret_cast<const uint8_t*>(pending.data()), bytes) == bytes;
    file.flush();
    if (!appended || file.size() != cursor.bytes + bytes) { invalidate(); return false; }
    cursor.bytes += bytes;
  }
  cursor.lastNote = last;
  if (it != noteIndex.end()) return false;
  ready = publishCandidate();
  building = false;
  if (ready) Serial.printf("[Storage] queue index ready, %d audio, %d organization\n", counts.audio, counts.organization);
  return ready;
}

bool visitSyncQueue(bool newestFirst, const std::function<bool(const SyncQueueEntry&)>& visit) {
  owner_trace::WorkScope ownerWork(owner_trace::Work::Queue);
#if PALA_RECORDER_FIRST
  return visitRecorderWork(newestFirst, visit);
#endif
  if (!ready) return false;
  File file = SD_MMC.open(queuePath, FILE_READ);
  size_t size = 0;
  if (queueFileHeader(file, size) &&
      visitSyncQueueEntries(file, size, newestFirst, visit, sizeof(queueMagic))) return true;
  invalidate();
  return false;
}

SyncQueueKind syncQueueKindFor(int note) {
  SyncQueueKind kind = SyncQueueKind::None;
  visitSyncQueue(false, [&](const SyncQueueEntry& entry) {
    if (entry.note == note) kind = entry.kind;
    return entry.note < note;
  });
  return kind;
}

SyncQueueChange::SyncQueueChange(int note) {
#if PALA_RECORDER_FIRST
  accepted_ = recorderWorkReady();
  return;
#endif
  // Boot repairs happen before a queue can be served. Invalidate an old
  // snapshot even when the repair itself precedes queue initialization.
  if (!depth && !markDirty()) return;
  if (depth && changedNote != 0 && changedNote != note) return;
  if (!depth) changedNote = note;
  ++depth;
  accepted_ = true;
}

SyncQueueChange::~SyncQueueChange() {
#if PALA_RECORDER_FIRST
  return;
#endif
  if (!accepted_ || --depth != 0) return;
  if (ready && changedNote > 0 && updateQueue(changedNote)) return;
  if (ready || changedNote == 0 || (building && changedNote <= cursor.lastNote)) invalidate();
}
