#if PALA_STAGE3

#include "archive_month.h"
#include "note_config.h"
#include "record.h"
#include "recorder_store.h"
#include "usb_sync.h"

#include <array>
#include <dirent.h>
#include <sys/stat.h>
#include <unistd.h>

namespace {
using namespace archive_month;
Job job("/sdcard/.pala-archive-delete-v1");
SpaceRefresh capacity;
bool mounted = false, failed = false;
std::optional<ArchiveMonthConfirmation> confirmation;
uint32_t promptedAt = 0;
constexpr uint32_t confirmationLifetimeMs = 120000;

std::string monthPath(Month month) {
  char date[8];
  std::snprintf(date, sizeof(date), "%06d", month.key);
  return std::string(month.legacy ? "/sdcard/notes/" : "/sdcard/recordings/") + date;
}

bool storageBusy() {
  return pendingPriorityRecording() || usbSyncActive() || bluetoothSyncActive();
}

bool scanMonth(Month month, const Visitor& visit) {
  if (!mounted || storageBusy() || !recorderWorkReady()) return false;
  const auto directory = monthPath(month);
  DIR* handle = opendir(directory.c_str());
  if (!handle) return false;
  bool done = false;
  while (!storageBusy()) {
    errno = 0;
    const auto* entry = readdir(handle);
    if (!entry) { done = errno == 0; break; }
    const std::string_view name(entry->d_name);
    if (name == "." || name == "..") continue;
    if (!validFilename(name)) break;
    const std::string filename(name);
    struct stat info{};
    if (::stat((directory + "/" + filename).c_str(), &info) != 0 || !S_ISREG(info.st_mode) ||
        !visit({filename, 0, Pending::None, static_cast<uint64_t>(info.st_size),
                static_cast<uint64_t>(info.st_mtime)})) break;
  }
  if (closedir(handle) != 0 || !done) return false;
  bool complete = true;
  const bool ready = visitRecorderWork(false, [&](const SyncQueueEntry& row) {
    if (storageBusy()) { complete = false; return false; }
    if (!belongsToMonth(month, row.note)) return true;
    const auto phase = row.kind == SyncQueueKind::Audio ? Pending::Audio : Pending::Delivery;
    complete = visit({"", row.note, phase});
    return complete;
  });
  return ready && complete && !storageBusy();
}

bool removeMember(Month month, const Member& member) {
  if (member.note) return discardRecorderArchiveWork(member.note);
  const std::string path = monthPath(month) + "/" + member.name;
  struct stat info{};
  if (::stat(path.c_str(), &info) != 0) return errno == ENOENT;
  // This also protects a subdirectory or a different file kind introduced
  // after the confirmation, even if it matches an approved filename.
  if (!S_ISREG(info.st_mode) || static_cast<uint64_t>(info.st_size) != member.size ||
      static_cast<uint64_t>(info.st_mtime) != member.modified) return true;
  return ::unlink(path.c_str()) == 0;
}

void finishMonth(Month month) {
  // New captures may now be present. Removing a nonempty directory fails
  // without touching its contents.
  ::rmdir(monthPath(month).c_str());
}

std::string newToken() {
  std::array<uint8_t, 16> entropy{};
  if (!noteConfigRandom(entropy.data(), entropy.size())) return {};
  constexpr char digits[] = "0123456789abcdef";
  std::string token;
  token.reserve(entropy.size() * 2);
  for (uint8_t byte : entropy) {
    token.push_back(digits[byte >> 4]);
    token.push_back(digits[byte & 15]);
  }
  return token;
}
} // namespace

void mountArchiveMonthDeletion() {
  mounted = true;
  job.active();
}

bool archiveMonthDeletionActive() { return mounted && job.active(); }
bool archiveMonthDeletionFailed() { return failed; }

void maintainArchiveMonthDeletion() {
  const bool deleting = !failed && archiveMonthDeletionActive();
  if ((!deleting && !capacity.pending()) || storageBusy()) return;
  RecordingWindow microphone;
  if (!deleting) {
    capacity.service(storageBusy, refreshRecordingSpace);
    return;
  }
  const auto progress = job.advance(removeMember, finishMonth, storageBusy);
  capacity.observe(progress);
  if (progress == archive_month::Progress::Error) {
    failed = true;
    Serial.println("[Archive] Cannot replay the month deletion journal.");
  }
}

void cancelArchiveMonthConfirmation() { confirmation.reset(); }

std::optional<ArchiveMonthConfirmation> prepareArchiveMonthDeletion(archive_month::Month month) {
  cancelArchiveMonthConfirmation();
  if (!mounted || archiveMonthDeletionActive()) return {};
  RecordingWindow microphone;
  const auto inventory = archive_month::preview(month, scanMonth);
  if (!inventory) return {};
  auto token = newToken();
  if (token.empty()) return {};
  promptedAt = millis();
  confirmation = ArchiveMonthConfirmation{month, *inventory, std::move(token)};
  return confirmation;
}

archive_month::Stage confirmArchiveMonthDeletion(archive_month::Month month, std::string_view token) {
  using archive_month::Stage;
  auto approved = std::exchange(confirmation, std::nullopt);
  if (!approved || !(approved->month == month) || approved->token != token ||
      millis() - promptedAt >= confirmationLifetimeMs) return Stage::Changed;
  if (storageBusy() || !mounted || archiveMonthDeletionActive()) return Stage::Unavailable;
  RecordingWindow microphone;
  return job.stage(month, approved->inventory, scanMonth, [](std::FILE* file) {
    return !storageBusy() && ::fsync(::fileno(file)) == 0;
  });
}
#endif
