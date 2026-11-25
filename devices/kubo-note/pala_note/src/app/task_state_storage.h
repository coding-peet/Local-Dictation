#pragma once

#include <cerrno>
#include <cstdio>
#include <string>
#include <string_view>
#include <sys/stat.h>

namespace task_storage {
using Open = std::FILE* (*)(const char*, const char*);

class Source {
 public:
  Source() = default;
  Source(const Source&) = delete;
  Source& operator=(const Source&) = delete;
  ~Source() { if (file) std::fclose(file); }

  bool open(const char* path, Open opener = std::fopen) {
    file = opener(path, "rb");
    if (!file && errno == ENOENT) {
      file = opener((std::string(path) + ".bak").c_str(), "rb");
      backup = file != nullptr;
    }
    if (!file) return errno == ENOENT;
    struct stat status;
#ifdef _WIN32
    const int descriptor = ::_fileno(file);
#else
    const int descriptor = ::fileno(file);
#endif
    if (::fstat(descriptor, &status) != 0 || (status.st_mode & S_IFMT) != S_IFREG ||
        std::fseek(file, 0, SEEK_END) != 0) return false;
    size = std::ftell(file);
    return size >= 0 && std::fseek(file, 0, SEEK_SET) == 0;
  }

  bool seek(long offset) { return offset == 0 || (file && offset >= 0 && offset <= size && std::fseek(file, offset, SEEK_SET) == 0); }
  long position() const { return file ? std::ftell(file) : 0; }
  int read() { return file ? std::fgetc(file) : EOF; }
  size_t readBytes(char* out, size_t bytes) { return file ? std::fread(out, 1, bytes, file) : 0; }
  bool complete() const { return !file || (!std::ferror(file) && position() == size); }
  bool close() {
    const bool ok = !file || std::fclose(file) == 0;
    file = nullptr;
    return ok;
  }

  bool line(std::string& result, int (*get)(std::FILE*) = std::fgetc) {
    result.clear();
    if (!file) return true;
    for (int c = get(file); c != EOF; c = get(file)) {
      if (result.size() >= 65536 || c == 0) return false;
      result += static_cast<char>(c);
      if (c == '\n') return true;
    }
    return complete();
  }

  std::FILE* file = nullptr;
  long size = 0;
  bool backup = false;
};

template<class Dirty>
bool edit(const char* primary, std::string_view id, std::string_view replacement, Dirty dirty,
          Open open = std::fopen, int (*get)(std::FILE*) = std::fgetc) {
  Source source;
  if (!source.open(primary, open)) return false;
  const bool hadPrimary = source.file && !source.backup;
  const bool hadBackup = source.backup;
  const std::string temp = std::string(primary) + ".tmp";
  const std::string backup = std::string(primary) + ".bak";
  auto* output = std::fopen(temp.c_str(), "wb");
  if (!output) return false;
  const auto write = [&](std::string_view bytes) {
    return std::fwrite(bytes.data(), 1, bytes.size(), output) == bytes.size();
  };
  bool replaced = false, atLineStart = true, ok = true;
  std::string row;
  while ((ok = source.line(row, get)) && !row.empty()) {
    const auto field = row.find('|');
    if (field != row.npos && std::string_view(row).substr(0, field) == id) {
      if (replaced) continue;
      replaced = true;
      ok = write(replacement) && write("\n");
      atLineStart = true;
    } else {
      ok = write(row);
      atLineStart = row.back() == '\n';
    }
    if (!ok) break;
  }
  ok = source.complete() && ok && (replaced || ((atLineStart || write("\n")) && write(replacement) && write("\n")));
  ok = source.close() && ok;
  ok = std::fflush(output) == 0 && ok;
  if (std::fclose(output) != 0 || !ok || !dirty()) return false;
  if (hadPrimary) {
    if (std::remove(backup.c_str()) != 0 && errno != ENOENT) return false;
    if (std::rename(primary, backup.c_str()) != 0) return false;
  }
  if (std::rename(temp.c_str(), primary) != 0) {
    if (hadPrimary) std::rename(backup.c_str(), primary);
    return false;
  }
  if (hadBackup || hadPrimary) std::remove(backup.c_str());
  return true;
}
}  // namespace task_storage
