#pragma once
#include "capture_wav_recovery.h"
#include <algorithm>
#include <array>
#include <cerrno>
#include <cstdio>
#include <string>
#include <string_view>
#include <vector>
#include <sys/stat.h>
#ifdef _WIN32
#include <io.h>
#else
#include <unistd.h>
#endif

namespace recording_parts {
// A canonical WAV is also the first part; only long takes have a journal.
// Additional filenames cannot be mistaken for catalog WAVs or inbox claims.
constexpr uint32_t partBytes = 300U * 16000 * 2;
constexpr unsigned maxParts = 448;  // beyond the writer's 32-bit SD budget
struct Entry {
  uint32_t bytes, crc;
};
enum class State { Single, Open, Complete, Interrupted, Error };
inline std::string path(const std::string& wav, unsigned index) {
  return index ? wav + ".p" + std::to_string(index) : wav;
}
inline size_t remaining(uint32_t used, size_t requested) {
  return std::min<size_t>(partBytes - used, requested);
}
inline bool sync(FILE* file) {
  if (std::fflush(file)) return false;
#ifdef _WIN32
  return _commit(_fileno(file)) == 0;
#else
  return ::fsync(fileno(file)) == 0;
#endif
}
inline uint32_t guard(unsigned index, Entry row) { return 0x50415254u ^ index ^ row.bytes ^ row.crc; }
inline bool put(const std::string& wav, unsigned index, Entry row) {
  const auto name = wav + ".parts";
  auto* f = std::fopen(name.c_str(), "r+b");
  if (!f && errno == ENOENT && index == 0) f = std::fopen(name.c_str(), "wb");
  if (!f) return false;
  const std::array<uint32_t, 4> record{index, row.bytes, row.crc, guard(index, row)};
  const bool ok = std::fseek(f, long(index * 16), SEEK_SET) == 0 &&
                  std::fwrite(record.data(), sizeof(record), 1, f) == 1 && sync(f);
  return std::fclose(f) == 0 && ok;
}
inline bool append(const std::string& wav, unsigned index, Entry row) {
  return index < maxParts && row.bytes && row.bytes <= partBytes && !(row.bytes & 1) && put(wav, index, row);
}
inline bool seal(const std::string& wav, unsigned count, bool interrupted) {
  return count && count <= maxParts && put(wav, count, {0, interrupted ? 1u : 0u});
}
inline State read(const std::string& wav, std::vector<Entry>& rows, capture_wav::Open open = std::fopen) {
  rows.clear();
  FILE* file = open((wav + ".parts").c_str(), "rb");
  if (!file) return errno == ENOENT ? State::Single : State::Error;
  State state = State::Error;
  while (rows.size() <= maxParts) {
    std::array<uint32_t, 4> row{};
    if (std::fread(row.data(), 1, sizeof(row), file) != sizeof(row)) {
      if (std::feof(file) && !std::ferror(file)) state = State::Open;
      break;
    }
    const Entry entry{row[1], row[2]};
    if (row[0] != rows.size() || row[3] != guard(row[0], entry)) break;
    if (!entry.bytes) {
      if (!rows.empty() && entry.crc <= 1 && std::fgetc(file) == EOF && !std::ferror(file))
        state = entry.crc ? State::Interrupted : State::Complete;
      break;
    }
    if (rows.size() == maxParts || entry.bytes > partBytes || (entry.bytes & 1) ||
        (!rows.empty() && rows.back().bytes != partBytes))
      break;
    rows.push_back(entry);
  }
  return std::fclose(file) == 0 ? state : State::Error;
}

// Called only by the SD writer. File rotation never changes ownership of I2S.
template <class OpenPart, class Checkpoint, class Checksum>
class Writer {
 public:
  Writer(FILE*& stream, const std::string& base, bool segmented, OpenPart open, Checkpoint checkpoint,
         Checksum checksum)
      : file_(stream),
        base_(base),
        segmented_(segmented),
        open_(open),
        checkpoint_(checkpoint),
        checksum_(checksum) {}

  bool write(const void* samples, size_t bytes) {
    auto* pcm = static_cast<const uint8_t*>(samples);
    while (bytes) {
      if (segmented_ && partSize == partBytes && !rotate()) return false;
      const auto wanted = segmented_ ? remaining(partSize, bytes) : bytes;
      const auto written = std::fwrite(pcm, 1, wanted, file_);
      const auto whole = written & ~size_t{1};
      if (segmented_) crc_ = checksum_(crc_, pcm, whole);
      partSize += whole;
      total += whole;
      if (written != wanted) return false;
      bytes -= wanted;
      pcm += wanted;
    }
    return true;
  }

  bool publish(bool interrupted) {
    return index_ == 0 || (append(base_, index_, {partSize, crc_}) && seal(base_, index_ + 1, interrupted));
  }

  uint32_t total = 0, partSize = 0;

 private:
  bool rotate() {
    if (!checkpoint_(file_, partSize)) return false;
    const bool closed = std::fclose(file_) == 0;
    file_ = nullptr;
    if (!closed || !append(base_, index_, {partSize, crc_}) || ++index_ >= maxParts) return false;
    partSize = crc_ = 0;
    file_ = open_(index_);
    return file_ != nullptr;
  }
  FILE*& file_;
  const std::string& base_;
  bool segmented_;
  OpenPart open_;
  Checkpoint checkpoint_;
  Checksum checksum_;
  unsigned index_ = 0;
  uint32_t crc_ = 0;
};

// The source WAV and its persistent claim are moved last. On replay each
// companion must exist in exactly one of the two locations.
inline int exists(const std::string& name) {
  struct stat s {};
  if (::stat(name.c_str(), &s) == 0) return 1;
  return errno == ENOENT ? 0 : -1;
}
inline bool moveOne(const std::string& from, const std::string& to) {
  int a = exists(from), b = exists(to);
  return (a == 0 && b == 1) || (a == 1 && b == 0 && std::rename(from.c_str(), to.c_str()) == 0);
}
inline bool moveSidecars(const std::string& from, const std::string& to) {
  std::vector<Entry> rows;
  auto state = read(from, rows);
  if (state == State::Single) {
    state = read(to, rows);
    return state == State::Single || state == State::Complete || state == State::Interrupted;
  }
  if (state != State::Complete && state != State::Interrupted) return false;
  for (unsigned i = 1; i < rows.size(); ++i)
    if (!moveOne(path(from, i), path(to, i))) return false;
  return moveOne(from + ".parts", to + ".parts");
}

template <class Crc>
bool recover(const std::string& wav, Crc crc) {
  std::vector<Entry> rows;
  auto state = read(wav, rows);
  if (state != State::Open) return state != State::Error;
  // Only the last data file can be unjournalled. Earlier committed rows
  // remain immutable. An empty final file is possible after a power cut.
  const unsigned index = static_cast<unsigned>(rows.size());
  const auto tail = path(wav, index);
  const auto result = capture_wav::recover(tail);
  if (result == capture_wav::State::Missing || result == capture_wav::State::Empty)
    return seal(wav, index, true);
  if (result != capture_wav::State::Ready) return false;
  capture_wav::Info info;
  if (capture_wav::inspect(tail, info, std::fopen) != capture_wav::State::Ready || !info.pcm ||
      info.pcm > partBytes || (index && rows.back().bytes != partBytes))
    return false;
  FILE* f = std::fopen(tail.c_str(), "rb");
  if (!f) return false;
  bool ok = std::fseek(f, 44, SEEK_SET) == 0;
  uint32_t hash = 0;
  std::array<uint8_t, 1024> buffer{};
  for (uint32_t left = info.pcm; ok && left;) {
    auto n = std::min<size_t>(left, buffer.size());
    ok = std::fread(buffer.data(), 1, n, f) == n;
    if (ok) hash = crc(hash, buffer.data(), n);
    left -= n;
  }
  return std::fclose(f) == 0 && ok && append(wav, index, {info.pcm, hash}) && seal(wav, index + 1, true);
}
inline bool recoverTranscript(const std::string& wav, const std::string& txt) {
  const auto pending = wav + ".transcript.pending";
  const int have = exists(pending);
  if (have != 1) return have == 0;
  const int full = exists(wav + ".transcript");
  if (full != 1) return full == 0;
  const auto state = note_transcript::inspect(txt.c_str());
  if (note_transcript::committed(state)) return std::remove(pending.c_str()) == 0;
  return state == note_transcript::Stored::Missing && std::rename(pending.c_str(), txt.c_str()) == 0;
}

// Rebuild the full text in order from authenticated part receipts. A partial
// transfer has a private scratch path; only a completed sequence is published.
class TranscriptArchive {
 public:
  void reset() {
    base_.clear();
    scratch_.clear();
    next_ = 0;
    wrote_ = false;
  }
  void start(const std::string& wav, const char* link) {
    reset();
    base_ = wav + ".transcript";
    scratch_ = base_ + "." + link + ".tmp";
  }
  bool piece(unsigned index, const char* data, size_t bytes) {
    if (scratch_.empty() || next_ != index) return false;
    FILE* f = std::fopen(scratch_.c_str(), index ? "ab" : "wb");
    if (!f) return false;
    const bool ok = (!bytes || !wrote_ || std::fputc(' ', f) != EOF) &&
                    std::fwrite(data, 1, bytes, f) == bytes && sync(f);
    if (std::fclose(f) != 0 || !ok) return false;
    ++next_;
    wrote_ |= bytes != 0;
    return true;
  }
  bool finish(unsigned count, std::string_view preview = {}) {
    if (!count || next_ != count || !wrote_) return false;
    if (exists(base_) == 0) {
      // Publish the compact receipt before the full text. A reboot between
      // the next rename and the queue commit can then finish without ASR.
      if (!preview.empty()) {
        FILE* receipt = std::fopen((base_ + ".pending").c_str(), "wb");
        if (!receipt) return false;
        const bool written =
            std::fwrite(preview.data(), 1, preview.size(), receipt) == preview.size() && sync(receipt);
        if (std::fclose(receipt) != 0 || !written) return false;
      }
      return std::rename(scratch_.c_str(), base_.c_str()) == 0;
    }
    // If publication succeeded before a lost receipt, verify the durable
    // text before acknowledging or using a newly generated delivery preview.
    FILE* old = std::fopen(base_.c_str(), "rb");
    FILE* fresh = std::fopen(scratch_.c_str(), "rb");
    bool same = old && fresh;
    std::array<char, 512> a{}, b{};
    while (same) {
      const size_t left = std::fread(a.data(), 1, a.size(), old),
                   right = std::fread(b.data(), 1, b.size(), fresh);
      same = left == right && std::memcmp(a.data(), b.data(), left) == 0 && !std::ferror(old) &&
             !std::ferror(fresh);
      if (left < a.size()) break;
    }
    if (old) std::fclose(old);
    if (fresh) std::fclose(fresh);
    return same && std::remove(scratch_.c_str()) == 0;
  }

 private:
  std::string base_, scratch_;
  unsigned next_ = 0;
  bool wrote_ = false;
};

}  // namespace recording_parts
