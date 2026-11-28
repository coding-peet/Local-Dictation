#pragma once

#include <cerrno>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <functional>
#include <optional>
#include <string>
#include <string_view>
#include <utility>

#include "monthly_recordings.h"

namespace archive_month {

struct Month {
  int key = -1;
  bool legacy = false;
  bool operator==(const Month& other) const { return key == other.key && legacy == other.legacy; }
};

inline bool validMonth(int key) {
  return key == 0 || (key / 100 >= 2020 && key / 100 <= 2100 && key % 100 >= 1 && key % 100 <= 12);
}

inline std::optional<Month> parseMonth(std::string_view name, bool legacy) {
  if (name.size() != 6) return std::nullopt;
  int key = 0;
  for (char glyph : name) {
    if (glyph < '0' || glyph > '9') return std::nullopt;
    key = key * 10 + glyph - '0';
  }
  if (!validMonth(key)) return std::nullopt;
  return Month{key, legacy};
}

inline bool validFilename(std::string_view name) {
  if (name.empty() || name.size() >= 64 || name == "." || name == "..") return false;
  for (char glyph : name) {
    const bool alphanumeric = (glyph >= '0' && glyph <= '9') ||
                             (glyph >= 'a' && glyph <= 'z') || (glyph >= 'A' && glyph <= 'Z');
    if (!alphanumeric && glyph != '.' && glyph != '_' && glyph != '-') return false;
  }
  return true;
}

enum class Pending { None, Audio, Delivery };
struct Member {
  std::string name;
  int note = 0;
  Pending pending = Pending::None;
  uint64_t size = 0;
  uint64_t modified = 0;
};

inline bool belongsToMonth(Month month, int note) {
  return monthly_recordings::sequence(note) && monthly_recordings::folderMonth(note) == month.key &&
         monthly_recordings::inRecordings(note) != month.legacy;
}

inline bool validMember(Month month, const Member& member) {
  return member.note ? member.name.empty() && belongsToMonth(month, member.note) : validFilename(member.name);
}

using Visitor = std::function<bool(const Member&)>;
using Scan = std::function<bool(Month, const Visitor&)>;
using Remove = std::function<bool(Month, const Member&)>;
using Finish = std::function<void(Month)>;
using Stop = std::function<bool()>;
using Flush = std::function<bool(std::FILE*)>;

constexpr uint32_t maxMembers = 32768;
constexpr uint64_t hashStart = 14695981039346656037ULL;
inline uint64_t hash(uint64_t seed, const void* bytes, size_t count) {
  const auto* first = static_cast<const uint8_t*>(bytes);
  for (size_t i = 0; i < count; ++i) seed = (seed ^ first[i]) * 1099511628211ULL;
  return seed;
}

struct Inventory {
  uint32_t files = 0, audio = 0, delivery = 0, members = 0;
  uint64_t fingerprint = 0;
  bool operator==(const Inventory& other) const {
    return files == other.files && audio == other.audio && delivery == other.delivery &&
           members == other.members && fingerprint == other.fingerprint;
  }
};

inline std::optional<Inventory> preview(Month month, const Scan& scan, const Visitor& emit = {}) {
  if (!validMonth(month.key)) return std::nullopt;
  Inventory result;
  const bool done = scan(month, [&](const Member& member) {
    if (!validMember(month, member) || ++result.members > maxMembers) return false;
    if (!member.note) ++result.files;
    if (member.pending == Pending::Audio) ++result.audio;
    if (member.pending == Pending::Delivery) ++result.delivery;
    const auto pending = static_cast<uint32_t>(member.pending);
    uint64_t fingerprint = hash(hashStart, member.name.data(), member.name.size());
    fingerprint = hash(fingerprint, &member.note, sizeof(member.note));
    fingerprint = hash(fingerprint, &pending, sizeof(pending));
    fingerprint = hash(fingerprint, &member.size, sizeof(member.size));
    // Order-independent because the FAT directory and pending queue need not
    // enumerate stable members in the same order between the two requests.
    result.fingerprint += hash(fingerprint, &member.modified, sizeof(member.modified));
    return !emit || emit(member);
  });
  return done ? std::optional<Inventory>(result) : std::nullopt;
}

enum class Stage { Accepted, Changed, Unavailable };
enum class Progress { Idle, Pending, Completed, Error };

class SpaceRefresh {
 public:
  void observe(Progress progress) {
    if (progress == Progress::Completed) pending_ = true;
  }
  bool pending() const { return pending_; }

  template<class Busy, class Refresh>
  void service(Busy busy, Refresh refresh) {
    if (!pending_ || busy()) return;
    pending_ = false;
    refresh();
  }

 private:
  bool pending_ = false;
};

class Job {
 public:
  explicit Job(std::string path) : path_(std::move(path)) {}
  ~Job() { if (reader_) std::fclose(reader_); }
  Job(const Job&) = delete;
  Job& operator=(const Job&) = delete;

  bool active() {
    if (!present_) {
      std::FILE* probe = std::fopen(path_.c_str(), "rb");
      present_ = probe || errno != ENOENT;
      if (probe) std::fclose(probe);
    }
    return *present_;
  }

  Stage stage(Month month, const Inventory& expected, const Scan& scan, const Flush& flush) {
    if (active() || !validMonth(month.key)) return Stage::Unavailable;
    const auto temporary = path_ + ".tmp";
    std::FILE* out = std::fopen(temporary.c_str(), "wb");
    if (!out) return Stage::Unavailable;
    Header header{magic, static_cast<uint32_t>(month.key), month.legacy ? 1U : 0U, 0, 0};
    uint64_t digest = scopeHash(header);
    const bool opened = std::fwrite(&header, sizeof(header), 1, out) == 1;
    const auto current = opened ? preview(month, scan, [&](const Member& member) {
      Record item{};
      item.note = member.note;
      item.size = member.size;
      item.modified = member.modified;
      std::memcpy(item.filename, member.name.data(), member.name.size());
      digest = hash(digest, &item, sizeof(item));
      return std::fwrite(&item, sizeof(item), 1, out) == 1;
    }) : std::nullopt;
    const bool matches = current && *current == expected;
    if (matches) {
      header.count = current->members;
      header.digest = digest;
    }
    const bool written = matches && std::fseek(out, 0, SEEK_SET) == 0 &&
        std::fwrite(&header, sizeof(header), 1, out) == 1 && std::fflush(out) == 0 && flush(out);
    const bool closed = std::fclose(out) == 0;
    if (written && closed && std::rename(temporary.c_str(), path_.c_str()) == 0) {
      present_ = true;
      return Stage::Accepted;
    }
    std::remove(temporary.c_str());
    return current && !matches ? Stage::Changed : Stage::Unavailable;
  }

  Progress advance(const Remove& remove, const Finish& finish, const Stop& stop, unsigned budget = 8) {
    if (!active()) return Progress::Idle;
    if (broken_) return Progress::Error;
    if (stop()) return Progress::Pending;
    if (!reader_ && !open()) return fail();
    const Month month{static_cast<int>(header_.month), header_.legacy != 0};
    while (budget-- && !stop()) {
      if (position_ == header_.count) {
        if (!validated_) {
          if (std::fgetc(reader_) != EOF || std::ferror(reader_) || digest_ != header_.digest ||
              std::fseek(reader_, sizeof(Header), SEEK_SET) != 0) return fail();
          position_ = 0;
          validated_ = true;
          continue;
        }
        if (std::fclose(reader_) != 0) { reader_ = nullptr; return fail(); }
        reader_ = nullptr;
        finish(month);
        if (std::remove(path_.c_str()) != 0) return fail();
        present_ = false;
        return Progress::Completed;
      }
      Record item;
      if (std::fread(&item, sizeof(item), 1, reader_) != 1) return fail();
      const auto* terminator = static_cast<const char*>(std::memchr(item.filename, 0, sizeof(item.filename)));
      if (!terminator) return fail();
      const Member member{std::string(item.filename, terminator - item.filename), item.note,
                          Pending::None, item.size, item.modified};
      if (!validMember(month, member)) return fail();
      if (validated_) {
        if (!remove(month, member)) {
          if (std::fseek(reader_, -static_cast<long>(sizeof(Record)), SEEK_CUR) != 0) return fail();
          return Progress::Pending;
        }
      } else {
        digest_ = hash(digest_, &item, sizeof(item));
      }
      ++position_;
    }
    return Progress::Pending;
  }

 private:
  static constexpr uint32_t magic = 0x31414c50U;
  struct Header { uint32_t magic, month, legacy, count; uint64_t digest; };
  struct Record { uint64_t size, modified; int32_t note; char filename[64]; uint32_t reserved; };

  static uint64_t scopeHash(const Header& header) {
    return hash(hash(hashStart, &header.month, sizeof(header.month)), &header.legacy, sizeof(header.legacy));
  }

  bool open() {
    reader_ = std::fopen(path_.c_str(), "rb");
    if (!reader_ || std::fread(&header_, sizeof(header_), 1, reader_) != 1 ||
        header_.magic != magic || header_.legacy > 1 || header_.count > maxMembers ||
        !validMonth(static_cast<int>(header_.month))) return false;
    digest_ = scopeHash(header_);
    position_ = 0;
    validated_ = false;
    return true;
  }

  Progress fail() {
    broken_ = true;
    if (reader_) { std::fclose(reader_); reader_ = nullptr; }
    return Progress::Error;
  }

  std::string path_;
  std::FILE* reader_ = nullptr;
  std::optional<bool> present_;
  Header header_{};
  unsigned position_ = 0;
  uint64_t digest_ = 0;
  bool validated_ = false, broken_ = false;
};

} // namespace archive_month
