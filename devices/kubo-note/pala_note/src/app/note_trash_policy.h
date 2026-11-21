#pragma once

#include <cstdint>
#include <string_view>

namespace note_trash {
constexpr int defaultRetentionDays = 90;
constexpr int purgeBatchSize = 8;
constexpr const char* bundleExtensions[] = {
  "wav", "txt", "meta", "queue", "moments", "brain.json", "brain.vec", "mac.vec", "bridge-owner"
};
constexpr const char* commitSuffixes[] = {"", ".tmp", ".bak"};
enum class Phase { None, Moving, Parked, Restoring, Purging, Ambiguous };

constexpr bool clearable(Phase phase) { return phase == Phase::Parked || phase == Phase::Purging; }

constexpr bool validRetention(int days) { return days >= 1 && days <= 3650; }
constexpr bool validEpoch(int64_t epoch) { return epoch >= 1700000000 && epoch < 4102444800LL; }

constexpr int64_t retentionStart(int64_t trashedAt, int64_t laterAnchor) {
  return validEpoch(trashedAt) ? trashedAt : validEpoch(laterAnchor) ? laterAnchor : 0;
}

constexpr bool expired(int64_t deletedAt, int64_t now, int days) {
  return validRetention(days) && validEpoch(deletedAt) && validEpoch(now)
      && now - deletedAt >= int64_t(days) * 86400;
}

constexpr char asciiLower(char letter) {
  return letter >= 'A' && letter <= 'Z' ? static_cast<char>(letter + ('a' - 'A')) : letter;
}

constexpr bool equalsAscii(std::string_view name, std::string_view expectedLowercase) {
  if (name.size() != expectedLowercase.size()) return false;
  for (size_t i = 0; i < name.size(); ++i) {
    if (asciiLower(name[i]) != expectedLowercase[i]) return false;
  }
  return true;
}

constexpr int folderNumber(std::string_view name) {
  if (name.size() < 8 || name.size() > 15 || !equalsAscii(name.substr(0, 5), "note_")) return 0;
  if (name.size() > 8 && name[5] == '0') return 0;
  unsigned number = 0;
  for (char digit : name.substr(5)) {
    if (digit < '0' || digit > '9') return 0;
    const unsigned value = digit - '0';
    if (number > (2147483647U - value) / 10) return 0;
    number = number * 10 + value;
  }
  return static_cast<int>(number);
}

constexpr int fileNumber(std::string_view name) {
  const auto dot = name.find('.');
  if (dot == name.npos) return 0;
  const int number = folderNumber(name.substr(0, dot));
  if (!number) return 0;
  auto extension = name.substr(dot + 1);
  if (extension.size() >= 4) {
    const auto suffix = extension.substr(extension.size() - 4);
    if (equalsAscii(suffix, ".tmp") || equalsAscii(suffix, ".bak")) extension.remove_suffix(4);
  }
  for (const char* known : bundleExtensions) if (equalsAscii(extension, known)) return number;
  return 0;
}

template<class FileSystem>
constexpr bool relocate(FileSystem& fs, const char* source, const char* destination) {
  if (!fs.exists(source)) return true;
  return !fs.exists(destination) && fs.rename(source, destination);
}
}  // namespace note_trash
