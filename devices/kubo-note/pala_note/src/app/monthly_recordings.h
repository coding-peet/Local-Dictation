#pragma once

#include <cstdio>
#include <cstdint>
#include <string_view>
#include "date_validation.h"

namespace monthly_recordings {

constexpr uint32_t idFlag = uint32_t{1} << 30;
constexpr unsigned monthBits = 10;
constexpr uint32_t monthMask = (1u << monthBits) - 1;
constexpr uint32_t maxSequence = (idFlag - 1) >> monthBits;

inline uint32_t sequence(int id) {
  return id > 0 && (uint32_t(id) & idFlag) ? (uint32_t(id) & (idFlag - 1)) >> monthBits : 0;
}

inline int makeId(uint32_t next, const char* utc) {
  if (!next || next > maxSequence || !utc || (*utc && !validUtcTimestamp(utc))) return 0;
  unsigned month = 0;
  if (*utc) {
    const unsigned year = (utc[0]-'0') * 1000 + (utc[1]-'0') * 100 + (utc[2]-'0') * 10 + utc[3]-'0';
    month = (year - 2020) * 12 + (utc[5]-'0') * 10 + utc[6]-'0';
  }
  return static_cast<int>(idFlag | (next << monthBits) | month);
}

inline int folderMonth(int id) {
  const unsigned month = uint32_t(id) & monthMask;
  return month && month <= 972 ? (2020 + (month - 1) / 12) * 100 + (month - 1) % 12 + 1 : 0;
}

inline bool archiveExtension(std::string_view extension) {
  const auto base = extension.substr(0, extension.find('.'));
  return base == "wav" || base == "txt" || base == "delivery";
}

// Loaded once from the card before work replay. Zero identifies Test 1 paths
// until the first new ordinal is committed on this card.
inline uint32_t firstNewSequence = 0;

inline bool inRecordings(int id) {
  return firstNewSequence && sequence(id) >= firstNewSequence;
}

inline const char* archiveRoot(const char* root, int id) {
  if (!inRecordings(id)) return root;
  const std::string_view value(root);
  if (value == "/notes") return "/recordings";
  if (value == "/sdcard/notes") return "/sdcard/recordings";
  return root;
}

inline void path(char* out, size_t capacity, const char* root, int id, const char* extension) {
  const auto base = std::string_view(extension).substr(0, std::string_view(extension).find('.'));
  if (sequence(id) && (archiveExtension(extension) || (inRecordings(id) && base == "moments")))
    std::snprintf(out, capacity, "%s/%06d/note_%03d.%s", archiveRoot(root, id), folderMonth(id), id, extension);
  else
    std::snprintf(out, capacity, "%s/note_%03d.%s", root, id, extension);
}

}  // namespace monthly_recordings
