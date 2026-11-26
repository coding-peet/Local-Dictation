#pragma once

#include <cerrno>
#include <cstdio>
#include <string>
#include <string_view>
#include "transcript_storage.h"

namespace recorder_work {
enum class Claim { Granted, Reconcile, Conflict, Error };

inline bool validDigest(std::string_view digest) {
  if (digest.size() != 64) return false;
  for (char c : digest)
    if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return false;
  return true;
}

// Only the single foreground storage owner calls this. A reboot or lost
// reply turns a previous grant into read-only reconciliation on every peer.
inline Claim claim(const std::string& path, std::string_view digest) {
  if (!validDigest(digest)) return Claim::Conflict;
  const auto state = note_transcript::inspect(path.c_str());
  if (state == note_transcript::Stored::Error) return Claim::Error;
  if (state == note_transcript::Stored::Backup) return Claim::Error;
  if (state == note_transcript::Stored::Primary)
    return note_transcript::sameBytes(path.c_str(), digest) ? Claim::Reconcile : Claim::Conflict;
  return note_transcript::storeOnce(path.c_str(), digest) ? Claim::Granted : Claim::Error;
}

inline bool validPlan(std::string_view plan) {
  if (plan == "V2") return true;
  if (plan.size() < 4 || plan.size() > 123 || plan.substr(0, 3) != "V2.") return false;
  for (char c : plan.substr(3)) {
    if (!(c >= 'A' && c <= 'Z') && !(c >= 'a' && c <= 'z') &&
        !(c >= '0' && c <= '9') && c != '-' && c != '_') return false;
  }
  return true;
}

// The first complete plan wins. A pre-upgrade claim still uses its old route.
inline bool plan(const std::string& path, const std::string& claimPath,
                 std::string_view proposed, std::string& selected) {
  selected.clear();
  if (!proposed.empty() && !validPlan(proposed)) return false;
  const auto stored = note_transcript::inspect(path.c_str());
  if (stored == note_transcript::Stored::Primary) {
    auto* file = std::fopen(path.c_str(), "rb");
    if (!file) return false;
    char bytes[124];
    const size_t len = std::fread(bytes, 1, sizeof(bytes), file);
    const bool done = std::feof(file) && !std::ferror(file);
    if (std::fclose(file) != 0 || !done || !validPlan({bytes, len})) return false;
    selected.assign(bytes, len);
    return true;
  }
  if (stored != note_transcript::Stored::Missing) return false;
  const auto previous = note_transcript::inspect(claimPath.c_str());
  if (previous == note_transcript::Stored::Primary) {
    selected = "LEGACY";
    return true;
  }
  if (previous != note_transcript::Stored::Missing) return false;
  if (proposed.empty()) return true;
  if (!note_transcript::storeOnce(path.c_str(), proposed)) return false;
  selected.assign(proposed);
  return true;
}

inline bool complete(const std::string& claimPath, const std::string& receiptPath, std::string_view digest) {
  if (!validDigest(digest)) return false;
  if (note_transcript::committed(note_transcript::inspect(receiptPath.c_str())))
    return note_transcript::sameBytes(receiptPath.c_str(), digest);
  return note_transcript::sameBytes(claimPath.c_str(), digest) &&
         note_transcript::storeOnce(receiptPath.c_str(), digest);
}
}
