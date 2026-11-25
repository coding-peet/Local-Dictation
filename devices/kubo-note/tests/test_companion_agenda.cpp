// Run the actual Companion GET/POST handlers with an in-process HTTP sink.
// The SD index adapter publishes the saved state on its third maintenance slice.
#include "../pala_note/src/app/task_state_storage.h"
#include <array>
#include <cassert>
#include <chrono>
#include <cstdint>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <map>
#include <type_traits>
#include <vector>

struct String {
  std::string s;
  String() = default;
  String(const char* value) : s(value) {}
  String(std::string value) : s(value) {}
  template<class T, typename = std::enable_if_t<std::is_integral_v<T>>>
  explicit String(T value) : s(std::to_string(value)) {}
  size_t length() const { return s.size(); }
  bool isEmpty() const { return s.empty(); }
  const char* c_str() const { return s.c_str(); }
  char operator[](size_t i) const { return s[i]; }
  long toInt() const { return std::strtol(c_str(), nullptr, 10); }
  int indexOf(char c) const { const auto at = s.find(c); return at == s.npos ? -1 : static_cast<int>(at); }
  String substring(size_t from, size_t until = std::string::npos) const {
    return from >= s.size() ? String() : String(s.substr(from, until == s.npos ? until : until - from));
  }
  String& operator+=(const String& value) { s += value.s; return *this; }
  friend String operator+(String a, const String& b) { a += b; return a; }
  friend bool operator<(const String& a, const String& b) { return a.s < b.s; }
  friend bool operator==(const String& a, const String& b) { return a.s == b.s; }
  friend bool operator!=(const String& a, const String& b) { return a.s != b.s; }
};

struct Http {
  std::map<std::string, String> args, headers;
  std::string body;
  int status = 0;
  void reset() { args.clear(); headers.clear(); body.clear(); status = 0; }
  String arg(const char* key) { return args[key]; }
  void sendHeader(const char* key, const String& value) { headers[key] = value; }
  void setContentLength(int) {}
  void send(int code, const char* = "", const String& html = {}) { status = code; body = html.s; }
  void sendContent(const String& html) { body += html.s; }
} transferServer;
constexpr int CONTENT_LENGTH_UNKNOWN = -1;

const char* viewerText(const char*, const char* en) { return en; }
String htmlEscape(const String& value) { return value; }
String portalPageStart(const String&) { return "<html>"; }
String portalNav(const char*) { return "<nav></nav>"; }
String palaNoteName() { return "Test Note"; }
String currentUtcIso() { return "2026-11-24T12:00:00Z"; }
String utcToLocalDeviceLabel(const String& utc) { return utc; }
int weekDayIndex(int, int, int) { return 2; }
int weekMonthDays(int, int) { return 30; }
bool positiveFormValue(const char* key, int& result) {
  result = static_cast<int>(transferServer.arg(key).toInt());
  return result > 0;
}

struct BrainTaskRecord {
  int noteNum = 7;
  String id = "7-a", text = "Target task", dueUtc = "2026-11-25T12:00:00Z", datePrecision = "time", type = "todo";
  bool done = false;
};
struct AgendaIndexCursor { uint32_t next, end, generation; bool recent; uint8_t mask = 0, match = 0; };
enum class TaskEdit { Saved, Invalid, Missing, StorageError };
std::vector<BrainTaskRecord> rows;
int pendingSlices = 0, slicesRun = 0;
uint32_t generation = 10;
uint32_t agendaIndexGeneration() { return generation; }
bool noteCatalogReady() { return true; }
void maintainNoteCatalog() { assert(false); }
bool agendaIndexReady() { return pendingSlices == 0; }
void requestAgendaIndexRebuild() { pendingSlices = 3; ++generation; }
void maintainAgendaIndex() {
  ++slicesRun;
  if (--pendingSlices) return;
  std::string line;
  std::ifstream file("tasks.state");
  assert(std::getline(file, line));
  rows.back().done = line.rfind("7-a|1|", 0) == 0;
  ++generation;
}
TaskEdit editBrainTask(const String& id, bool done) {
  if (id != "7-a") return TaskEdit::Missing;
  return task_storage::edit("tasks.state", id.c_str(), done ? "7-a|1||target" : "7-a|0||target",
                            [] { requestAgendaIndexRebuild(); return true; }) ? TaskEdit::Saved : TaskEdit::StorageError;
}
void agendaTaskCounts(uint32_t& total, uint32_t& open) {
  total = static_cast<uint32_t>(rows.size());
  open = total - (rows.back().done ? 1 : 0);
}
bool openAgendaIndex(AgendaIndexCursor& cursor, bool recent, const char* = nullptr) {
  if (!agendaIndexReady()) return false;
  const auto n = static_cast<uint32_t>(rows.size());
  cursor = {recent ? n : 0U, recent ? 0U : n, generation, recent};
  return true;
}
bool visitAgendaDates(AgendaIndexCursor& c, bool (*visit)(const char*, bool, void*), void* ctx,
                      uint32_t budget, bool& finished) {
  while (budget-- && c.next < c.end) {
    const auto& row = rows[c.next++];
    if (!visit(row.dueUtc.c_str(), false, ctx)) { finished = true; return true; }
  }
  finished = c.next == c.end;
  return true;
}
bool visitAgendaIndex(AgendaIndexCursor& c, bool (*visit)(const BrainTaskRecord&, void*), void* ctx,
                      uint32_t budget = UINT32_MAX, bool* more = nullptr) {
  while (budget-- && c.next != c.end) {
    const auto pos = c.recent ? --c.next : c.next++;
    if (!visit(rows[pos], ctx)) return true;
  }
  if (more) *more = c.next != c.end;
  return true;
}

#include "../pala_note/src/app/agenda_web.inc"

int main() {
  namespace fs = std::filesystem;
  const auto before = fs::current_path();
  const auto nonce = std::chrono::steady_clock::now().time_since_epoch().count();
  const auto directory = fs::temp_directory_path() / ("pala-companion-" + std::to_string(nonce));
  assert(fs::create_directory(directory));
  fs::current_path(directory);
  for (int i = 0; i < 150; ++i) {
    BrainTaskRecord earlier;
    earlier.id = "other";
    earlier.text = "Earlier month";
    earlier.dueUtc = "2026-10-31T12:00:00Z";
    rows.push_back(earlier);
  }
  rows.push_back({});
  const auto get = [&](auto handler) {
    transferServer.reset();
    handler();
    assert(transferServer.headers["Cache-Control"] == "no-store");
    return transferServer.status;
  };
  assert(get(handleTasksPage) == 200 && transferServer.body.find("Target task") != std::string::npos);
  assert(get(handleCalendarPage) == 503); // Bounded month scan continues during transfer.
  maintainCompanionAgenda();
  assert(get(handleCalendarPage) == 200 && transferServer.body.find("Open") != std::string::npos);

  for (const bool done : {true, false}) {
    const auto previousRevision = generation;
    transferServer.reset();
    transferServer.args = {{"id", "7-a"}, {"done", done ? "1" : "0"}, {"return", "/tasks"}};
    handleTaskStatus();
    assert(transferServer.status == 303 && transferServer.headers["Location"] == "/tasks");
    assert(get(handleTasksPage) == 503 && transferServer.headers["Refresh"] == "2");
    assert(get(handleCalendarPage) == 503 && transferServer.headers["Retry-After"] == "2");
    const auto beforeSlices = slicesRun;
    while (!agendaIndexReady()) maintainCompanionAgenda();
    assert(slicesRun - beforeSlices == 3);
    transferServer.reset();
    transferServer.args = {{"rev", String(previousRevision)}, {"start", "1"}};
    handleTasksPage();
    assert(transferServer.status == 200 && transferServer.body.find("Target task") != std::string::npos);
    assert((transferServer.body.find("taskrow completed") != std::string::npos) == done);
    assert(get(handleCalendarPage) == 503); // A month cache from the former generation cannot be reused.
    maintainCompanionAgenda();
    assert(get(handleCalendarPage) == 200 && transferServer.body.find("Target task") != std::string::npos);
    assert((transferServer.body.find("agenda-item completed") != std::string::npos) == done);
    assert(transferServer.body.find(done ? "Done" : "Open") != std::string::npos);
  }
  transferServer.reset();
  transferServer.args = {{"id", "7-a"}, {"done", "bogus"}};
  handleTaskStatus();
  assert(transferServer.status == 400 && agendaIndexReady());
  fs::current_path(before);
  fs::remove_all(directory);
  std::cout << "Tasks and Calendar refresh after completing and reopening a task in one Companion session.\n";
}
