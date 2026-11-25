#include "../pala_note/src/app/catalog_storage.h"
#include "../pala_note/src/app/storage_commit.h"
#include <cassert>
#include <chrono>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>

namespace fs = std::filesystem;
using note_catalog::Source;

namespace {
const char* failingPath = nullptr;

std::FILE* failOpenOnce(const char* path, const char* mode) {
  if (failingPath && std::strcmp(failingPath, path) == 0) {
    failingPath = nullptr;
    errno = EIO;
    return nullptr;
  }
  return std::fopen(path, mode);
}

struct Disk {
  bool exists(const char* path) const { return fs::exists(path); }
  bool remove(const char* path) const { return fs::remove(path); }
  bool rename(const char* from, const char* to) const { return std::rename(from, to) == 0; }
};

std::string readBytes(const char* path) {
  std::ifstream input(path, std::ios::binary);
  return {std::istreambuf_iterator<char>(input), std::istreambuf_iterator<char>()};
}

bool load(note_catalog::Snapshot& snapshot, std::vector<NoteEntry>& entries) {
  return snapshot.load(entries, "index.csv", "index.csv.bak", "index.bak", "catalog.fast.v1",
                       [](int) { return Source::Present; }, failOpenOnce);
}

bool persist(const std::vector<NoteEntry>& entries) {
  auto* file = std::fopen("index.tmp", "wb");
  assert(file);
  size_t bytes = 0;
  bool ok = note_catalog::writeRows(entries, [&](const char* row, size_t n) {
    return std::fwrite(row, 1, n, file);
  }, bytes);
  ok = std::fclose(file) == 0 && ok && fs::file_size("index.tmp") == bytes;
  Disk disk;
  return ok && commitStoredFile(disk, std::string("index.csv"), std::string("index.tmp"));
}

std::vector<int> ids(const std::vector<NoteEntry>& entries) {
  std::vector<int> result;
  for (const auto& entry : entries) result.push_back(entry.num);
  return result;
}

void testCatalogAndIntent() {
  const std::string original = "2,Ideas,1,4\r\n9,Inbox,0\n";
  const std::vector<int> originalIds{2, 9};
  std::ofstream("index.csv", std::ios::binary) << original;
  std::ofstream("index.bak") << "89,Old,0\n";
  std::ofstream("index.tmp") << "90,Partial,0\n";
  std::ofstream("catalog.fast.v1").put(1);

  note_catalog::Snapshot snapshot;
  std::vector<NoteEntry> notes{{77, "Existing", false, 0}};
  failingPath = "index.csv";
  assert(!load(snapshot, notes));
  assert(!snapshot.loaded && notes.front().num == 77);
  assert(readBytes("index.csv") == original && fs::exists("catalog.fast.v1"));

  // Inject an error while resolving the second row. A successfully read prefix
  // must not become the current catalog.
  assert(!snapshot.load(notes, "index.csv", "index.csv.bak", "index.bak", "catalog.fast.v1",
                        [](int n) { return n == 9 ? Source::Error : Source::Present; }));
  assert(!snapshot.loaded && notes.size() == 1 && notes.front().num == 77);
  assert(readBytes("index.csv") == original);

  fs::rename("index.csv", "index.csv.bak");
  failingPath = "index.csv.bak";
  assert(!load(snapshot, notes));
  assert(readBytes("index.csv.bak") == original);
  assert(load(snapshot, notes));
  assert(snapshot.needsDirectory && ids(notes) == originalIds);
  assert(!fs::exists("catalog.fast.v1") && readBytes("index.csv") == original);
  assert(notes.front().hasText && notes.front().momentCount == 4);
  assert(!std::strcmp(notes.front().tag, "Ideas"));

  const int32_t pending = 12;
  std::ofstream("capture.pending", std::ios::binary).write(reinterpret_cast<const char*>(&pending), sizeof(pending));
  const std::string pendingBytes = readBytes("capture.pending");
  const std::string wavBytes(2048, 'W');
  std::ofstream("note_012.wav", std::ios::binary) << wavBytes;
  auto discover = [&](int id, const char* path) {
    return note_catalog::recoverRecording(notes, id, path, [&](auto at) {
      notes.insert(at, {id, "Inbox", false, 0});
      return true;
    }, failOpenOnce);
  };
  auto recoverRecording = [&](int note, const char* utc) {
    assert(!*utc); // legacy reservation carries no timestamp
    assert(note == pending);
    return discover(note, "note_012.wav") && persist(notes);
  };
  for (const char* path : {"capture.pending", "note_012.wav"}) {
    failingPath = path;
    assert(!note_catalog::recoverReservation("capture.pending", recoverRecording, failOpenOnce));
    assert(readBytes("capture.pending") == pendingBytes);
    assert(readBytes("index.csv") == original && ids(notes) == originalIds);
  }
  assert(note_catalog::recoverReservation("capture.pending", recoverRecording, failOpenOnce));
  assert(!fs::exists("capture.pending") && readBytes("note_012.wav") == wavBytes);

  // A migration step has found another WAV. The catalog on disk still lacks
  // this entry when Companion requests its page and a new capture arrives.
  std::ofstream("note_021.wav", std::ios::binary) << wavBytes;
  assert(discover(21, "note_021.wav"));
  note_catalog::PortalPage page(notes, 0, 25);
  assert(page.at(0).num == 21 && page.end == 4);
  assert(load(snapshot, notes));
  assert(notes.back().num == 21);
  std::ofstream("note_022.wav", std::ios::binary) << wavBytes;
  notes.push_back({22, "Inbox", false, 0});
  assert(persist(notes));

  note_catalog::Snapshot reboot;
  std::vector<NoteEntry> after;
  assert(load(reboot, after));
  assert(ids(after) == std::vector<int>({2, 9, 12, 21, 22}));
  assert(after[0].hasText && after[0].momentCount == 4 && !std::strcmp(after[0].tag, "Ideas"));
}
}  // namespace

int main() {
  const auto previous = fs::current_path();
  const auto unique = std::chrono::steady_clock::now().time_since_epoch().count();
  const auto fixture = fs::temp_directory_path() / ("pala-catalog-test-" + std::to_string(unique));
  assert(fs::create_directory(fixture));
  fs::current_path(fixture);
  testCatalogAndIntent();
  fs::current_path(previous);
  fs::remove_all(fixture);
  std::cout << "Catalog retry, backup, capture intent, Companion page during migration, append and reboot passed.\n";
}
