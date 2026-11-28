#include "restart_maintenance.h"
#include "owner_trace.h"

#include <Arduino.h>
#include <Preferences.h>
#include <SD_MMC.h>
#include <algorithm>
#include <cerrno>
#include <cstdio>
#include <dirent.h>
#include <iterator>
#include <esp_attr.h>
#include <esp_system.h>

#include "../../config.h"
#include "../../globals.h"
#include "date_validation.h"
#include "note_trash_policy.h"
#include "monthly_recordings.h"
#include "notes.h"
#include "sync_queue.h"
#include "tasks.h"
#include "task_state_storage.h"

namespace {
using namespace note_maintenance;
constexpr char kNamespace[] = "pala-audit";
constexpr char kReportKey[] = "report";
constexpr uint32_t kAuditCookie = 0xDCD3185B;
constexpr uint32_t kReportVersion = 1;
constexpr uint32_t kRepairStallMs = 60'000;
// Only a real software restart from the physical confirmation can launch
// the exclusive check. The persistent report is left Running until complete.
RTC_NOINIT_ATTR uint32_t auditCookie;
RestartConfirmation confirmation;
portMUX_TYPE confirmationLock = portMUX_INITIALIZER_UNLOCKED;

struct AuditReport {
  uint32_t version = kReportVersion;
  AuditState state = AuditState::Never;
  uint32_t total = 0;
  uint32_t checked = 0;
  uint32_t wavIssues = 0;
  uint32_t metadataIssues = 0;
  uint32_t catalogIssues = 0;
  uint32_t queueIssues = 0;
  uint32_t firstNote = 0;
  uint32_t sampleNotes = 0;
  uint32_t sampleMs = 0;
  uint32_t repaired = 0;

  uint32_t problems() const { return wavIssues + metadataIssues + catalogIssues + queueIssues; }
};

AuditReport bootReport;
bool bootClaimed = false;
AuditDisplay* activeDisplay = nullptr;
AuditReport* activeReport = nullptr;

AuditReport readReport() {
  Preferences prefs;
  AuditReport report;
  if (prefs.begin(kNamespace, true)
      && prefs.getBytesLength(kReportKey) == sizeof(report)
      && prefs.getBytes(kReportKey, &report, sizeof(report)) == sizeof(report)
      && report.version == kReportVersion && report.state <= AuditState::Complete) return report;
  return {};
}

bool saveReport(const AuditReport& report) {
  Preferences prefs;
  AuditReport readback;
  return prefs.begin(kNamespace, false)
      && prefs.putBytes(kReportKey, &report, sizeof(report)) == sizeof(report)
      && prefs.getBytes(kReportKey, &readback, sizeof(readback)) == sizeof(readback)
      && std::memcmp(&report, &readback, sizeof(report)) == 0;
}

void reportIssue(uint32_t& counter, AuditReport& report, int note = 0) {
  ++counter;
  if (note > 0 && report.firstNote == 0) report.firstNote = static_cast<uint32_t>(note);
}

// Consume the full line but bound scratch space, even for a damaged file.
template <size_t N>
bool readLine(File& file, char (&buffer)[N], std::string_view& line, bool& overflow) {
  size_t used = 0;
  bool present = false;
  overflow = false;
  for (int byte = file.read(); byte >= 0; byte = file.read()) {
    present = true;
    if (byte == '\n') break;
    if (used < N - 1) buffer[used++] = static_cast<char>(byte);
    else overflow = true;
  }
  if (used && buffer[used - 1] == '\r') --used;
  buffer[used] = '\0';
  line = {buffer, used};
  return present;
}

void notePath(char (&path)[64], int note, const char* extension) {
  monthly_recordings::path(path, sizeof(path), NOTES_DIR, note, extension);
}

bool readWav(int note) {
  char path[64];
  notePath(path, note, "wav");
  File file = SD_MMC.open(path, FILE_READ);
  if (!file || file.isDirectory()) return false;
  return checkWav(file.size(), [&](uint32_t offset, uint8_t* buffer, size_t bytes) {
    return (file.position() == offset || file.seek(offset)) && file.read(buffer, bytes) == bytes;
  });
}

bool validFields(File& file, const NoteEntry* entry, bool queue, bool* synced = nullptr) {
  char buffer[256];
  std::string_view line;
  bool overflow;
  unsigned seen = 0;
  while (readLine(file, buffer, line, overflow)) {
    if (overflow || line.find('\0') != line.npos) return false;
    if (line.empty()) continue;
    const auto separator = line.find('=');
    if (separator == line.npos || separator == 0) return false;
    const auto key = line.substr(0, separator);
    const auto value = line.substr(separator + 1);
    unsigned flag = 0;
    if (queue) {
      if (key == "state") {
        constexpr std::string_view states[] = {
            "queued", "transcribing", "organizing", "indexing", "ready", "blocked", "retry", "usb-local"};
        if (std::find(std::begin(states), std::end(states), value) == std::end(states)) return false;
        flag = 1;
      } else if (key == "retries" || key == "failures") {
        uint32_t count;
        if (!parseUnsigned(value, INT32_MAX, count)) return false;
        flag = key == "retries" ? 2 : 4;
      } else if (key == "updated_utc" || key == "last_failure_utc") {
        if (!value.empty() && !validUtcTimestamp(value.data())) return false;
        flag = key == "updated_utc" ? 8 : 16;
      }
    } else if (key == "tag") {
      if (value.size() > 15 || (entry && value != entry->tag)) return false;
      flag = 1;
    } else if (key == "synced") {
      if (value != "0" && value != "1") return false;
      const bool hasText = value == "1";
      if (entry && entry->hasText != hasText) return false;
      if (synced) *synced = hasText;
      flag = 2;
    } else if (key == "created_utc") {
      if (!value.empty() && !validUtcTimestamp(value.data())) return false;
      flag = 4;
    }
    if (seen & flag) return false;
    seen |= flag;
  }
  // Older recordings can lack the capture timestamp and queue retry fields.
  return file.position() == file.size() && (queue ? (seen & 1) != 0 : (seen & 3) == 3);
}

bool checkMetadata(int note, const NoteEntry* entry) {
  char path[64];
  bool synced = false;
  notePath(path, note, "meta");
  {
    File file = SD_MMC.open(path, FILE_READ);
    if (!file || file.isDirectory() || !validFields(file, entry, false, &synced)) return false;
  }
  notePath(path, note, "txt");
  {
    File transcript = SD_MMC.open(path, FILE_READ);
    const bool hasText = transcript && !transcript.isDirectory() && transcript.size() != 0;
    if (hasText != synced) return false;
  }
  notePath(path, note, "brain.json");
  File structured = SD_MMC.open(path, FILE_READ);
  if (!structured) {
    // A missing structured file is normal with pending organization. An
    // interrupted source commit, however, is only reported by this audit.
    return !SD_MMC.exists(path) && !SD_MMC.exists((String(path) + ".bak").c_str());
  }
  if (structured.isDirectory()) return false;
  JsonDocument filter, metadata;
  filter["noteNum"] = true;
  filter["schemaVersion"] = true;
  if (deserializeJson(metadata, structured, DeserializationOption::Filter(filter))
      || !metadata.is<JsonObject>()) return false;
  return metadata["noteNum"].isNull() ||
      (metadata["noteNum"].is<int>() && metadata["noteNum"].as<int>() == note);
}

bool checkQueueFields(int note) {
  char path[64];
  notePath(path, note, "queue");
  File file = SD_MMC.open(path, FILE_READ);
  // No sidecar is a supported legacy state. The durable front is validated
  // independently and can be rebuilt from files.
  return !file ? !SD_MMC.exists(path) && !SD_MMC.exists((String(path) + ".bak").c_str())
      : !file.isDirectory() && validFields(file, nullptr, true);
}

bool checkTaskState() {
  task_storage::Source file;
  if (!file.open("/sdcard" NOTES_DIR "/tasks.state") || file.backup) return false;
  std::string buffer;
  size_t rows = 0;
  while (true) {
    if (!file.line(buffer)) return false;
    if (buffer.empty()) break;
    noteAuditWork(NoteStorageAuditPhase::TaskState, ++rows);
    std::string_view line(buffer);
    if (line.back() == '\n') line.remove_suffix(1);
    if (!line.empty() && line.back() == '\r') line.remove_suffix(1);
    if (line.empty()) continue;
    const auto first = line.find('|');
    if (first == line.npos || first == 0 || first > 32) return false;
    const auto second = line.find('|', first + 1);
    if (second != first + 2 || (line[first + 1] != '0' && line[first + 1] != '1')) return false;
    const auto third = line.find('|', second + 1);
    const auto timestamp = line.substr(second + 1, third == line.npos ? line.npos : third - second - 1);
    if (!timestamp.empty()) {
      char utc[21];
      if (timestamp.size() != 20) return false;
      std::memcpy(utc, timestamp.data(), timestamp.size());
      utc[20] = '\0';
      if (!validUtcTimestamp(utc)) return false;
    }
    if (rows % 128 == 0) delay(1);
  }
  return file.close();
}

bool checkCatalog(AuditReport& report) {
  File catalog = SD_MMC.open(INDEX_FILE, FILE_READ);
  if (!noteCatalogBaseLoaded() || !catalog || catalog.isDirectory()) {
    reportIssue(report.catalogIssues, report);
    return false;
  }
  char buffer[80];
  std::string_view line;
  bool overflow = false;
  int previous = 0;
  size_t position = 0;
  uint32_t rows = 0;
  while (readLine(catalog, buffer, line, overflow)) {
    noteAuditWork(NoteStorageAuditPhase::CheckCatalog, ++rows, noteIndex.size());
    if (line.empty() && !overflow) continue;
    const auto row = parseCatalogRow(line);
    if (overflow || !row.valid || row.note <= previous) {
      reportIssue(report.catalogIssues, report, row.note);
      continue;
    }
    previous = row.note;
    if (position >= noteIndex.size() || noteIndex[position].num != row.note
        || noteIndex[position].hasText != row.hasTranscript || noteIndex[position].momentCount != row.moments) {
      reportIssue(report.catalogIssues, report, row.note);
    }
    ++position;
    if (position % 128 == 0) delay(1);
  }
  const bool read = catalog.position() == catalog.size();
  if (!read || position != noteIndex.size()) reportIssue(report.catalogIssues, report);
  return read;
}

void checkNote(int note, const NoteEntry* entry, AuditReport& report) {
  if (!readWav(note)) reportIssue(report.wavIssues, report, note);
  if (!checkMetadata(note, entry)) reportIssue(report.metadataIssues, report, note);
  if (!checkQueueFields(note)) reportIssue(report.queueIssues, report, note);
  ++report.checked;
  noteAuditWork(NoteStorageAuditPhase::Checking, report.checked, report.total);
  if (report.checked % 16 == 0) delay(1);
}

bool orphanPrimary(const std::string_view extension, int note) {
  if (extension == ".wav") return true;
  char path[64];
  notePath(path, note, "wav");
  if (SD_MMC.exists(path)) return false;
  if (extension == ".meta") return true;
  notePath(path, note, "meta");
  return !SD_MMC.exists(path);
}

uint32_t visitOrphans(AuditReport& report, bool check, bool& complete) {
  // POSIX readdir avoids reopening each file just to obtain its name.
  DIR* directory = opendir("/sdcard" NOTES_DIR);
  if (!directory) {
    complete = false;
    reportIssue(report.catalogIssues, report);
    return 0;
  }
  uint32_t found = 0, orphans = 0;
  for (;;) {
    errno = 0;
    const dirent* entry = readdir(directory);
    if (!entry) {
      if (errno) { complete = false; reportIssue(report.catalogIssues, report); }
      break;
    }
    ++found;
    noteAuditWork(check ? NoteStorageAuditPhase::ScanOrphans : NoteStorageAuditPhase::Discovering, found);
    if (found % 128 == 0) delay(1);
    const std::string_view name = entry->d_name;
    if (entry->d_type == DT_DIR || name.size() < 5) continue;
    const auto suffix = name.substr(name.size() - 5);
    const char* extension = note_trash::equalsAscii(suffix, ".meta") ? ".meta"
        : note_trash::equalsAscii(suffix.substr(1), ".wav") ? ".wav"
        : note_trash::equalsAscii(suffix.substr(1), ".txt") ? ".txt" : nullptr;
    if (!extension) continue;
    const int note = note_trash::fileNumber(name);
    if (note <= 0 || noteIsTrashed(note)) continue;
    const auto at = std::lower_bound(noteIndex.begin(), noteIndex.end(), note,
        [](const NoteEntry& item, int number) { return item.num < number; });
    if ((at != noteIndex.end() && at->num == note) || !orphanPrimary(extension, note)) continue;
    ++orphans;
    if (check) checkNote(note, nullptr, report);
    else reportIssue(report.catalogIssues, report, note);
  }
  closedir(directory);
  return orphans;
}

void checkOriginals(AuditReport& report, uint32_t orphans, bool& complete) {
  report.total = static_cast<uint32_t>(noteIndex.size()) + orphans;
  noteAuditWork(NoteStorageAuditPhase::Checking, 0, report.total);
  for (const NoteEntry& entry : noteIndex) checkNote(entry.num, &entry, report);
  if (orphans && visitOrphans(report, true, complete) != orphans) complete = false;
}

bool repairDerived(AuditReport& report) {
  bool complete = true;
  // Recovery code used by the rebuilt indexes can restore interrupted
  // source transactions. Leave them untouched during a read-only audit.
  const bool sourceHealthy = noteCatalogBaseLoaded() && report.problems() == 0;
  const auto verifyPublishedAgenda = [&](bool finalPass) {
    bool agendaValid = agendaIndexReady();
    uint32_t position = 0;
    while (agendaValid && position != UINT32_MAX) {
      agendaValid = verifyAgendaIndexBatch(position, finalPass);
      delay(1);
    }
    return agendaValid;
  };
  const bool agendaNeedsWork = !verifyPublishedAgenda(false) || !verifyAgendaSources();
  const bool queueNeedsWork = !syncQueueReady() || !verifySyncQueueSources();
  if (!agendaNeedsWork && !queueNeedsWork) return true;
  if (agendaNeedsWork) reportIssue(report.catalogIssues, report);
  if (queueNeedsWork) reportIssue(report.queueIssues, report);
  if (!sourceHealthy) return false;  // Required index verification cannot finish with damaged sources.
  if (agendaNeedsWork) {
    if (!requestAgendaIndexRebuild(true)) {
      complete = false;
      reportIssue(report.catalogIssues, report);
    } else {
      while (!agendaIndexReady()) {
        if (!maintainAgendaIndex()) {
          complete = false;
          reportIssue(report.catalogIssues, report);
          break;
        }
        delay(1);
      }
      if (agendaIndexReady()) {
        if (verifyPublishedAgenda(true)) ++report.repaired;
        else { complete = false; reportIssue(report.catalogIssues, report); }
      }
    }
  }
  if (queueNeedsWork) {
    if (!requestSyncQueueRebuild(true)) {
      complete = false;
      reportIssue(report.queueIssues, report);
    } else {
      int largestScanned = 0;
      uint32_t lastAdvance = millis();
      while (!syncQueueReady()) {
        const auto previous = std::upper_bound(noteIndex.begin(), noteIndex.end(), syncQueueRebuildProgressNote(),
            [](int number, const NoteEntry& note) { return number < note.num; });
        noteAuditWork(NoteStorageAuditPhase::BuildQueue, static_cast<uint32_t>(previous - noteIndex.begin()), noteIndex.size());
        maintainSyncQueue();
        if (syncQueueReady()) break;
        const int scanned = syncQueueRebuildProgressNote();
        if (scanned > largestScanned) {
          largestScanned = scanned;
          lastAdvance = millis();
        } else if (millis() - lastAdvance >= kRepairStallMs) {
          complete = false;
          reportIssue(report.queueIssues, report);
          break;
        }
        delay(1);
      }
      if (syncQueueReady()) {
        if (verifySyncQueueSources(true)) ++report.repaired;
        else { complete = false; reportIssue(report.queueIssues, report); }
      }
    }
  }
  return complete;
}

const char* stateField(AuditState state) {
  if (state == AuditState::Complete) return "complete";
  if (state == AuditState::Incomplete) return "incomplete";
  if (state == AuditState::Running) return "running";
  return "never";
}
}  // namespace

void noteAuditWork(NoteStorageAuditPhase phase, uint32_t done, uint32_t total) {
  if (activeDisplay) activeDisplay->update(millis(), phase, done, total, activeReport->problems());
}

uint32_t requestNoteRestart(NoteRestartMode mode) {
  const uint32_t now = millis();
  portENTER_CRITICAL(&confirmationLock);
  const uint32_t accepted = confirmation.request(mode, now) ? confirmation.ticket() : 0;
  portEXIT_CRITICAL(&confirmationLock);
  return accepted;
}

NoteRestartRequest pendingNoteRestartRequest() {
  const uint32_t now = millis();
  portENTER_CRITICAL(&confirmationLock);
  const auto request = confirmation.pending(now);
  portEXIT_CRITICAL(&confirmationLock);
  return request;
}

void noteRestartDisplayed(NoteRestartRequest request) {
  const uint32_t now = millis();
  portENTER_CRITICAL(&confirmationLock);
  confirmation.displayed(request, now);
  portEXIT_CRITICAL(&confirmationLock);
}

bool noteRestartExpired(uint32_t ticket) {
  const uint32_t now = millis();
  portENTER_CRITICAL(&confirmationLock);
  const bool expired = ticket == confirmation.ticket() && !strcmp(confirmation.state(now), "expired");
  portEXIT_CRITICAL(&confirmationLock);
  return expired;
}

void addNoteRestartConfirmation(JsonObject result) {
  const uint32_t now = millis();
  portENTER_CRITICAL(&confirmationLock);
  const uint32_t ticket = confirmation.ticket();
  const char* state = confirmation.state(now);
  portEXIT_CRITICAL(&confirmationLock);
  result["ticket"] = ticket;
  result["state"] = state;
}

bool answerNoteRestart(NoteRestartRequest request, bool accepted) {
  const uint32_t now = millis();
  portENTER_CRITICAL(&confirmationLock);
  const bool matched = confirmation.answer(request, now, accepted);
  portEXIT_CRITICAL(&confirmationLock);
  if (!matched) return false;
  return executeConfirmedRestart(request.mode, accepted, auditCookie, kAuditCookie, [] {
    auto report = readReport();
    report.state = AuditState::Running;
    report.total = 0;
    report.checked = 0;
    report.wavIssues = 0;
    report.metadataIssues = 0;
    report.catalogIssues = 0;
    report.queueIssues = 0;
    report.firstNote = 0;
    report.repaired = 0;
    return saveReport(report);
  }, [] {
    Serial.flush();
    ESP.restart();
  });
}

void addNoteStorageAuditSettings(JsonObject result, size_t knownNoteCount) {
  const auto report = readReport();
  const auto notes = static_cast<uint32_t>(knownNoteCount);
  result["state"] = stateField(visibleAuditState(report.state, activeDisplay != nullptr));
  result["notes"] = notes;
  if (const auto seconds = estimateAuditSeconds(report.sampleNotes, report.sampleMs, notes))
    result["estimateSeconds"] = seconds;
  if (report.state == AuditState::Complete || (report.state == AuditState::Incomplete && report.problems())) {
    result["checked"] = report.checked;
    result["wavIssues"] = report.wavIssues;
    result["metadataIssues"] = report.metadataIssues;
    result["catalogIssues"] = report.catalogIssues;
    result["queueIssues"] = report.queueIssues;
    result["firstNote"] = report.firstNote;
    result["repaired"] = report.repaired;
  }
}

bool claimNoteStorageAuditForBoot() {
  const bool cookie = auditCookie == kAuditCookie;
  auditCookie = 0;
  bootReport = readReport();
  bootClaimed = claimAuditBoot(cookie, esp_reset_reason() == ESP_RST_SW, bootReport, saveReport);
  return bootClaimed;
}

bool runRequestedNoteStorageAudit(NoteStorageAuditProgressCallback notify) {
  owner_trace::WorkScope ownerWork(owner_trace::Work::Audit);
  if (!bootClaimed) return false;
  bootClaimed = false;
  auto& report = bootReport;
  const uint32_t startedAt = millis();
  AuditDisplay display(startedAt, notify);
  activeReport = &report;
  activeDisplay = &display;
  noteAuditWork(NoteStorageAuditPhase::LoadCatalog, 0);
  initializeNoteCatalog(false);
  initializeSyncQueue();
  bool complete = checkCatalog(report);
  const uint32_t orphans = visitOrphans(report, false, complete);
  checkOriginals(report, orphans, complete);
  if (!checkTaskState()) reportIssue(report.metadataIssues, report);
  complete = repairDerived(report) && complete && report.checked == report.total;
  if (complete && report.checked != 0) {
    report.sampleMs = millis() - startedAt;
    report.sampleNotes = report.checked;
  }
  display.update(millis(), NoteStorageAuditPhase::Saving, report.checked, 0, report.problems(), true);
  const bool saved = finishAudit(complete, report, saveReport);
  activeDisplay = nullptr;
  activeReport = nullptr;
  display.update(millis(), saved ? NoteStorageAuditPhase::Finished : NoteStorageAuditPhase::Incomplete,
                 report.checked, report.total, report.problems(), true);
  Serial.printf("[Storage audit] %s, %lu/%lu checked in %lu ms, %lu problems\\n",
      saved ? "complete" : "incomplete", static_cast<unsigned long>(report.checked),
      static_cast<unsigned long>(report.total), static_cast<unsigned long>(millis() - startedAt),
      static_cast<unsigned long>(report.problems()));
  return saved;
}
