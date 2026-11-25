#include "../pala_note/src/app/audit_projection.h"
#include "../pala_note/src/app/task_state_storage.h"
#include <cassert>
#include <cstring>
#include <chrono>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <iterator>
#include <vector>

namespace fs = std::filesystem;
namespace {
const char* failPath = nullptr;
int bytesUntilFault = 0;
std::FILE* faultOpen(const char* path, const char* mode) {
  if (failPath && std::strcmp(path, failPath) == 0) { errno = EIO; return nullptr; }
  return std::fopen(path, mode);
}
int earlyEnd(std::FILE* file) { return bytesUntilFault-- == 0 ? EOF : std::fgetc(file); }
std::string bytes(const char* p) {
  std::ifstream f(p, std::ios::binary);
  return {std::istreambuf_iterator<char>(f), std::istreambuf_iterator<char>()};
}

void taskEdits() {
  const char* path = "tasks.state";
  const std::string original = "13-z|1|2026-01-01T12:00:00Z|earlier\r\n12-x|0||edit\n11-y|1||last";
  const std::string prior = "13-z|0||older\n";
  std::ofstream(path, std::ios::binary) << original;
  std::ofstream("tasks.state.bak", std::ios::binary) << prior;
  int changes = 0;
  const auto dirty = [&] { ++changes; return true; };
  failPath = path;
  assert(!task_storage::edit(path, "12-x", "12-x|1||now", dirty, faultOpen));
  assert(bytes(path) == original && bytes("tasks.state.bak") == prior && changes == 0);
  failPath = nullptr;
  for (int boundary : {1, 37, int(original.size()) - 1}) {
    bytesUntilFault = boundary;
    assert(!task_storage::edit(path, "12-x", "12-x|1||now", dirty, faultOpen, earlyEnd));
    assert(bytes(path) == original && bytes("tasks.state.bak") == prior && changes == 0);
  }
  assert(!task_storage::edit(path, "12-x", "12-x|1||now", [] { return false; }));
  assert(bytes(path) == original && bytes("tasks.state.bak") == prior);
  fs::remove("tasks.state.bak");
  fs::rename(path, "tasks.state.bak");
  failPath = "tasks.state.bak";
  assert(!task_storage::edit(path, "12-x", "12-x|1||now", dirty, faultOpen));
  assert(!fs::exists(path) && bytes("tasks.state.bak") == original && changes == 0);
  failPath = nullptr;
  assert(task_storage::edit(path, "12-x", "12-x|1||now", dirty, faultOpen));
  assert(bytes(path) == "13-z|1|2026-01-01T12:00:00Z|earlier\r\n12-x|1||now\n11-y|1||last");
}

struct Note { int num; };
struct Task { int note; bool done; };

void compareProjections(int noteCount, int pendingCount) {
  std::vector<Note> notes;
  std::vector<SyncQueueEntry> queue;
  std::vector<Task> projected;
  for (int id = 1; id <= noteCount; ++id) {
    notes.push_back({id});
    if (id <= pendingCount) queue.push_back({id, SyncQueueKind::Audio});
    if (!(id % 11)) projected.push_back({id, id % 22 == 0});
  }
  const auto queueFromSources = [&](int unreadable = -1) {
    return queueMatchesSource(notes, [&](Note note, SyncQueueKind& kind) {
      kind = note.num <= pendingCount ? SyncQueueKind::Audio : SyncQueueKind::None;
      return note.num != unreadable;
    }, [&](auto visit) {
      for (auto row : queue) if (!visit(row)) break;
      return true;
    });
  };
  assert(queueFromSources());
  assert(!queueFromSources(noteCount)); // A failed source after the final queue row.
  queue.pop_back();
  assert(!queueFromSources()); // Missing final eligible row.
  queue.push_back({pendingCount, SyncQueueKind::Audio});
  queue[2].kind = SyncQueueKind::Organization;
  assert(!queueFromSources());
  queue[2].kind = SyncQueueKind::Audio;
  queue.push_back({noteCount + 1, SyncQueueKind::Audio});
  assert(!queueFromSources());
  queue.pop_back();
  assert(queueFromSources());

  const auto verify = [&](int unreadable = -1) {
    std::vector<Task> oneNote;
    return agendaMatchesSource(notes, static_cast<uint32_t>(projected.size()), [&](int id, auto& tasks) {
      if (id == unreadable) return false;
      if (!(id % 11)) tasks.push_back({id, id % 22 == 0});
      return true;
    }, [&](uint32_t pos, size_t ordinal, Task source) {
      return ordinal == 0 && source.note == projected[pos].note && source.done == projected[pos].done;
    }, oneNote);
  };
  assert(verify());
  assert(!verify(noteCount)); // A failed source does not verify as an empty note.
  const auto omitted = projected[3];
  projected.erase(projected.begin() + 3);
  assert(!verify()); // Structurally sorted and internally consistent, but a task was skipped.
  projected.insert(projected.begin() + 3, omitted);
  projected.back().done = !projected.back().done;
  assert(!verify()); // Wrong completion even with the right ID and count.
  projected.back().done = !projected.back().done;
  assert(verify());
  projected.push_back({noteCount + 1, false});
  assert(!verify());
}
}  // namespace

int main() {
  auto previous = fs::current_path();
  auto nonce = std::chrono::steady_clock::now().time_since_epoch().count();
  auto fixture = fs::temp_directory_path() / ("pala-state-test-" + std::to_string(nonce));
  assert(fs::create_directory(fixture));
  fs::current_path(fixture);
  taskEdits();
  fs::current_path(previous);
  fs::remove_all(fixture);
  compareProjections(10000, 1000);
  compareProjections(50000, 500);
  std::cout << "Task state faults and two-way queue/agenda comparisons passed at 10000/1000 and 50000/500.\n";
}
