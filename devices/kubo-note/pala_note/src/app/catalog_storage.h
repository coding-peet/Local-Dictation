#pragma once

#include <algorithm>
#include <cerrno>
#include <cstdio>
#include <cstring>
#include <vector>
#include "storage_audit_policy.h"
#include "date_validation.h"
#include <array>
#include <string>
#include "../../types.h"

namespace note_catalog {
using OpenFile = std::FILE* (*)(const char*, const char*);
enum class Source { Present, Absent, Error };
inline Source openError() { return errno == ENOENT ? Source::Absent : Source::Error; }

inline bool removeIfPresent(const char* path) {
  return std::remove(path) == 0 || errno == ENOENT;
}

// Only publish a snapshot after clean EOF and a successful close. Refuse to
// publish a partial or unparseable catalog, including a truncated long row.
template<class Keep>
Source readCatalog(const char* path, std::vector<NoteEntry>& rows, Keep keep, OpenFile open) {
  auto* file = open(path, "rb");
  if (!file) return openError();
  char line[96];
  bool ok = true;
  while (ok && std::fgets(line, sizeof(line), file)) {
    std::string_view text(line);
    if (!text.empty() && text.back() == '\n') text.remove_suffix(1);
    if (!text.empty() && text.back() == '\r') text.remove_suffix(1);
    if (text.empty()) continue;
    const auto row = note_maintenance::parseCatalogRow(text);
    if (!row.valid) { ok = false; break; }
    const auto visible = keep(row.note);
    if (visible == Source::Error) { ok = false; break; }
    if (visible == Source::Absent) continue;
    NoteEntry note{};
    note.num = row.note;
    note.hasText = row.hasTranscript;
    note.momentCount = row.moments;
    const auto first = text.find(',') + 1;
    text.copy(note.tag, text.find(',', first) - first, first);
    rows.push_back(note);
  }
  ok = !std::ferror(file) && ok;
  if (std::fclose(file) != 0 || !ok) return Source::Error;
  return Source::Present;
}

struct Snapshot {
  bool loaded = false;
  bool needsDirectory = false;

  template<class Keep>
  bool load(std::vector<NoteEntry>& active, const char* primary, const char* backup,
            const char* legacyBackup, const char* fastMarker, Keep keep, OpenFile open = std::fopen) {
    if (loaded) return true;  // Readers share the in-progress migration's live entries.
    std::vector<NoteEntry> staging;
    auto state = readCatalog(primary, staging, keep, open);
    if (state == Source::Error) return false;
    if (state == Source::Absent) {
      // Invalidate the fast path durably before restoring an older generation.
      if (!removeIfPresent(fastMarker)) return false;
      for (const char* candidate : {backup, legacyBackup}) {
        state = readCatalog(candidate, staging, keep, open);
        if (state == Source::Error) return false;
        if (state == Source::Present) {
          if (std::rename(candidate, primary) != 0) return false;
          break;
        }
      }
      needsDirectory = true;
    }
    std::sort(staging.begin(), staging.end(),
              [](const NoteEntry& a, const NoteEntry& b) { return a.num < b.num; });
    active.swap(staging);
    loaded = true;
    return true;
  }
};

// The Companion API pages newest-first over the live catalog. It never opens
// the backing CSV, which can lag behind discoveries until migration publishes.
struct PortalPage {
  const std::vector<NoteEntry>& notes;
  int offset, limit, end;
  PortalPage(const std::vector<NoteEntry>& entries, int start, int count)
      : notes(entries), offset(std::clamp(start, 0, static_cast<int>(notes.size()))),
        limit(std::clamp(count, 1, 50)),
        end(offset + std::min(limit, static_cast<int>(notes.size()) - offset)) {}
  const NoteEntry& at(int index) const { return notes[notes.size() - 1 - index]; }
};

template<class Write>
bool writeRows(const std::vector<NoteEntry>& rows, Write write, size_t& bytes) {
  bytes = 0;
  if (rows.empty()) { bytes = 1; return write("\n", 1) == 1; }
  char line[64];
  for (const auto& row : rows) {
    const int size = std::snprintf(line, sizeof(line), "%d,%s,%d,%u\n",
                                   row.num, row.tag, row.hasText ? 1 : 0, unsigned(row.momentCount));
    if (size <= 0 || size >= static_cast<int>(sizeof(line)) ||
        write(line, static_cast<size_t>(size)) != static_cast<size_t>(size)) return false;
    bytes += static_cast<size_t>(size);
  }
  return true;
}

inline Source recordingSize(const char* path, long& bytes, OpenFile open = std::fopen) {
  auto* file = open(path, "rb");
  if (!file) return openError();
  bytes = std::fseek(file, 0, SEEK_END) == 0 ? std::ftell(file) : -1;
  if (std::fclose(file) != 0 || bytes < 0) return Source::Error;
  return Source::Present;
}

template<class Insert>
bool recoverRecording(std::vector<NoteEntry>& notes, int id, const char* wavPath,
                      Insert insert, OpenFile open = std::fopen, unsigned minimumPcm = 1001) {
  const auto at = std::lower_bound(notes.begin(), notes.end(), id,
                                   [](const NoteEntry& note, int n) { return note.num < n; });
  if (at != notes.end() && at->num == id) return true;
  long bytes = 0;
  const auto source = recordingSize(wavPath, bytes, open);
  if (source == Source::Error) return false;
  if (source == Source::Absent || bytes < static_cast<long>(44 + minimumPcm)) return true;
  return insert(at);
}

// Legacy cards contain a four-byte ID. New reservations atomically publish
// the same ID, a version tag, and the capture UTC before opening the WAV.
constexpr size_t reservationBytes = sizeof(int32_t) + 4 + 21;
inline bool reserveRecording(const std::string& path, int32_t id, const char* utc) {
  if (id <= 0 || !utc || (*utc && !validUtcTimestamp(utc))) return false;
  auto* prior = std::fopen(path.c_str(), "rb");
  if (prior) { std::fclose(prior); return false; }
  if (errno != ENOENT) return false;
  std::array<char, reservationBytes> bytes{};
  std::memcpy(bytes.data(), &id, sizeof(id));
  std::memcpy(bytes.data()+sizeof(id), "UTC1", 4);
  std::memcpy(bytes.data()+sizeof(id)+4, utc, std::strlen(utc));
  const auto tmp = path + ".tmp";
  auto* out = std::fopen(tmp.c_str(), "wb");
  if (!out) return false;
  const bool written = std::fwrite(bytes.data(), 1, bytes.size(), out) == bytes.size() && std::fflush(out) == 0;
  return std::fclose(out) == 0 && written && std::rename(tmp.c_str(), path.c_str()) == 0;
}

enum class ReservationState { Missing, Empty, Valid, Error };
struct Reservation {
  int32_t id = 0;
  char utc[21]{};
};

inline ReservationState readReservation(const char* path, Reservation& record, OpenFile open = std::fopen) {
  auto* file = open(path, "rb");
  if (!file) return openError() == Source::Absent ? ReservationState::Missing : ReservationState::Error;
  std::array<char, reservationBytes + 1> bytes{};
  const size_t len = std::fread(bytes.data(), 1, bytes.size(), file);
  const bool readOk = !std::ferror(file) && std::feof(file);
  if (std::fclose(file) != 0 || !readOk) return ReservationState::Error;
  if (len < sizeof(record.id)) return ReservationState::Empty;
  record = {};
  std::memcpy(&record.id, bytes.data(), sizeof(record.id));
  if (len != sizeof(record.id)) {
    if (len != reservationBytes || std::memcmp(bytes.data()+sizeof(record.id), "UTC1", 4) ||
        bytes[reservationBytes-1] != 0) return ReservationState::Error;
    std::memcpy(record.utc, bytes.data()+sizeof(record.id)+4, sizeof(record.utc));
    if (*record.utc && !validUtcTimestamp(record.utc)) return ReservationState::Error;
  }
  return record.id > 0 ? ReservationState::Valid : ReservationState::Error;
}

template<class Recover>
bool recoverReservation(const char* path, Recover recover, OpenFile open = std::fopen) {
  Reservation record;
  switch (readReservation(path, record, open)) {
    case ReservationState::Missing: return true;
    case ReservationState::Empty: return removeIfPresent(path); // old writer never started audio
    case ReservationState::Valid: return recover(record.id, record.utc) && removeIfPresent(path);
    case ReservationState::Error: return false;
  }
  return false;
}
}  // namespace note_catalog
