#include "../../config.h"
#if PALA_RECORDER_FIRST
#include "recorder_store.h"
#include "../../globals.h"
#include "../../types.h"
#include "capture_wav_recovery.h"
#include "catalog_storage.h"
#include "monthly_recordings.h"
#include "notes.h"
#include "record.h"
#include <algorithm>
#include <charconv>
#include <dirent.h>

namespace {
constexpr char directory[] = "/sdcard/.pala-work";
constexpr size_t maxActive = 4096;
DIR* cursor = nullptr;
bool loaded = false;
std::vector<int> obsolete;

std::string workPath(int note, const char* suffix) {
  return std::string(directory) + "/" + std::to_string(note) + suffix;
}

std::string archivePath(int note, const char* ext) {
  char base[80];
  monthly_recordings::path(base, sizeof(base), "/sdcard" NOTES_DIR, note, ext);
  return base;
}

std::string receiptPath(int note) { return archivePath(note, "delivery"); }

auto findWork(int note) {
  return std::lower_bound(noteIndex.begin(), noteIndex.end(), note,
      [](const NoteEntry& entry, int id) { return entry.num < id; });
}

bool cleanup(int note) {
  for (const char* suffix : {".claim", ".plan", ".plan.tmp", ".queue", ".queue.tmp", ".queue.bak", ".job"})
    if (!note_catalog::removeIfPresent(workPath(note, suffix).c_str())) return false;
  return true;
}

bool loadJob(int note, bool boot) {
  note_catalog::Reservation intent;
  const auto source = note_catalog::readReservation(workPath(note, ".job").c_str(), intent);
  if (source != note_catalog::ReservationState::Valid || intent.id != note ||
      monthly_recordings::makeId(monthly_recordings::sequence(note), intent.utc) != note) return false;
  const auto receipt = note_transcript::inspect(receiptPath(note).c_str());
  if (receipt == note_transcript::Stored::Error) return false;
  if (note_transcript::committed(receipt)) {
    // Require the receipt bytes to be well formed before dropping work.
    FILE* file = std::fopen(receiptPath(note).c_str(), "rb");
    if (!file) return false;
    char digest[65];
    const auto bytes = std::fread(digest, 1, sizeof(digest), file);
    const bool valid = std::feof(file) && !std::ferror(file) &&
                       recorder_work::validDigest({digest, bytes});
    if (std::fclose(file) != 0 || !valid) return false;
    if (boot) obsolete.push_back(note);
    return true;
  }
  const auto wav = archivePath(note, "wav");
  const auto audio = capture_wav::recover(wav);
  if (audio == capture_wav::State::Missing || audio == capture_wav::State::Empty) {
    // A reservation may be durable before the WAV has any useful samples.
    // Retain all existing bytes, even an empty or torn file.
    if (boot) obsolete.push_back(note);
    return boot;
  }
  if (audio != capture_wav::State::Ready) return false;
  const auto transcript = storedNoteTranscript(note);
  if (transcript == note_transcript::Stored::Error) return false;
  const String meta = String("/sdcard") + noteMetaPath(note);
  if (!capture_wav::restoreTimestamp(meta.c_str(), intent.utc,
      [&](const char* utc) { return writeNoteMeta(note, "Inbox", utc); })) return false;

  const auto at = findWork(note);
  const bool hasText = note_transcript::committed(transcript);
  if (at != noteIndex.end() && at->num == note) {
    at->hasText = hasText;
    return true;
  }
  if (noteIndex.size() >= maxActive) return false;
  NoteEntry row{};
  row.num = note;
  strcpy(row.tag, "Inbox");
  row.hasText = hasText;
  noteIndex.insert(at, row);
  return true;
}

int jobNumber(std::string_view name) {
  if (name.size() < 5 || name.substr(name.size() - 4) != ".job") return 0;
  int note = 0;
  const auto* end = name.data() + name.size() - 4;
  const auto parsed = std::from_chars(name.data(), end, note);
  return parsed.ec == std::errc{} && parsed.ptr == end && monthly_recordings::sequence(note) ? note : -1;
}
}

void startRecorderWork() {
  if (cursor) { closedir(cursor); cursor = nullptr; }
  loaded = false;
  obsolete.clear();
  noteIndex.clear();
}

bool recorderWorkReady() { return loaded; }

bool scanRecorderWorkSlice() {
  if (loaded) return true;
  if (!cursor) {
    if (::mkdir(directory, 0777) != 0 && errno != EEXIST) return false;
    cursor = opendir(directory);
    if (!cursor) return false;
  }
  for (int examined = 0; examined < 4 && !pendingPriorityRecording(); ++examined) {
    errno = 0;
    const auto* entry = readdir(cursor);
    if (!entry) {
      const bool complete = !errno;
      const bool closed = closedir(cursor) == 0;
      cursor = nullptr;
      if (!complete || !closed) { startRecorderWork(); return false; }
      // Wait until after the directory scan to delete from FAT.
      for (int note : obsolete) if (!cleanup(note)) { startRecorderWork(); return false; }
      obsolete.clear();
      loaded = true;
      return true;
    }
    const int note = jobNumber(entry->d_name);
    if (note < 0 || (note > 0 && !loadJob(note, true))) {
      startRecorderWork();
      return false;
    }
  }
  return false;
}

bool reserveRecorderWork(int note, const String& utc) {
  if (!loaded || noteIndex.size() >= maxActive) return false;
  const auto job = workPath(note, ".job");
  note_catalog::Reservation prior;
  const auto source = note_catalog::readReservation(job.c_str(), prior);
  if (source == note_catalog::ReservationState::Missing)
    return note_catalog::reserveRecording(job, note, utc.c_str());
  return source == note_catalog::ReservationState::Valid && prior.id == note && utc == prior.utc;
}

bool registerRecorderWork(int note) { return loaded && loadJob(note, false); }

bool visitRecorderWork(bool newestFirst, const std::function<bool(const SyncQueueEntry&)>& visit) {
  if (!loaded) return false;
  for (size_t i = 0; i < noteIndex.size(); ++i) {
    const auto& row = noteIndex[newestFirst ? noteIndex.size() - 1 - i : i];
    if (!visit({row.num, row.hasText ? SyncQueueKind::Organization : SyncQueueKind::Audio})) break;
  }
  return true;
}

SyncQueueCounts recorderWorkCounts() {
  SyncQueueCounts result;
  visitRecorderWork(false, [&](const SyncQueueEntry& entry) {
    if (entry.kind == SyncQueueKind::Audio) ++result.audio;
    else ++result.organization;
    return true;
  });
  return result;
}

bool recorderDeliveryPlan(int note, const String& proposed, String& selected) {
  if (!loaded || !monthly_recordings::sequence(note) || !noteHasCommittedTranscript(note)) return false;
  const auto at = findWork(note);
  if (at == noteIndex.end() || at->num != note || !at->hasText) return false;
  std::string plan;
  if (!recorder_work::plan(workPath(note, ".plan"), workPath(note, ".claim"),
                           {proposed.c_str(), proposed.length()}, plan)) return false;
  selected = plan.c_str();
  return true;
}

recorder_work::Claim claimRecorderDelivery(int note, const String& digest) {
  if (!loaded || !noteHasCommittedTranscript(note)) return recorder_work::Claim::Error;
  const auto at = findWork(note);
  if (at == noteIndex.end() || at->num != note || !at->hasText) return recorder_work::Claim::Error;
  return recorder_work::claim(workPath(note, ".claim"), {digest.c_str(), digest.length()});
}

bool finishRecorderDelivery(int note, const String& digest) {
  if (!loaded || !monthly_recordings::sequence(note) || !noteHasCommittedTranscript(note)) return false;
  if (!recorder_work::complete(workPath(note, ".claim"), receiptPath(note), {digest.c_str(), digest.length()}))
    return false;
  if (!cleanup(note)) return false;
  const auto at = findWork(note);
  if (at != noteIndex.end() && at->num == note) noteIndex.erase(at);
  return true;
}

#endif
