#pragma once
#include <array>
#include <cerrno>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <sys/stat.h>
#include "storage_audit_policy.h"
#include "transcript_storage.h"

// Only call on a recorder-owned WAV identified by capture.pending. The original
// file remains byte-for-byte available after a repair, including a torn sample.
namespace capture_wav {
using Open = std::FILE* (*)(const char*, const char*);
using Rename = int (*)(const char*, const char*);
using Write = size_t (*)(const void*, size_t, size_t, std::FILE*);
enum class State { Missing, Empty, Ready, Stale, Unrecognized, Error };

struct Info {
  std::array<uint8_t, 44> header{};
  uint32_t pcm = 0;
};

inline State inspect(const std::string& path, Info& info, Open open) {
  auto* f = open(path.c_str(), "rb");
  if (!f) return errno == ENOENT ? State::Missing : State::Error;
  struct stat st{};
  State result = State::Error;
  if (stat(path.c_str(), &st) == 0 && st.st_size >= 0 && uint64_t(st.st_size) <= UINT32_MAX) {
    const auto size = static_cast<uint32_t>(st.st_size);
    auto* h = info.header.data();
    if (size < 46) result = State::Empty; // no complete 16-bit mono frame
    else if (std::fread(h, 1, info.header.size(), f) == info.header.size()) {
      using note_maintenance::readLe16;
      using note_maintenance::readLe32;
      // The recorder flushes this format before writing PCM. Treat foreign
      // formats as evidence for inspection, never as headerless raw audio.
      const bool ownedFormat = !std::memcmp(h, "RIFF", 4) && !std::memcmp(h+8, "WAVEfmt ", 8) &&
          readLe32(h+16) == 16 && readLe16(h+20) == 1 && readLe16(h+22) == 1 &&
          readLe32(h+24) == 16000 && readLe32(h+28) == 32000 &&
          readLe16(h+32) == 2 && readLe16(h+34) == 16 && !std::memcmp(h+36, "data", 4);
      info.pcm = (size - 44U) & ~uint32_t(1);
      if (!ownedFormat) result = State::Unrecognized;
      else if (readLe32(h+4) == size-8 && readLe32(h+40) == size-44 && !(size & 1U)) result = State::Ready;
      else if (readLe32(h+4) == size-8 && note_maintenance::checkWav(size, [&](uint32_t offset, uint8_t* dst, size_t n) {
        return std::fseek(f, static_cast<long>(offset), SEEK_SET) == 0 && std::fread(dst, 1, n, f) == n;
      })) result = State::Ready; // a valid completed container may have ancillary chunks
      else result = std::ferror(f) ? State::Error : State::Stale;
    }
  }
  return std::fclose(f) == 0 ? result : State::Error;
}

inline void putSize(uint8_t* dst, uint32_t value) {
  for (unsigned i=0; i<4; ++i) dst[i] = static_cast<uint8_t>(value >> (8*i));
}

inline bool copySamples(const std::string& source, const std::string& temp, Info info, Open open, Write write) {
  auto* in = open(source.c_str(), "rb");
  if (!in) return false;
  auto* out = open(temp.c_str(), "wb");
  if (!out) { std::fclose(in); return false; }
  putSize(info.header.data()+4, info.pcm+36);
  putSize(info.header.data()+40, info.pcm);
  bool ok = std::fseek(in, 44, SEEK_SET) == 0 &&
      write(info.header.data(), 1, info.header.size(), out) == info.header.size();
  std::array<uint8_t, 2048> buffer;
  for (auto remaining = info.pcm; ok && remaining;) {
    const size_t count = remaining < buffer.size() ? remaining : buffer.size();
    ok = std::fread(buffer.data(), 1, count, in) == count && write(buffer.data(), 1, count, out) == count;
    remaining -= static_cast<uint32_t>(count);
  }
  ok = std::fclose(in) == 0 && ok;
  ok = std::fflush(out) == 0 && ok;
  return std::fclose(out) == 0 && ok;
}

inline State recover(const std::string& wav, Open open = std::fopen, Rename move = std::rename,
                     Write write = std::fwrite) {
  Info info;
  auto state = inspect(wav, info, open);
  if (state != State::Stale && state != State::Missing) return state;
  const auto original = wav + ".recovery-original";
  Info saved;
  const auto prior = inspect(original, saved, open);
  const bool parked = state == State::Missing;
  if (parked) {
    if (prior == State::Missing || prior == State::Error) return prior;
    if (prior != State::Stale) return State::Unrecognized;
    info = saved;
  } else if (prior != State::Missing) {
    return State::Error; // never overwrite existing evidence
  }
  // A short write or a reset while copying leaves the source in place.
  // After parking, a reboot addresses the same source via the journal's ID.
  const auto temp = wav + ".recovery-tmp";
  if (!copySamples(parked ? original : wav, temp, info, open, write)) {
    // Keeping a short scratch copy on a nearly full card could starve REC.
    // The complete captured file is still at wav or original on every path.
    std::remove(temp.c_str());
    return State::Error;
  }
  Info candidate;
  if (inspect(temp, candidate, open) != State::Ready || candidate.pcm != info.pcm) return State::Error;
  if (!parked && move(wav.c_str(), original.c_str()) != 0) return State::Error;
  return move(temp.c_str(), wav.c_str()) == 0 ? State::Ready : State::Error;
}

// Preserve any existing metadata generation. If the RTC was unavailable, the
// journal legitimately carries no UTC, just like the original ID-only format.
template<class WriteMissing>
bool restoreTimestamp(const std::string& meta, const char* utc, WriteMissing writeMissing) {
  if (!*utc) return true;
  const auto source = note_transcript::inspect(meta.c_str());
  if (source == note_transcript::Stored::Error) return false;
  return source != note_transcript::Stored::Missing || writeMissing(utc);
}

}
