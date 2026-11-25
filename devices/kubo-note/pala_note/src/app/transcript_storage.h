#pragma once

#include <cerrno>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <string_view>
#include <sys/stat.h>

namespace note_transcript {
enum class Stored { Missing, Primary, Backup, Error };
constexpr bool committed(Stored state) { return state == Stored::Primary || state == Stored::Backup; }
constexpr bool canSendAudio(Stored state, bool indexed) { return state == Stored::Missing && !indexed; }

inline Stored inspect(const char* primary) {
  struct stat info;
  if (::stat(primary, &info) == 0)
    return (info.st_mode & S_IFMT) == S_IFREG && info.st_size > 0 ? Stored::Primary : Stored::Error;
  if (errno != ENOENT) return Stored::Error;
  const auto backup = std::string(primary) + ".bak";
  if (::stat(backup.c_str(), &info) == 0)
    return (info.st_mode & S_IFMT) == S_IFREG && info.st_size > 0 ? Stored::Backup : Stored::Error;
  return errno == ENOENT ? Stored::Missing : Stored::Error;
}

// Sidecar fields are short. A field in the backup is authoritative only
// when the primary really is absent. Read failures must not invent a route
// or discard the original capture timestamp when rewriting metadata.
inline bool readField(const char* path, const char* prefix, std::string& value) {
  value.clear();
  auto* file = std::fopen(path, "rb");
  if (!file && errno == ENOENT) file = std::fopen((std::string(path) + ".bak").c_str(), "rb");
  if (!file) return errno == ENOENT;
  char line[192];
  const size_t keyLength = std::strlen(prefix);
  bool ok = true;
  bool continued = false;
  while (std::fgets(line, sizeof(line), file)) {
    std::string_view part(line);
    const bool ended = !part.empty() && part.back() == '\n';
    if (!continued && part.substr(0, keyLength) == prefix) {
      if (!ended && !std::feof(file)) { ok = false; break; }
      if (ended) part.remove_suffix(1);
      if (!part.empty() && part.back() == '\r') part.remove_suffix(1);
      value.assign(part.substr(keyLength));
      break;
    }
    continued = !ended;
  }
  ok = ok && !std::ferror(file);
  return std::fclose(file) == 0 && ok;
}

inline bool writeClosed(const char* path, const void* bytes, size_t count) {
  auto* file = std::fopen(path, "wb");
  if (!file) return false;
  const bool written = std::fwrite(bytes, 1, count, file) == count && std::fflush(file) == 0;
  return std::fclose(file) == 0 && written;
}

// A single writer publishes its note ID before publishing text. A reboot
// needs one direct lookup even when the catalog has tens of thousands of rows.
inline bool reserve(const char* journal, int32_t id) {
  const std::string temporary = std::string(journal) + ".tmp";
  struct stat info;
  if (id <= 0 || ::stat(journal, &info) == 0 || errno != ENOENT) return false;
  return writeClosed(temporary.c_str(), &id, sizeof(id)) &&
         std::rename(temporary.c_str(), journal) == 0;
}

template<class Repair>
bool replay(const char* journal, Repair repair) {
  auto* file = std::fopen(journal, "rb");
  if (!file) return errno == ENOENT;
  int32_t id = 0;
  bool valid = std::fread(&id, sizeof(id), 1, file) == 1 && id > 0;
  valid = valid && std::fgetc(file) == EOF && !std::ferror(file);
  return std::fclose(file) == 0 && valid && repair(id) && std::remove(journal) == 0;
}

inline bool sameBytes(const char* path, std::string_view expected) {
  auto* file = std::fopen(path, "rb");
  if (!file) return false;
  char chunk[256];
  bool same = true;
  while (same && !expected.empty()) {
    const size_t n = expected.size() < sizeof(chunk) ? expected.size() : sizeof(chunk);
    same = std::fread(chunk, 1, n, file) == n && std::memcmp(chunk, expected.data(), n) == 0;
    expected.remove_prefix(n);
  }
  same = same && std::fgetc(file) == EOF && !std::ferror(file);
  return std::fclose(file) == 0 && same;
}

inline bool storeOnce(const char* path, std::string_view bytes) {
  if (bytes.empty()) return false;
  const auto state = inspect(path);
  if (state == Stored::Primary) return sameBytes(path, bytes);
  if (state == Stored::Backup) return sameBytes((std::string(path) + ".bak").c_str(), bytes);
  if (state != Stored::Missing) return false;
  const std::string temporary = std::string(path) + ".tmp";
  return writeClosed(temporary.c_str(), bytes.data(), bytes.size()) &&
         inspect(path) == Stored::Missing && std::rename(temporary.c_str(), path) == 0;
}

inline bool restoreBackup(const char* path, Stored state) {
  return state == Stored::Primary ||
         (state == Stored::Backup && std::rename((std::string(path) + ".bak").c_str(), path) == 0);
}
template<class PersistCatalog, class Route, class StartOrganization>
bool repairCommitted(const char* path, PersistCatalog persistCatalog, Route readRoute,
                     StartOrganization startOrganization) {
  const auto source = inspect(path);
  if (source == Stored::Missing) return true;  // Power failed before publication.
  if (!restoreBackup(path, source) || !persistCatalog()) return false;
  const auto route = readRoute();
  if (route == "storage-error") return false;
  return (route != "transcribing" && route != "queued" && route != "retry") || startOrganization();
}

template<class Reconcile, class Transcribe>
bool useCommittedOrTranscribe(Stored source, Reconcile reconcile, Transcribe transcribe) {
  return source == Stored::Missing ? transcribe() : committed(source) && reconcile();
}
}  // namespace note_transcript
