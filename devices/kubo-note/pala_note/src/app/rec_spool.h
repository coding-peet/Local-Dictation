#pragma once
#include <cerrno>
#include <charconv>
#include <cstdio>
#include <cstdint>
#include <ctime>
#include "capture_wav_recovery.h"
#include "capture_clock.h"
#include "recording_parts.h"
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

using capture_clock::captureUtc;

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
struct FlatWavPath {
  std::string operator()(int, const std::string& base) const { return base + ".wav"; }
};

struct FlatMomentsPath {
  std::string operator()(int, const std::string& base) const { return base + ".moments"; }
};

template<class Allocate, class Reserve, class Prepare, class Finish,
         class WavPath = FlatWavPath, class MomentsPath = FlatMomentsPath>
bool adopt(const std::string& inbox, const std::string& notes, std::string name,
           int& id, Allocate allocate, Reserve reserve, Prepare prepare, Finish finish,
           WavPath wavPath = {}, MomentsPath momentsPath = {}) {
  std::string base;
  uint32_t epoch = 0;
  id = 0;
  if (!parse(name, id, base, epoch)) return false;
  std::string target, wav;
  auto resolve = [&] {
    char number[30]{};
    std::snprintf(number, sizeof(number), "note_%03d", id);
    target = notes + number;
    wav = wavPath(id, target);
    return present(wav);
  };
  if (!id) {
    // Find a free target before persisting the claim or moving sidecars.
    int occupied;
    do {
      const int previous = id;
      id = allocate(epoch);
      if (id <= previous) return false;
      occupied = resolve();
    } while (occupied > 0);
    if (occupied < 0) return false;
    const auto claimed = std::string("c") + std::to_string(id) + "-" + name;
    if (present(inbox+claimed) != 0 || std::rename((inbox+name).c_str(), (inbox+claimed).c_str()) != 0)
      return false;
    name = claimed;
  } else if (resolve() != 0) {
    return false;
  }
  if (!reserve(id, epoch)) return false;
  const auto marks = inbox + base.substr(0, base.size()-4) + ".mom";
  const int hasMarks = present(marks);
  if (hasMarks < 0) return false;
  if (hasMarks) {
    const auto destination = momentsPath(id, target);
    if (present(destination) != 0 || std::rename(marks.c_str(), destination.c_str()) != 0) return false;
  }
  return recording_parts::moveSidecars(inbox+base,wav) && prepare(id, epoch) && std::rename((inbox+name).c_str(), wav.c_str()) == 0 && finish(id);
}
}
