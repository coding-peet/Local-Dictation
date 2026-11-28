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

inline std::string storedPath(const std::string& path, note_transcript::Stored state) {
  return state == note_transcript::Stored::Backup ? path + ".bak" : path;
}

// Only the single foreground storage owner calls this. A reboot or lost
// reply turns a previous grant into read-only reconciliation on every peer.
inline Claim claim(const std::string& path, std::string_view digest) {
  if (!validDigest(digest)) return Claim::Conflict;
  const auto state = note_transcript::inspect(path.c_str());
  if (state == note_transcript::Stored::Error) return Claim::Error;
  if (note_transcript::committed(state))
    return note_transcript::sameBytes(storedPath(path, state).c_str(), digest) ? Claim::Reconcile : Claim::Conflict;
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
  if (note_transcript::committed(stored)) {
    auto* file = std::fopen(storedPath(path, stored).c_str(), "rb");
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
  if (note_transcript::committed(previous)) {
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
  const auto receipt = note_transcript::inspect(receiptPath.c_str());
  if (note_transcript::committed(receipt))
    return note_transcript::sameBytes(storedPath(receiptPath, receipt).c_str(), digest);
  if (receipt != note_transcript::Stored::Missing) return false;
  const auto grant = note_transcript::inspect(claimPath.c_str());
  return note_transcript::committed(grant) &&
         note_transcript::sameBytes(storedPath(claimPath, grant).c_str(), digest) &&
         note_transcript::storeOnce(receiptPath.c_str(), digest);
}
}
