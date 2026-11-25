#pragma once
#include <cerrno>
#include <cstdio>
#include <string>

namespace recording_owner {
enum class Read { Missing, Found, Error };

inline bool valid(const std::string& value) {
  if (value.empty() || value.size() > 32) return false;
  for (const auto ch : value) {
    if ((ch < '0' || ch > '9') && (ch < 'A' || ch > 'Z') &&
        (ch < 'a' || ch > 'z') && ch != '_' && ch != '-') return false;
  }
  return true;
}

inline Read read(const std::string& path, std::string& owner) {
  auto* file = std::fopen(path.c_str(), "rb");
  if (!file) return errno == ENOENT ? Read::Missing : Read::Error;
  char id[33];
  const auto bytes = std::fread(id, 1, sizeof(id), file);
  const bool complete = std::feof(file) && !std::ferror(file);
  if (std::fclose(file) || !complete) return Read::Error;
  owner.assign(id, bytes);
  return valid(owner) ? Read::Found : Read::Error;
}

// Called on the foreground loop before releasing any audio or organization
// request. The capture worker never creates or modifies these receipts.
inline bool resolve(const std::string& path, const std::string& candidate, std::string& owner) {
  for (const auto& name : {path, path + ".bak"}) {
    const auto result = read(name, owner);
    if (result != Read::Missing) return result == Read::Found;
  }
  if (!valid(candidate)) return false;
  const auto tmp = path + ".tmp";
  auto* file = std::fopen(tmp.c_str(), "wb");
  if (!file) return false;
  const bool written = std::fwrite(candidate.data(), 1, candidate.size(), file) == candidate.size();
  if (std::fclose(file) || !written || std::rename(tmp.c_str(), path.c_str())) return false;
  return read(path, owner) == Read::Found && owner == candidate;
}
}
