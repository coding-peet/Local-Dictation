#pragma once

#include <algorithm>
#include <cerrno>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <map>
#include <optional>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

#include "monthly_recordings.h"
#include "recording_parts.h"
#include "note_trash_policy.h"

class String : public std::string {
 public:
  using std::string::string;
  String(std::string value) : std::string(std::move(value)) {}
  explicit String(int value) : std::string(std::to_string(value)) {}
  bool isEmpty() const { return empty(); }
  String substring(size_t first) const { return substr(first); }
  String substring(size_t first, size_t end) const { return substr(first, end - first); }
  long toInt() const { return std::strtol(c_str(), nullptr, 10); }
  friend String operator+(const String& a, const String& b) {
    return static_cast<const std::string&>(a) + static_cast<const std::string&>(b);
  }
  friend String operator+(const String& a, const char* b) { return a + String(b); }
  friend String operator+(const char* a, const String& b) { return String(a) + b; }
};

template <typename T> T constrain(T value, T low, T high) { return std::clamp(value, low, high); }
struct RecordingWindow {};
constexpr const char* NOTES_DIR = "/notes";
constexpr size_t CONTENT_LENGTH_UNKNOWN = static_cast<size_t>(-1);

namespace fixture {
inline bool rec = false;
inline size_t maxRead = 0;
inline unsigned closedFiles = 0;
inline unsigned closedDirectories = 0;
inline std::map<std::string, std::vector<std::string>> directories;
inline std::map<std::string, std::string> files;
inline std::vector<std::string> openedDirectories, openedFiles;
}

struct DirEntry { char d_name[64]; };
struct DIR {
  const std::vector<std::string>* names;
  size_t next = 0;
  DirEntry entry{};
};

inline DIR* opendir(const char* path) {
  fixture::openedDirectories.emplace_back(path);
  const auto found = fixture::directories.find(path);
  return found == fixture::directories.end() ? nullptr : new DIR{&found->second};
}
inline DirEntry* readdir(DIR* directory) {
  if (directory->next == directory->names->size()) return nullptr;
  const auto& name = directory->names->at(directory->next++);
  std::snprintf(directory->entry.d_name, sizeof(directory->entry.d_name), "%s", name.c_str());
  return &directory->entry;
}
inline int closedir(DIR* directory) { delete directory; ++fixture::closedDirectories; return 0; }

struct File {
  const std::string* content = nullptr;
  size_t offset = 0;
  explicit operator bool() const { return content != nullptr; }
  bool isDirectory() const { return false; }
  size_t size() const { return content->size(); }
  bool seek(size_t at) { if (at>size()) return false; offset=at; return true; }
  bool available() const { return offset < size(); }
  size_t read(uint8_t* target, size_t capacity) {
    fixture::maxRead = std::max(fixture::maxRead, capacity);
    const size_t count = std::min(size() - offset, capacity);
    std::memcpy(target, content->data() + offset, count);
    offset += count;
    return count;
  }
  void close() { ++fixture::closedFiles; }
};
constexpr int FILE_READ = 0;
struct Card {
  bool exists(const String& path) const { return fixture::files.count(path)!=0; }
  File open(const char* path,int = FILE_READ) {
    fixture::openedFiles.emplace_back(path);
    const auto file = fixture::files.find(path);
    return {file == fixture::files.end() ? nullptr : &file->second};
  }
  File open(const String& path) { return open(path.c_str()); }
};
inline Card SD_MMC;

inline recording_parts::State readArchiveParts(const char* path,std::vector<recording_parts::Entry>& rows) {
  return recording_parts::read(path,rows,[](const char* name,const char*) -> FILE* {
    const auto it=fixture::files.find(name);
    if (it==fixture::files.end()) { errno=ENOENT; return nullptr; }
    FILE* file=std::tmpfile();
    if (!file) return nullptr;
    if (std::fwrite(it->second.data(),1,it->second.size(),file)!=it->second.size() || std::fseek(file,0,SEEK_SET)) {
      std::fclose(file); return nullptr;
    }
    return file;
  });
}

struct Http {
  struct Socket {
    bool open = true;
    bool recAfterWrite = false;
    std::string bytes;
    bool connected() const { return open; }
    void stop() { open = false; }
    size_t write(const uint8_t* data, size_t count) {
      bytes.append(reinterpret_cast<const char*>(data), count);
      if (recAfterWrite) fixture::rec = true;
      return count;
    }
  };

  std::map<std::string, String> args, headers;
  Socket socket;
  String mime, body;
  size_t length = 0;
  int status = 0;
  bool complete = false;
  bool recAfterChunk = false;

  bool hasArg(const char* name) const { return args.find(name) != args.end(); }
  String arg(const char* name) const { return hasArg(name) ? args.at(name) : String{}; }
  void send(int code, const char* type, const String& data) {
    status = code;
    mime = type;
    body = data;
  }
  void sendHeader(const char* name, const String& value) { headers.insert_or_assign(name, value); }
  void setContentLength(size_t count) { length = count; }
  void sendContent(const String& text) {
    if (text.empty()) complete = true;
    else body += text;
  }
  void sendContent(const char* data, size_t count) {
    body.append(data, count);
    if (recAfterChunk) fixture::rec = true;
  }
  Socket& client() { return socket; }
};
inline Http transferServer;

inline bool pendingPriorityRecording() { return fixture::rec; }
inline bool noteIsTrashed(int) { return false; }
inline bool noteHasCommittedTranscript(int id) { return monthly_recordings::sequence(id) == 1; }
inline String noteCreatedUtc(int) { return "2026-12-04T08:00:00Z"; }
inline String portalPageStart(const char*) { return "<html><body><div>"; }
inline String htmlEscape(const String& text) { return text; }
