#pragma once
#include <cerrno>
#include <charconv>
#include <cstdio>
#include <cstdint>
#include <ctime>
#include "capture_wav_recovery.h"
#include <string>
#include <sys/stat.h>

namespace rec_spool {
inline int present(const std::string& path) {
  struct stat info{};
  if (::stat(path.c_str(), &info) == 0) return 1;
  return errno == ENOENT ? 0 : -1;
}

inline bool parse(const std::string& name, int& id, std::string& base, uint32_t& epoch) {
  base = name;
  if (!name.empty() && name.front() == 'c') {
    const auto dash = name.find('-');
    if (dash == std::string::npos) return false;
    const auto parsed = std::from_chars(name.data()+1, name.data()+dash, id);
    if (parsed.ec != std::errc{} || parsed.ptr != name.data()+dash || id <= 0) return false;
    base = name.substr(dash+1);
  }
  const auto split = base.find('_');
  if (split == std::string::npos || base.front() != 's' || base.size() != split+13 ||
      base.compare(base.size()-4, 4, ".wav") != 0) return false;
  const auto parsed = std::from_chars(base.data()+1, base.data()+split, epoch);
  uint32_t nonce;
  const auto hex = std::from_chars(base.data()+split+1, base.data()+split+9, nonce, 16);
  return parsed.ec == std::errc{} && parsed.ptr == base.data()+split &&
         hex.ec == std::errc{} && hex.ptr == base.data()+split+9;
}

inline std::string captureUtc(uint32_t seconds) {
  const auto epoch = static_cast<std::time_t>(seconds);
  if (epoch < 1600000000) return {};
  std::tm fields{};
#ifdef _WIN32
  if (gmtime_s(&fields, &epoch) != 0) return {};
#else
  if (!gmtime_r(&epoch, &fields)) return {};
#endif
  char stamp[24]{};
  return std::strftime(stamp, sizeof(stamp), "%Y-%m-%dT%H:%M:%SZ", &fields) ? stamp : "";
}

// Only the boot scanner may promote a partial. After this rename the claim
// and capture.pending carry the recording through its normal adoption path.
inline capture_wav::State promotePartial(const std::string& inbox, const std::string& partial, std::string& name) {
  if (partial.size() < 9 || partial.compare(partial.size()-8, 8, ".partial") != 0)
    return capture_wav::State::Unrecognized;
  name = partial.substr(0, partial.size()-8) + ".wav";
  int id = 0;
  uint32_t epoch = 0;
  std::string base;
  if (!parse(name, id, base, epoch) || id) return capture_wav::State::Unrecognized;
  const auto src = inbox + partial, dst = inbox + name;
  capture_wav::Info info;
  const auto state = capture_wav::inspect(src, info, std::fopen);
  if (state != capture_wav::State::Ready && state != capture_wav::State::Stale) return state;
  return present(dst) == 0 && std::rename(src.c_str(), dst.c_str()) == 0
      ? capture_wav::State::Ready : capture_wav::State::Error;
}

// The claim filename persists the chosen ID until the WAV is renamed. Sidecars
// move first. After the WAV move, capture.pending owns any unfinished commit.
template<class Allocate, class Reserve, class Prepare, class Finish>
bool adopt(const std::string& inbox, const std::string& notes, std::string name,
           int& id, Allocate allocate, Reserve reserve, Prepare prepare, Finish finish) {
  std::string base;
  uint32_t epoch = 0;
  id = 0;
  if (!parse(name, id, base, epoch)) return false;
  if (!id) {
    id = allocate();
    if (id <= 0) return false;
    const auto claimed = std::string("c") + std::to_string(id) + "-" + name;
    if (present(inbox+claimed) != 0 || std::rename((inbox+name).c_str(), (inbox+claimed).c_str()) != 0)
      return false;
    name = claimed;
  }
  char number[30]{};
  std::snprintf(number, sizeof(number), "note_%03d", id);
  const std::string target = notes + number, wav = target + ".wav";
  if (present(wav) != 0 || !reserve(id, epoch)) return false;
  const auto marks = inbox + base.substr(0, base.size()-4) + ".mom";
  const int hasMarks = present(marks);
  if (hasMarks < 0 || (hasMarks && (present(target+".moments") != 0 ||
      std::rename(marks.c_str(), (target+".moments").c_str()) != 0))) return false;
  return prepare(id, epoch) && std::rename((inbox+name).c_str(), wav.c_str()) == 0 && finish(id);
}
}
