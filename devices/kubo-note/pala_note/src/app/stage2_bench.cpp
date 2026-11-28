#include "stage2_bench.h"
#include "wifi_trial.h"
#if PALA_STAGE2_BENCH
#include "../audio/microphone_health.h"
#include <atomic>
#include <cerrno>
#include <esp_attr.h>
#include <esp_system.h>
#if PALA_VFS_OWNER_TRACE
#include <esp_app_desc.h>
#include "pcm_capture_buffer.h"
#include "record.h"
#endif
#if PALA_STORAGE_PROBE
#include "wav_io_timing.h"
#endif

namespace stage2 {
namespace {
// Only the initial REC screen owns this trace. The UI task publishes
// its result via recordingUiBusy; the foreground reports after joining.
struct DisplayTrace {
  uint32_t edge, request, draw, refresh, waveform, waitEnd, returned;
  const char* mode;
  const char* status;
  bool drawing, waveActive, busySeen;
};
DisplayTrace initialScreen{};
thread_local DisplayTrace* currentScreen = nullptr;

// The owner diagnostic pins the first two takes until reset. Its third slot
// services later recordings and preserves the legacy latest-take endpoint.
// Only fixed-size assignments occur under this lock. Format USB replies outside it.
constexpr const char* screenFields[] = {
    "request", "draw", "ready", "claim", "upload", "wave_full", "wave_partial",
    "busy", "idle", "timeout", "no_busy", "plane", "done", "cache", "fallback",
    "setup_done"
};
constexpr const char* storageFields[] = {
    "dequeue", "ready", "stat_wav", "recover", "allocate", "directory", "path_end",
    "serial", "open", "reserve", "seek", "header_write", "flush", "fsync",
    "stall_begin", "stall_end", "first_write", "first_write_error"
};
constexpr const char* releaseFields[] = {
    "button", "mic_stop", "queued", "saving_ready", "wave_full", "wave_partial", "busy",
    "idle", "timeout", "no_busy", "committed", "superseded", "saved_request", "saved_idle"
};
using ReleaseTimes = uint32_t[sizeof(releaseFields) / sizeof(*releaseFields)];
using ScreenTimes = uint32_t[sizeof(screenFields) / sizeof(*screenFields)];
using StorageTimes = uint32_t[sizeof(storageFields) / sizeof(*storageFields)];

#if PALA_STORAGE_PROBE
struct IoPhaseMetrics {
  uint32_t edge, result, error;
  storage_probe::Sample sample;
};
#endif

struct PriorSaveTimes {
  uint32_t priorEdge, pendingAt, finishedAt, finishOk, visibleOn, visibleOff;
};

struct TakeMetrics {
  PriorSaveTimes overlap;
  uint32_t edge, accepted, pcm, framesIn, framesOut, crc;
  uint32_t wavCollisions, firstWavCollision, lastWavCollision;
  int note;
  bool inbox, overflow, saved;
  char inboxFile[32];
  microphone::Stats audio;
#if PALA_STORAGE_PROBE
  IoPhaseMetrics ioOpen, ioHeader;
  WavOpenParts ioCalls;
#endif
  ReleaseTimes release;
  ScreenTimes screen;
  StorageTimes storage;
};
#if PALA_VFS_OWNER_TRACE
constexpr size_t pinnedTakes = 2;
#else
constexpr size_t pinnedTakes = 0;
#endif
struct BenchMetrics {
  uint32_t revision;
  ScreenTimes home;
  uint32_t seen;
  size_t latest;
  TakeMetrics takes[pinnedTakes + 1];
};
BenchMetrics retained{};
constexpr size_t bleBlockCapacity = 32;
struct BleBlockTimes {
  uint32_t sequence, offset, bytes, requested, firstQueued, lastQueued, complete;
};
BleBlockTimes bleBlocks[bleBlockCapacity]{};
uint32_t bleBlockSequence = 0;

// Called only while metricsLock is held. Late display and archive callbacks
// can still update either pinned take after a later REC edge.
TakeMetrics* findTake(uint32_t edge) {
  for (auto& t : retained.takes) {
    if (t.accepted && t.edge == edge) return &t;
  }
  return nullptr;
}
portMUX_TYPE metricsLock = portMUX_INITIALIZER_UNLOCKED;

template <size_t N>
int fieldIndex(const char* name, const char* const (&fields)[N]) {
  for (size_t i = 0; i < N; ++i)
    if (strcmp(name, fields[i]) == 0) return static_cast<int>(i);
  return -1;
}

void recordTime(uint32_t& field, uint32_t at) {
  if (!field) {
    field = at;
    ++retained.revision;
  }
}

template <size_t N>
void appendTimes(String& page, const uint32_t (&times)[N]) {
  for (uint32_t at : times) {
    page += ' ';
    page += String(at);
  }
}

struct NamedPoint { const char* name; Point point; };
constexpr NamedPoint points[] = {{"off", Point::None}, {"open", Point::Open}, {"pcm", Point::Pcm},
                                 {"publish", Point::Publish}, {"text", Point::Text}, {"receipt", Point::Receipt},
                                 {"lose-receipt", Point::LoseReceipt}};
CutSelection armed;
portMUX_TYPE guard = portMUX_INITIALIZER_UNLOCKED;
bool consume(Point point, int note) {
  portENTER_CRITICAL(&guard);
  const bool selected = armed.consume(point, note);
  portEXIT_CRITICAL(&guard);
  return selected;
}
std::atomic<bool> invalid{false};
// The software-restart trial should exercise the actual untrusted boot path
// without modifying RTC calendar registers or leaving a persistent SD flag.
RTC_NOINIT_ATTR uint32_t skipClockOnRestart;
constexpr uint32_t skipMagic = 0x73524732;
#if defined(PALA_TEST_FIRST_WRITE_DELAY_MS)
constexpr uint32_t configuredStallMs = PALA_TEST_FIRST_WRITE_DELAY_MS;
#else
constexpr uint32_t configuredStallMs = 0;
#endif
}

void displayRequest(uint32_t edge) {
  initialScreen = {};
  initialScreen.edge = edge;
  initialScreen.request = millis();
  initialScreen.mode = "none";
  initialScreen.status = "missing";
}

void displayDraw() {
  currentScreen = &initialScreen;
  currentScreen->draw = millis();
  currentScreen->drawing = true;
}

void displayRefresh() {
  if (currentScreen) currentScreen->refresh = millis();
}

void displayWaveform(bool full) {
  if (!currentScreen) return;
  currentScreen->mode = full ? "full" : "partial";
  currentScreen->waveform = millis();
  currentScreen->waveActive = true;
}

void displayBusy() {
  if (currentScreen && currentScreen->waveActive) currentScreen->busySeen = true;
}

void displayWaitEnd(bool timedOut) {
  if (!currentScreen) return;
  currentScreen->waitEnd = millis();
  currentScreen->waveActive = false;
  if (timedOut) currentScreen->status = "timeout";
  else currentScreen->status = currentScreen->busySeen ? "idle" : "no_busy";
}

void displayReturned() {
  initialScreen.returned = millis();
  currentScreen = nullptr;
}

void reportDisplay() {
  const auto& t = initialScreen;
  if (!t.status) return;
  const auto elapsed = [&](uint32_t stamp) -> unsigned long { return stamp - t.edge; };
  Serial.printf("[RecDisplay] edge=%lu request=%lu draw=%lu refresh=%lu wave=%lu wait_end=%lu done=%lu mode=%s status=%s\n",
                (unsigned long)t.edge, elapsed(t.request), t.drawing ? elapsed(t.draw) : 0UL,
                t.drawing ? elapsed(t.refresh) : 0UL, t.drawing ? elapsed(t.waveform) : 0UL,
                t.drawing ? elapsed(t.waitEnd) : 0UL, t.drawing ? elapsed(t.returned) : 0UL, t.mode, t.status);
  initialScreen.status = nullptr;
}

uint32_t bleBlockRequested(uint32_t offset, uint32_t bytes) {
  const uint32_t requested = millis();
  portENTER_CRITICAL(&metricsLock);
  const uint32_t sequence = ++bleBlockSequence;
  bleBlocks[(sequence - 1) % bleBlockCapacity] = {sequence, offset, bytes, requested, 0, 0, 0};
  ++retained.revision;
  portEXIT_CRITICAL(&metricsLock);
  return sequence;
}

void bleBlockNotifications(uint32_t sequence, uint32_t firstQueuedAt, uint32_t lastQueuedAt, bool completed) {
  portENTER_CRITICAL(&metricsLock);
  auto& block = bleBlocks[(sequence - 1) % bleBlockCapacity];
  if (block.sequence == sequence) {
    recordTime(block.firstQueued, firstQueuedAt);
    block.lastQueued = lastQueuedAt;
    block.complete = completed;
    ++retained.revision;
  }
  portEXIT_CRITICAL(&metricsLock);
}

void captureStart(uint32_t edge, bool inbox, uint32_t acceptedMs) {
  portENTER_CRITICAL(&metricsLock);
  if (!findTake(edge)) {
    retained.latest = retained.seen < pinnedTakes ? retained.seen : pinnedTakes;
    if (retained.seen != UINT32_MAX) ++retained.seen;
    auto& t = retained.takes[retained.latest];
    t = {};
    t.edge = edge;
    t.accepted = acceptedMs;
    t.inbox = inbox;
    ++retained.revision;
  }
  portEXIT_CRITICAL(&metricsLock);
}

void firstPcm(uint32_t edge, uint32_t sampledAt) {
  portENTER_CRITICAL(&metricsLock);
  if (auto* t = findTake(edge)) recordTime(t->pcm, sampledAt);
  portEXIT_CRITICAL(&metricsLock);
}

void displayEvent(uint32_t edge, const char* phase, uint32_t at) {
  const int field = fieldIndex(phase, screenFields);
  if (field < 0) return;
  portENTER_CRITICAL(&metricsLock);
  if (auto* t = findTake(edge)) recordTime(t->screen[field], at);
  portEXIT_CRITICAL(&metricsLock);
}

void homeEvent(const char* phase, uint32_t at) {
  const int field = fieldIndex(phase, screenFields);
  if (field < 0) return;
  portENTER_CRITICAL(&metricsLock);
  recordTime(retained.home[field], at);
  portEXIT_CRITICAL(&metricsLock);
}

void captureMark(uint32_t edge, const char* name, uint32_t at) {
  const int field = fieldIndex(strcmp(name, "forced_wait") == 0 ? "stall_end" : name, storageFields);
  if (field < 0) return;
  portENTER_CRITICAL(&metricsLock);
  if (auto* t = findTake(edge)) recordTime(t->storage[field], at);
  portEXIT_CRITICAL(&metricsLock);
}

void captureResult(uint32_t edge, uint32_t framesIn, uint32_t framesOut,
                   uint32_t crc, bool overflow, bool saved) {
  portENTER_CRITICAL(&metricsLock);
  if (auto* t = findTake(edge)) {
    t->framesIn = framesIn;
    t->framesOut = framesOut;
    t->crc = crc;
    t->overflow = overflow;
    t->saved = saved;
    ++retained.revision;
  }
  portEXIT_CRITICAL(&metricsLock);
}

void releaseEvent(uint32_t edge, const char* phase, uint32_t at) {
  const int field = fieldIndex(phase, releaseFields);
  if (field < 0) return;
  portENTER_CRITICAL(&metricsLock);
  if (auto* t = findTake(edge)) recordTime(t->release[field], at);
  portEXIT_CRITICAL(&metricsLock);
}

void previousSaveStarted(uint32_t edge, uint32_t priorEdge, uint32_t at) {
  portENTER_CRITICAL(&metricsLock);
  if (auto* t = findTake(edge); t && !t->overlap.priorEdge) {
    t->overlap.priorEdge = priorEdge;
    recordTime(t->overlap.pendingAt, at);
  }
  portEXIT_CRITICAL(&metricsLock);
}

void previousSaveFinished(uint32_t edge, uint32_t at, bool success) {
  portENTER_CRITICAL(&metricsLock);
  if (auto* t = findTake(edge); t && t->overlap.priorEdge && !t->overlap.finishedAt) {
    t->overlap.finishOk = success;
    recordTime(t->overlap.finishedAt, at);
  }
  portEXIT_CRITICAL(&metricsLock);
}

void previousSaveVisible(uint32_t edge, bool pending, uint32_t at) {
  portENTER_CRITICAL(&metricsLock);
  if (auto* t = findTake(edge); t && t->overlap.priorEdge) {
    if (pending) recordTime(t->overlap.visibleOn, at);
    else if (t->overlap.visibleOn) recordTime(t->overlap.visibleOff, at);
  }
  portEXIT_CRITICAL(&metricsLock);
}

void captureAudio(uint32_t edge, const microphone::Stats& stats) {
  portENTER_CRITICAL(&metricsLock);
  if (auto* t = findTake(edge)) {
    t->audio = stats;
    ++retained.revision;
  }
  portEXIT_CRITICAL(&metricsLock);
}

#if PALA_STORAGE_PROBE
void captureWavIo(uint32_t edge, WavIoPhase phase, const storage_probe::Sample& sample,
                  const WavOpenParts& parts, uint32_t result, uint32_t error) {
  portENTER_CRITICAL(&metricsLock);
  if (auto* t = findTake(edge)) {
    const bool open = phase == WavIoPhase::Open;
    auto& dest = open ? t->ioOpen : t->ioHeader;
    const bool previousCollision = open &&
        dest.result == static_cast<uint32_t>(WavOpenResult::OpenFailed) && dest.error == EEXIST;
    if (!dest.edge || previousCollision) {
      dest = {edge, result, error, sample};
      if (open) memcpy(t->ioCalls, parts, sizeof(t->ioCalls));
      ++retained.revision;
    }
  }
  portEXIT_CRITICAL(&metricsLock);
}
#endif

void captureWavCollision(uint32_t edge, int note) {
  portENTER_CRITICAL(&metricsLock);
  if (auto* t = findTake(edge)) {
    if (t->wavCollisions++ == 0) t->firstWavCollision = note;
    t->lastWavCollision = note;
    ++retained.revision;
  }
  portEXIT_CRITICAL(&metricsLock);
}

static void markArchived(TakeMetrics& t, int note, uint32_t at) {
  if (note <= 0 || t.note) return;
  t.note = note;
  recordTime(t.release[fieldIndex("committed", releaseFields)], at);
  ++retained.revision;
}

void captureArchived(uint32_t edge, int note) {
  const auto at = millis();
  portENTER_CRITICAL(&metricsLock);
  if (auto* t = findTake(edge)) markArchived(*t, note, at);
  portEXIT_CRITICAL(&metricsLock);
#if PALA_STAGE3_TRIAL
  Serial.printf("[S3] archive edge=%lu note=%d\n", (unsigned long)edge, note);
#endif
}

void bindInboxFile(uint32_t edge, const char* basename) {
  const auto length = strlen(basename);
  if (!length || length >= sizeof(TakeMetrics::inboxFile)) return;
  portENTER_CRITICAL(&metricsLock);
  if (auto* t = findTake(edge)) memcpy(t->inboxFile, basename, length + 1);
  portEXIT_CRITICAL(&metricsLock);
}

void captureInboxArchived(const char* name, int note) {
  const auto at = millis();
  // Adoption can prefix the original spool basename with the numbered ID.
  const char* suffix = strrchr(name, '-');
  const char* basename = suffix ? suffix + 1 : name;
#if PALA_STAGE3_TRIAL
  uint32_t archivedEdge = 0;
#endif
  portENTER_CRITICAL(&metricsLock);
  for (auto& t : retained.takes) {
    if (t.inboxFile[0] && strcmp(t.inboxFile, basename) == 0) {
      markArchived(t, note, at);
#if PALA_STAGE3_TRIAL
      archivedEdge = t.edge;
#endif
      t.inboxFile[0] = '\0';
      break;
    }
  }
  portEXIT_CRITICAL(&metricsLock);
#if PALA_STAGE3_TRIAL
  if (archivedEdge) Serial.printf("[S3] archive edge=%lu note=%d\n", (unsigned long)archivedEdge, note);
#endif
}

#if PALA_VFS_OWNER_TRACE
static uint32_t packedBytes(const uint8_t* bytes) {
  return (uint32_t(bytes[0]) << 24) | (uint32_t(bytes[1]) << 16) |
         (uint32_t(bytes[2]) << 8) | uint32_t(bytes[3]);
}

static bool appendOwnerPage(String& reply, const String& page, const TakeMetrics& t) {
  const bool open = page.startsWith("own_open");
  const char* base = open ? "own_open" : "own_header";
  if (!page.startsWith(base)) return false;
  const auto& p = open ? t.ioOpen : t.ioHeader;
  const auto& evidence = p.sample.owners;
  if (page == base) {
    const uint32_t counts[] = {p.edge, evidence.waits, evidence.omitted, evidence.contextDrops};
    appendTimes(reply, counts);
    return true;
  }
  const size_t length = strlen(base);
  if (page.length() != length + 1 || (page[length] != '0' && page[length] != '1')) return false;
  const auto& w = evidence.longest[page[length] - '0'];
  const auto* name = reinterpret_cast<const uint8_t*>(w.task);
  static_assert(sizeof(w.task) == 16);
  const uint32_t words[] = {p.edge, w.mutex, w.holder, w.waiterPc, w.fatPc, w.work,
                            w.fatOp, w.opcode, w.waitUs, w.heldUs, w.hostUs, w.eventUs, w.flags, w.delayUs,
                            packedBytes(name), packedBytes(name + 4), packedBytes(name + 8),
                            packedBytes(name + 12)};
  appendTimes(reply, words);
  return true;
}
#endif

String metricsPage(const String& requested) {
  static const uint32_t bootId = esp_random();
  String page = requested;
  size_t slot = pinnedTakes + 1;
#if PALA_VFS_OWNER_TRACE
  if (page.startsWith("a_") || page.startsWith("b_")) {
    slot = page[0] == 'a' ? 0 : 1;
    page.remove(0, 2);
  }
#endif
  // Keep the command-task stack independent of the number of retained takes.
  TakeMetrics t;
  ScreenTimes home;
  uint32_t revision, bleLast;
  BleBlockTimes bleBlock{};
  const bool bleSlotPage = requested.startsWith("ble_") && requested != "ble_blocks";
  const int bleSlot = bleSlotPage ? requested.substring(4).toInt() : -1;
  const bool validBleSlot = bleSlot >= 0 && static_cast<size_t>(bleSlot) < bleBlockCapacity &&
                            requested == String("ble_") + bleSlot;
#if PALA_VFS_OWNER_TRACE
  uint32_t history[4];
#endif
  portENTER_CRITICAL(&metricsLock);
  revision = retained.revision;
  bleLast = bleBlockSequence;
  if (validBleSlot) bleBlock = bleBlocks[bleSlot];
  memcpy(home, retained.home, sizeof(home));
  t = retained.takes[slot <= pinnedTakes ? slot : retained.latest];
#if PALA_VFS_OWNER_TRACE
  history[0] = retained.seen;
  history[1] = retained.takes[0].edge;
  history[2] = retained.takes[1].edge;
  history[3] = configuredStallMs;
#endif
  portEXIT_CRITICAL(&metricsLock);
  String reply = "BENCH TRACE1 ";
  reply += String(bootId) + " " + String(revision) + " " + requested;
#if PALA_WIFI_TRIAL
  if (requested == "wifi") return reply + wifi_trial::values();
  if (requested == "wifi_socket") {
    const auto& t = wifi_trial::trace();
    const uint32_t values[] = {t.socketIp, t.socketPort, t.tcpReached};
    appendTimes(reply, values);
    return reply;
  }
#endif
  if (requested == "ble_blocks") {
    const uint32_t values[] = {bleLast, bleBlockCapacity};
    appendTimes(reply, values);
    return reply;
  }
  if (bleSlotPage) {
    if (!validBleSlot) return "ERROR COMMAND";
    const uint32_t values[] = {bleBlock.sequence, bleBlock.offset, bleBlock.bytes, bleBlock.requested,
                               bleBlock.firstQueued, bleBlock.lastQueued, bleBlock.complete};
    appendTimes(reply, values);
    return reply;
  }
#if PALA_VFS_OWNER_TRACE
  if (requested == "history") {
    appendTimes(reply, history);
    return reply;
  }
  if (requested == "memory") {
    using Ring = pcm_capture::RecorderBuffer;
    constexpr uint32_t blocks = sizeof(Ring::Storage) / sizeof(Ring::Block);
    const uint32_t memory[] = {allocatedRecordingBuffers(), pcm_capture::recorderFrames, blocks,
                               blocks * sizeof(Ring::Block::samples), sizeof(Ring::Storage),
                               ESP.getPsramSize(), ESP.getFreePsram()};
    appendTimes(reply, memory);
    return reply;
  }
  if (requested == "image") {
    const uint8_t* hash = esp_app_get_description()->app_elf_sha256;
    uint32_t words[8];
    for (size_t i = 0; i < 8; ++i) words[i] = packedBytes(hash + 4 * i);
    appendTimes(reply, words);
    return reply;
  }
  if (page.startsWith("own_")) return appendOwnerPage(reply, page, t) ? reply : "ERROR COMMAND";
#endif
  if (page == "summary") {
    const uint32_t summary[] = {
        esp_reset_reason() == ESP_RST_SW ? 1U : 0U, configuredStallMs,
        t.edge, t.accepted, t.pcm, t.inbox, static_cast<uint32_t>(t.note),
        t.framesIn, t.framesOut, t.crc, t.overflow, t.saved
    };
    appendTimes(reply, summary);
  } else if (page == "wav_collision") {
    const uint32_t values[] = {t.edge, t.wavCollisions, t.firstWavCollision, t.lastWavCollision};
    appendTimes(reply, values);
  } else if (page == "audio") {
    const auto& s = t.audio;
    const uint32_t audio[] = {s.observed, s.dmaOverruns, s.dmaLostBytes, s.reconfigReads,
                              s.readErrors, s.maxReadGapUs, s.ringHighBlocks};
    appendTimes(reply, audio);
#if PALA_STORAGE_PROBE
  } else if (page == "io_open" || page == "io_header") {
    const auto& p = page == "io_open" ? t.ioOpen : t.ioHeader;
    const auto& s = p.sample;
    const uint32_t values[] = {p.edge, s.totalUs, s.fatLockUs, s.vfsLockUs, s.hostLockUs,
                               s.commandUs, s.eventUs, s.otherUs, s.lockCalls, s.lockBlocks,
                               s.commands, s.events, s.valid};
    appendTimes(reply, values);
  } else if (page == "io_calls") {
    const uint32_t values[] = {t.ioOpen.edge ? t.ioOpen.edge : t.ioHeader.edge,
                               t.ioCalls[0], t.ioCalls[1], t.ioCalls[2], t.ioCalls[3], t.ioCalls[4],
                               t.ioOpen.result, t.ioOpen.error, t.ioHeader.result, t.ioHeader.error};
    appendTimes(reply, values);
#endif
  } else if (page == "overlap") {
    const auto& p = t.overlap;
    const uint32_t values[] = {t.edge, p.priorEdge, p.pendingAt, p.finishedAt, p.finishOk,
                               p.visibleOn, p.visibleOff};
    appendTimes(reply, values);
  } else if (page == "release") {
    appendTimes(reply, t.release);
  } else if (page == "home") {
    appendTimes(reply, home);
  } else if (page == "rec") {
    appendTimes(reply, t.screen);
  } else if (page == "storage") {
    appendTimes(reply, t.storage);
  } else {
    return "ERROR COMMAND";
  }
  return reply;
}

bool configure(const String& name, int note) {
#if PALA_WIFI_TRIAL
  if (name == "wifi-retry") return wifi_trial::arm(note);
  if (name == "wifi-probe" && !note) return wifi_trial::queueLinkProbe();
#endif
  if (name == "clock-fail-next-boot" && !note) {
    skipClockOnRestart = skipMagic;
    return true;
  }
  if (name == "clock-unknown" || name == "clock-normal") {
    if (note) return false;
    if (name == "clock-normal") skipClockOnRestart = 0;
    invalid.store(name == "clock-unknown");
    return true;
  }
  for (const auto& entry : points) {
    if (name != entry.name) continue;
    portENTER_CRITICAL(&guard);
    const bool ok = armed.arm(entry.point, note);
    portEXIT_CRITICAL(&guard);
    return ok;
  }
  return false;
}

bool unknownClock() { return invalid.load(); }

bool bootRtcAvailable() {
  const bool skip = skipClockOnRestart == skipMagic && esp_reset_reason() == ESP_RST_SW;
  skipClockOnRestart = 0;
  return !skip;
}

void transport(const char* link, const char* phase, int note) {
  Serial.printf("[BenchLink] link=%s phase=%s note=%d\n", link, phase, note);
}

bool loseReceipt(int note) {
  if (!consume(Point::LoseReceipt, note)) return false;
  Serial.printf("[BenchCut] phase=lose-receipt note=%d\n", note);
  return true;
}

void cutWindow(Point point, int note, uint32_t edge) {
  if (!consume(point, note)) return;
  for (const auto& entry : points) {
    if (entry.point != point) continue;
    Serial.printf("[BenchCut] phase=%s note=%d edge=%lu window_ms=10000\n", entry.name, note, (unsigned long)edge);
    Serial.flush();
    delay(10000);
    Serial.printf("[BenchCut] phase=%s expired\n", entry.name);
    break;
  }
}
}
#endif
