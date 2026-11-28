#include "Arduino.h"
#include "owner_trace.h"
#include "../../config.h"
#include "../../globals.h"
#include "../../types.h"
#include "monthly_recordings.h"
#include "record.h"
#include "display_pipeline.h"
#include "rtc.h"
#include "stage2_bench.h"
#include "wav_io_timing.h"
#include "recorder_store.h"
#include "SD_MMC.h"
#include "esp_heap_caps.h"
#include "esp_system.h"
#include "esp_rom_crc.h"
#include "notes.h"
#include "settings.h"
#include "ui.h"
#include "pcm_resampler.h"
#include "wav_payload.h"
#include "../../sounds.h"
#include <atomic>
#include <algorithm>
#include <cerrno>
#include <dirent.h>
#include <fcntl.h>
#include <unistd.h>
#include <sys/stat.h>
#include <ctime>
#include <new>
#include "pcm_capture_buffer.h"
#include "recording_parts.h"
#include "recording_lock.h"
#include "../audio/microphone_health.h"
#include "record_priority.h"
#include "capture_slots.h"
#include "rec_spool.h"
#include "capture_wav_recovery.h"

extern "C" {
#include "../../src/audio/audio_bsp.h"
}

#if PALA_WAV_TIMING
namespace {
// Only setup writes. Publish each immutable entry after its fields are filled.
struct BootMark { const char* phase; uint32_t time; };
BootMark bootMarks[24];
std::atomic<unsigned> bootCount{0};
}
void markWavBoot(const char* phase) {
  const auto index = bootCount.load(std::memory_order_relaxed);
  if (index < sizeof(bootMarks) / sizeof(bootMarks[0])) {
    bootMarks[index] = {phase, millis()};
    bootCount.store(index + 1, std::memory_order_release);
  }
}
#endif

namespace {
constexpr size_t pcmFrames = pcm_capture::recorderFrames;
using CaptureBuffer = pcm_capture::RecorderBuffer;
// Cross-core controls stay in internal RAM. Only the two PCM rings use PSRAM.
enum class CaptureResult { Running, Complete, Cancelled, Overflow, CodecError };
struct CaptureSlot {
  alignas(CaptureBuffer) uint8_t control[sizeof(CaptureBuffer)];
  CaptureBuffer::Storage* storage = nullptr;
  CaptureBuffer* buffer = nullptr;
  std::atomic<bool> stop{false};
  std::atomic<CaptureResult> captureResult{CaptureResult::Complete};
  int holdPin = -1;
  bool markers = false, markerReady = false;
  uint32_t edge = 0, epoch = 0;
  microphone::Stats stats{};
#if PALA_TEST_FIRST_WRITE_DELAY_MS || PALA_STAGE2_BENCH
  uint32_t crc = 0;
#endif
};
CaptureSlot captures[capture_slots::Registry::count];
capture_slots::Registry takeRegistry;
TaskHandle_t captureTask = nullptr;
SemaphoreHandle_t registryMutex = nullptr;
int nextMicrophone = -1;
uint32_t ringCount = 0;
std::atomic<bool> spoolAvailable{false};
std::atomic<uint32_t> savedEdge{0};
// The foreground/gate has a single SD writer, even while another take acquires PCM.
int16_t writeBatch[REC_BUF / (2 * sizeof(int16_t))];

class RegistryLock {
 public:
  RegistryLock() { xSemaphoreTake(registryMutex, portMAX_DELAY); }
  ~RegistryLock() { xSemaphoreGive(registryMutex); }
  RegistryLock(const RegistryLock&) = delete;
};

// Called with registryMutex. These callbacks only publish RAM state and wake
// workers. No disk, panel refresh or audio operation runs under the mutex.
void startSlot(int i, int holdPin, bool markers, uint32_t edge, bool inbox) {
  auto& slot = captures[i];
  slot.buffer->reset();
  slot.holdPin = holdPin;
  slot.markers = markers;
  slot.markerReady = digitalRead(BTN_PWR) == HIGH;
  slot.edge = edge;
  slot.epoch = markers ? captureEpochAtRec(edge) : 0;
  slot.stop.store(false);
  if (markers) stage2::captureStart(edge, inbox, millis());
  slot.captureResult.store(CaptureResult::Running, std::memory_order_release);
  nextMicrophone = i;
  xTaskNotifyGive(captureTask);
}

void admitFollowup(uint32_t edge) {
  const auto admission = takeRegistry.overlap(edge);
  if (admission.state != capture_slots::Admission::Accepted) return;
  startSlot(admission.slot, BTN_REC, true, edge, spoolAvailable.load());
  if (admission.prior >= 0) {
    stage2::previousSaveStarted(edge, takeRegistry.edge(admission.prior), millis());
    note_display::setPriorSavePending(edge, true);
  }
}

int nextMicrophoneCapture() {
  for (;;) {
    bool watch;
    {
      RegistryLock lock;
      watch = takeRegistry.watch();
      uint32_t edge;
      if (watch && physicalRecordingHold(edge)) admitFollowup(edge);
      if (nextMicrophone >= 0) {
        const int slot = nextMicrophone;
        nextMicrophone = -1;
        return slot;
      }
    }
    ulTaskNotifyTake(pdTRUE, watch ? std::max<TickType_t>(1, pdMS_TO_TICKS(5)) : portMAX_DELAY);
  }
}

void microphoneProducer(void*) {
  int16_t stereo[pcmFrames * 2];
  constexpr size_t minBlocks = (SAMPLE_RATE / 2 + pcmFrames - 1) / pcmFrames;
  for (;;) {
    const int index = nextMicrophoneCapture();
    auto& slot = captures[index];
    const auto holdPin = slot.holdPin;
    const bool markers = slot.markers;
    const auto edge = slot.edge;
    size_t blocks = 0;
    RecordingLock latch;
    bool locked = false, offered = false;
    uint32_t shownMinute = 0;
#if PALA_STAGE3_TRIAL
    uint32_t lockedAt = 0, releasedAt = 0;
    unsigned stopButton = 0;
#endif
#if PALA_TEST_FIRST_WRITE_DELAY_MS || PALA_STAGE2_BENCH
    uint32_t crc = 0;
#endif
    CaptureResult result = CaptureResult::Complete;
    microphone::CaptureHealth health(audio_rx_dma_snapshot());
    for (;;) {
      if (slot.stop.load()) {
        result = CaptureResult::Cancelled;
        break;
      }
      const bool upper = digitalRead(holdPin) == LOW;
      const bool lower = markers && digitalRead(BTN_PWR) == LOW;
      const uint32_t elapsed = uint32_t(uint64_t(blocks) * pcmFrames * 1000 / SAMPLE_RATE);
      const auto input = latch.update(elapsed,upper,lower);
      const bool handsFree = PALA_STAGE3 && markers;
#if PALA_STAGE3_TRIAL
      if (handsFree && input.locked) {
        if (!lockedAt) lockedAt = millis();
        if (!releasedAt && !upper && !lower) releasedAt = millis();
        if (input.stop) stopButton = (upper ? 1U : 0U) | (lower ? 2U : 0U);
      }
#endif
      if (handsFree && (offered!=input.offer || locked!=input.locked || elapsed/60000!=shownMinute)) {
        offered=input.offer;
        locked=input.locked;
        shownMinute=elapsed/60000;
        note_display::recordingStatus(edge,elapsed,offered,locked);
      }
      if ((blocks >= minBlocks && (handsFree ? input.stop : !upper)) ||
          (markers && !locked && recordingPressTime() != edge)) {
        if (locked && input.stop) consumeRecordingStop(edge);
#if PALA_STAGE2_BENCH
        if (markers) {
          const auto stoppedAt = millis();
          stage2::releaseEvent(edge, "button", recordingButtonRelease(edge));
          stage2::releaseEvent(edge, "mic_stop", stoppedAt);
        }
#endif
        break;
      }
      const int status = audio_playback_read(stereo, sizeof(stereo));
      const auto action = health.read(status, micros());
      if (action == microphone::ReadAction::Retry) {
        vTaskDelay(pdMS_TO_TICKS(10));
        continue;
      }
      if (action == microphone::ReadAction::Stop) {
        result = CaptureResult::CodecError;
        break;
      }
      const auto sampledAt = millis();
      if (!slot.buffer->pushStereo(stereo, sampledAt, markers && lower && (!handsFree || elapsed < 5000))) {
        result = CaptureResult::Overflow;
        break;
      }
      if (!blocks) {
        if (markers) stage2::firstPcm(edge, sampledAt);
        note_display::firstPcm(edge, !markers);
      }
#if PALA_TEST_FIRST_WRITE_DELAY_MS || PALA_STAGE2_BENCH
      // pushStereo has copied this block. Hash the original left channel
      // independently of the ring and SD writer for later WAV readback.
      for (size_t i = 0; i < pcmFrames; ++i) stereo[i] = stereo[2 * i];
      crc = esp_rom_crc32_le(crc, reinterpret_cast<const uint8_t*>(stereo), pcmFrames * sizeof(int16_t));
#endif
      ++blocks;
    }
#if PALA_TEST_FIRST_WRITE_DELAY_MS || PALA_STAGE2_BENCH
    slot.crc = crc;
#endif
    slot.stats = health.finish(audio_rx_dma_snapshot(), slot.buffer->highWaterBlocks());
    if (markers) stage2::captureAudio(edge, slot.stats);
    note_display::endCapture(edge, result == CaptureResult::Complete);
    audio_idle_suspend();
#if PALA_STAGE3_TRIAL
    if (markers)
      Serial.printf("[S3] microphone edge=%lu intact=%u\n", (unsigned long)edge, unsigned(slot.stats.intact()));
    if (markers && lockedAt)
      Serial.printf("[S3] lock edge=%lu locked=%lu released=%lu stop=%u\n", (unsigned long)edge,
                    (unsigned long)lockedAt, (unsigned long)releasedAt, stopButton);
#endif
    RegistryLock lock;
    takeRegistry.micStopped(index);
    slot.captureResult.store(result, std::memory_order_release);
  }
}

class MicrophoneSession {
 public:
  MicrophoneSession(int holdPin, bool markers, uint32_t edge, bool inbox) {
    if (!captureTask) return;
    RegistryLock lock;
    bool fresh;
    index_ = takeRegistry.attach(edge, markers, fresh);
    if (index_ >= 0 && fresh) startSlot(index_, holdPin, markers, edge, inbox);
  }

  ~MicrophoneSession() {
    if (index_ < 0) return;
    stop();
    while (result() == CaptureResult::Running) vTaskDelay(1);
#if PALA_STAGE2_BENCH
    if (!reported_) reportCompletion(0, false); // failed before the first PCM write
#endif
    auto& stats = captures[index_].stats;
    if (!stats.intact()) {
      Serial.printf("[Rec] microphone observed=%u dma_overruns=%lu dma_bytes=%lu reconfig_reads=%lu read_errors=%lu\n",
          stats.observed, (unsigned long)stats.dmaOverruns, (unsigned long)stats.dmaLostBytes,
          (unsigned long)stats.reconfigReads, (unsigned long)stats.readErrors);
    }
    RegistryLock lock;
    takeRegistry.drained(index_);
  }

  MicrophoneSession(const MicrophoneSession&) = delete;
  MicrophoneSession& operator=(const MicrophoneSession&) = delete;

  bool read(CaptureBuffer::Block& block) {
    if (index_ < 0) return false;
    for (;;) {
      const bool stopped = result() != CaptureResult::Running;
      if (captures[index_].buffer->pop(block)) return true;
      if (stopped) return false;
      vTaskDelay(1);
    }
  }

  void stop() { captures[index_].stop.store(true); }
  CaptureResult result() const { return captures[index_].captureResult.load(std::memory_order_acquire); }
#if PALA_STAGE2_BENCH
  void reportCompletion(uint32_t writtenFrames, bool saved) {
    const auto& slot = captures[index_];
    if (slot.markers) stage2::captureResult(slot.edge, producedBlocks() * pcmFrames, writtenFrames,
        slot.crc, result() == CaptureResult::Overflow, saved);
    reported_ = true;
  }
#endif
  bool markerReady() const { return captures[index_].markerReady; }
  uint32_t producedBlocks() const { return captures[index_].buffer->producedBlocks(); }
#if PALA_TEST_FIRST_WRITE_DELAY_MS || PALA_STAGE2_BENCH
  uint32_t microphoneCrc() const { return captures[index_].crc; }
#endif

 private:
  int index_ = -1;
#if PALA_STAGE2_BENCH
  bool reported_ = false;
#endif
};

// Saturating estimate. Expensive FAT free-cluster queries run on the main
// loop after Home appears. The sample loop only compares an in-memory budget.
std::atomic<uint32_t> availableBytes{UINT32_MAX};
std::atomic<uint32_t> totalMiB{0}, usedMiB{0};
}  // namespace

uint32_t lastCaptureEdge() { return savedEdge.load(); }

uint32_t allocatedRecordingBuffers() { return ringCount; }

bool initializeRecordingAudio() {
  if (captureTask) return true;
  registryMutex = xSemaphoreCreateMutex();
  if (!registryMutex) return false;
  unsigned allocated = 0;
  for (auto& slot : captures) {
    void* storage = heap_caps_malloc(sizeof(CaptureBuffer::Storage), MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT);
    if (!storage) break;
    auto* blocks = new (storage) CaptureBuffer::Storage;
    slot.storage = blocks;
    slot.buffer = new (slot.control) CaptureBuffer(*blocks);
    ++allocated;
  }
  takeRegistry.capacity(allocated);
  if (allocated == 1) Serial.println("[Rec] one PCM ring available; concurrent REC queue limited");
  if (allocated &&
      xTaskCreatePinnedToCore(microphoneProducer, "pcm-capture", 4096, nullptr, 4, &captureTask, 1) == pdPASS) {
    ringCount = allocated;
    return true;
  }
  for (auto& slot : captures) {
    if (!slot.buffer) continue;
    auto* storage = slot.storage;
    slot.buffer->~CaptureBuffer();
    heap_caps_free(storage);
    slot.buffer = nullptr;
  }
  vSemaphoreDelete(registryMutex);
  registryMutex = nullptr;
  return false;
}

// Capture timestamps in RAM. Report after the microphone session is closed.
class WavStartTiming {
 public:
  stage2::WavIoTiming io;
#if PALA_WAV_TIMING
  WavStartTiming(uint32_t edge, bool recording, const MicrophoneSession& session)
      : io(edge, recording), edge_(edge), session_(session) {}
  void at(const char* name, uint32_t time) {
    stage2::captureMark(edge_, name, time);
    if (count_ < sizeof(marks_) / sizeof(marks_[0])) marks_[count_++] = {name, time};
  }
  void mark(const char* name) { at(name, millis()); }
#if PALA_TEST_FIRST_WRITE_DELAY_MS
  void stallFirstWrite() {
    // Only the inbox writer calls this method, after acquiring the microphone.
    static bool used = false;
    if (used || esp_reset_reason() != ESP_RST_SW) return;
    const auto count = bootCount.load(std::memory_order_acquire);
    for (unsigned i = 0; i < count; ++i) {
      if (strcmp(bootMarks[i].phase, "setup_done") == 0 && edge_ > bootMarks[i].time &&
          !note_display::inBootHome(edge_)) return;
    }
    used = true;
    const uint32_t before = session_.producedBlocks();
    const uint32_t start = millis();
    at("stall_begin", start);
    do {
      delay(PALA_TEST_FIRST_WRITE_DELAY_MS - stallMs_);
      stallMs_ = millis() - start;
    } while (stallMs_ < PALA_TEST_FIRST_WRITE_DELAY_MS);
    at("forced_wait", start + stallMs_);
    blocksDuringStall_ = session_.producedBlocks() - before;
  }

  void reportStall(uint32_t captured, uint32_t written, bool overflow, bool saved) const {
    if (!stallMs_) return;
    // Previous USB traces lost parts of long lines in the final trace burst.
    // Send short matching records after closing the WAV, before that burst.
    for (unsigned copy = 0; copy < 2; ++copy) {
      Serial.printf("[RecStall] e=%lu ms=%lu blocks=%lu\n", (unsigned long)edge_,
                    (unsigned long)stallMs_, (unsigned long)blocksDuringStall_);
      Serial.flush();
      Serial.printf("[RecCheck] e=%lu in=%lu out=%lu ov=%u ok=%u\n", (unsigned long)edge_,
                    (unsigned long)captured, (unsigned long)written, overflow ? 1U : 0U, saved ? 1U : 0U);
      Serial.flush();
      Serial.printf("[RecCRC] e=%lu crc=%08lx\n", (unsigned long)edge_, (unsigned long)session_.microphoneCrc());
      Serial.flush();
    }
  }
#endif
  ~WavStartTiming() {
    uint32_t prev = edge_;
    for (unsigned i = 0; i < count_; ++i) {
      const auto& m = marks_[i];
      Serial.printf("[RecTiming] edge=%lu stage=%s ms=%lu elapsed=%lu\n",
                    (unsigned long)edge_, m.name, (unsigned long)(m.time - prev),
                    (unsigned long)(m.time - edge_));
      prev = m.time;
    }
    Serial.printf("[RecBoot] edge=%lu software_reset=%u\n", (unsigned long)edge_,
                  esp_reset_reason() == ESP_RST_SW ? 1U : 0U);
    const auto bootEntries = bootCount.load(std::memory_order_acquire);
    for (unsigned i = 0; i < bootEntries; ++i) {
      const auto& m = bootMarks[i];
      Serial.printf("[RecBoot] edge=%lu stage=%s boot_ms=%lu\n", (unsigned long)edge_,
                    m.phase, (unsigned long)m.time);
    }
    Serial.printf("[RecTiming] edge=%lu stages=%u end\n", (unsigned long)edge_, count_);
  }

 private:
  struct Mark { const char* name; uint32_t time; };
  Mark marks_[24];
  unsigned count_ = 0;
  uint32_t edge_;
  const MicrophoneSession& session_;
#if PALA_TEST_FIRST_WRITE_DELAY_MS
  uint32_t stallMs_ = 0;
  uint32_t blocksDuringStall_ = 0;
#endif
#else
  WavStartTiming(uint32_t edge, bool recording, const MicrophoneSession&) : io(edge, recording) {}
  void at(const char*, uint32_t) {}
  void mark(const char*) {}
#endif
};

static FILE* openExclusiveWav(const String& path, stage2::WavIoTiming& io, int& openError) {
  openError = 0;
  using stage2::WavOpenCall;
  using stage2::WavOpenResult;
  io.begin();
  const String mounted = String("/sdcard") + path;
  io.after(WavOpenCall::Path);
  const int fd = ::open(mounted.c_str(), O_WRONLY | O_CREAT | O_EXCL, 0666);
  if (fd < 0) openError = errno;
  io.after(WavOpenCall::PosixOpen);
  if (fd < 0) {
    io.finishOpen(WavOpenResult::OpenFailed, openError);
    return nullptr;
  }
  auto* file = fdopen(fd, "wb");
  io.after(WavOpenCall::Fdopen);
  if (!file) {
    const int streamError = errno;
    ::close(fd);
    io.after(WavOpenCall::Close);
    io.finishOpen(WavOpenResult::FdopenFailed, streamError);
    errno = streamError;
    return nullptr;
  }
  const int buffering = setvbuf(file, nullptr, _IONBF, 0);
  io.after(WavOpenCall::Setvbuf);
  io.finishOpen(buffering == 0 ? WavOpenResult::Opened : WavOpenResult::SetvbufFailed);
  return file;
}

// Exclusive creation leaves the stream at byte zero. Checkpoints position it explicitly.
static bool writeWavHeader(FILE* file, uint32_t dataBytes, WavStartTiming* timing = nullptr) {
  uint8_t header[44] = {
    'R', 'I', 'F', 'F', 0, 0, 0, 0, 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ',
    16, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    2, 0, 16, 0, 'd', 'a', 't', 'a', 0, 0, 0, 0,
  };
  capture_wav::putSize(header + 4, dataBytes + 36);
  capture_wav::putSize(header + 24, SAMPLE_RATE);
  capture_wav::putSize(header + 28, SAMPLE_RATE * 2);
  capture_wav::putSize(header + 40, dataBytes);
  if (timing) timing->io.begin();
  const bool written = std::fwrite(header, 1, sizeof(header), file) == sizeof(header);
  if (timing) {
    timing->io.finishHeader(written);
    timing->mark("header_write");
  }
  if (!written) return false;
  const bool flushed = std::fflush(file) == 0;
  if (timing) timing->mark("flush");
  if (!flushed) return false;
  const bool synced = ::fsync(fileno(file)) == 0;
  if (timing) timing->mark("fsync");
  return synced;
}

static bool checkpointWavHeader(FILE* file, uint32_t dataBytes) {
  const long resumeAt = std::ftell(file);
  if (resumeAt < 0 || std::fseek(file, 0, SEEK_SET) != 0) return false;
  if (!writeWavHeader(file, dataBytes)) return false;
  return resumeAt <= 44 || std::fseek(file, resumeAt, SEEK_SET) == 0;
}

template<class PreparePath>
static bool recordHeldToPath(int holdPin, bool captureMoments, uint32_t edgeMs,
                             PreparePath preparePath, std::vector<uint32_t>* stagedMoments = nullptr,
                             const String* recorderUtc = nullptr) {
  MicrophoneSession session(holdPin, captureMoments, edgeMs, stagedMoments != nullptr);
  WavStartTiming timing(edgeMs, captureMoments, session);
  CaptureBuffer::Block firstBlock;
  if (!session.read(firstBlock)) {
    timing.mark("no_pcm");
    Serial.println("[Rec] microphone did not provide PCM");
    return false;
  }
  bool markReady = session.markerReady();
  const uint32_t firstPcm = firstBlock.sampledAt;
#if PALA_STAGE3_TRIAL
  if (captureMoments)
    Serial.printf("[S3] pcm edge=%lu at=%lu now=%lu\n", (unsigned long)edgeMs,
                  (unsigned long)firstPcm, (unsigned long)millis());
#endif
  timing.at("pcm", firstPcm);
  timing.mark("dequeue");
  int noteNum = -1;
  FILE* f = nullptr;
  String firstPath;
  while (!f) {
    const String path = preparePath(noteNum, timing);
    timing.mark("path_end");
    if (path.isEmpty()) return false;
    if (captureMoments) Serial.printf("[Rec] edge=%lu first_pcm=%lu latency_ms=%lu\n",
        (unsigned long)edgeMs, (unsigned long)firstPcm,
        (unsigned long)rec_priority::latency(edgeMs, firstPcm));
    Serial.printf("[Rec] %s\n", path.c_str());

    // A collision keeps the first PCM block and the same running capture.
    // The exclusive open stays the authority on whether a name is free.
    timing.mark("serial");
    int openError = 0;
    f = openExclusiveWav(path, timing.io, openError);
    if (f) { firstPath = path; break; }
#if PALA_RECORDER_FIRST
    if (recorderUtc && openError == EEXIST) {
      stage2::captureWavCollision(edgeMs, noteNum);
      Serial.printf("[Rec] WAV collision id=%d\n", noteNum);
      if (session.result() != CaptureResult::Overflow) continue;
    }
#endif
    Serial.printf("[Rec] cannot create WAV: %d\n", openError ? openError : errno);
    return false;
  }
  timing.mark("open");
  // Reserve work only after success, and before any recoverable WAV data.
#if PALA_RECORDER_FIRST
  if (captureMoments) stage2::cutWindow(stage2::Point::Open, noteNum, edgeMs);
  if (recorderUtc) {
    const bool reserved = reserveRecorderWork(noteNum, *recorderUtc);
    timing.mark("reserve");
    if (!reserved) {
      std::fclose(f);
      return false;
    }
  }
#else
  (void)recorderUtc;
#endif
  if (!writeWavHeader(f, 0, &timing)) {
    std::fclose(f);
    return false;
  }

  const bool split = PALA_STAGE3 && captureMoments;
  std::string base(firstPath.c_str());
  if (firstPath.endsWith(".partial"))
    base.replace(base.size()-8,8,".wav");
  const auto mounted = std::string("/sdcard")+base;
  auto openPart = [&](unsigned index) -> FILE* {
    int error = 0;
    auto* next=openExclusiveWav(recording_parts::path(base,index).c_str(),timing.io,error);
    if (next && !writeWavHeader(next,0)) { std::fclose(next); return nullptr; }
    return next;
  };
  recording_parts::Writer writer(f,mounted,split,openPart,checkpointWavHeader,esp_rom_crc32_le);
  const auto& totalMono=writer.total;
  auto append = [&](const int16_t* samples,size_t bytes) { return writer.write(samples,bytes); };

  constexpr size_t blockBytes = sizeof(firstBlock.samples);
  static_assert(sizeof(writeBatch) % blockBytes == 0);
#if PALA_TEST_FIRST_WRITE_DELAY_MS
  if (stagedMoments) timing.stallFirstWrite();
#endif
  bool audioOk = append(firstBlock.samples, blockBytes);
  const uint32_t firstWrite = millis();
#if PALA_WAV_TIMING
  const uint32_t queuedAtFirstWrite = session.producedBlocks() - 1;
#endif
  timing.at(audioOk ? "first_write" : "first_write_error", firstWrite);
  if (audioOk && captureMoments)
    Serial.printf("[Rec] first_write=%lu write_latency_ms=%lu\n", (unsigned long)firstWrite,
                  (unsigned long)rec_priority::latency(edgeMs, firstWrite));

  std::vector<uint32_t> momentsMs;
  auto mark = [&](bool down, uint32_t endBytes) {
    if (!down) {
      markReady = true;
      return;
    }
    if (!markReady) return;
    markReady = false;
    const uint32_t markerMs = (endBytes / sizeof(int16_t) * 1000ULL) / SAMPLE_RATE;
    if (!momentsMs.empty() && markerMs - momentsMs.back() < 250) return;
    momentsMs.push_back(markerMs);
    note_display::progress(edgeMs, markerMs, (int)momentsMs.size());
  };
  if (audioOk && captureMoments) mark(firstBlock.marker, totalMono);

  uint32_t lastCheckpoint = firstWrite;
  bool spaceLimited = false;
  size_t batched = 0;
  CaptureBuffer::Block block;
  while (audioOk && session.read(block)) {
    memcpy(reinterpret_cast<uint8_t*>(writeBatch) + batched, block.samples, blockBytes);
    batched += blockBytes;
    if (captureMoments) mark(block.marker, totalMono + batched);
    if (batched < sizeof(writeBatch)) continue;
    audioOk = append(writeBatch, batched);
    batched = 0;
    if (!audioOk) break;

    if (captureMoments && totalMono == blockBytes + sizeof(writeBatch))
      stage2::cutWindow(stage2::Point::Pcm, noteNum, edgeMs);
    const uint32_t now = millis();
    if (now - lastCheckpoint < 5000UL) continue;
    if (!checkpointWavHeader(f, writer.partSize)) {
      audioOk = false;
      Serial.println("[Rec] WAV checkpoint failed; partial recording retained");
      break;
    }
    lastCheckpoint = now;
    const auto freeAtStart = availableBytes.load();
    if (!spaceLimited && (totalMono >= freeAtStart || freeAtStart - totalMono < 2UL * 1024UL * 1024UL)) {
      Serial.println("[Rec] stopping: SD card is nearly full");
      spaceLimited = true;
      session.stop();
    }
  }
  if (audioOk && batched) audioOk = append(writeBatch, batched);
  if (!audioOk) {
    session.stop();
    while (session.result() == CaptureResult::Running) vTaskDelay(1);
  }
  const auto result = session.result();
  if (result == CaptureResult::Overflow)
    Serial.println("[Rec] PCM buffer overflow; partial recording retained");
  if (result == CaptureResult::CodecError)
    Serial.println("[Rec] microphone read failed; partial recording retained");
  audioOk &= result == CaptureResult::Complete || (spaceLimited && result == CaptureResult::Cancelled);

  const uint32_t savedMs = (totalMono / sizeof(int16_t) * 1000ULL) / SAMPLE_RATE;
  while (!momentsMs.empty() && momentsMs.back() > savedMs) momentsMs.pop_back();

  const bool headerOk = f && checkpointWavHeader(f, writer.partSize);
  const bool closeOk = f && std::fclose(f) == 0;
  if (audioOk && headerOk && closeOk) audioOk=writer.publish(spaceLimited);
  if (recorderUtc && headerOk && closeOk && totalMono) stage2::cutWindow(stage2::Point::Publish, noteNum, edgeMs);

  const auto momentCount = momentsMs.size();
  if (stagedMoments) stagedMoments->swap(momentsMs);
  else if (captureMoments && !momentsMs.empty() && !saveNoteMoments(noteNum, momentsMs)) {
    Serial.println("[Rec] warning: moments were not saved");
  }

  Serial.printf("[Rec] done: %lu bytes, %u moments\n",
                (unsigned long)totalMono, (unsigned)momentCount);
  const bool saved = audioOk && headerOk && closeOk && totalMono > 1000;
  if (saved && captureMoments) savedEdge.store(edgeMs);
#if PALA_STAGE2_BENCH
  session.reportCompletion(totalMono / sizeof(int16_t), saved);
#endif
#if PALA_WAV_TIMING
  if (result != CaptureResult::Running) {
#if PALA_TEST_FIRST_WRITE_DELAY_MS
    timing.reportStall(session.producedBlocks() * pcmFrames, totalMono / sizeof(int16_t),
                       result == CaptureResult::Overflow, saved);
#endif
#if PALA_STAGE2_BENCH
    Serial.printf("[RecCheck] e=%lu in=%lu out=%lu ov=%u ok=%u\n", (unsigned long)edgeMs,
                  (unsigned long)(session.producedBlocks() * pcmFrames),
                  (unsigned long)(totalMono / sizeof(int16_t)), result == CaptureResult::Overflow, saved);
    Serial.flush();
    Serial.printf("[RecCRC] e=%lu crc=%08lx\n", (unsigned long)edgeMs, (unsigned long)session.microphoneCrc());
    Serial.flush();
#endif
    Serial.printf("[RecPCM] edge=%lu queued_first=%lu captured_frames=%lu written_frames=%lu overflow=%u saved=%u\n",
        (unsigned long)edgeMs, (unsigned long)queuedAtFirstWrite,
        (unsigned long)(session.producedBlocks() * pcmFrames),
        (unsigned long)(totalMono / sizeof(int16_t)), result == CaptureResult::Overflow ? 1U : 0U,
        saved ? 1U : 0U);
  }
#endif
  return saved;
}

namespace {
constexpr char spool[] = "/.rec-inbox";
constexpr char mountedSpool[] = "/sdcard/.rec-inbox";
rec_priority::Gate gate;
unsigned windowDepth = 0;
TaskHandle_t spoolWriterTask = nullptr;
enum class SpoolReadiness { WaitingForStorage, Ready, Unavailable };
std::atomic<SpoolReadiness> spoolReadiness{SpoolReadiness::WaitingForStorage};

bool waitForRecordingSpool() {
  for (;;) {
    const auto state = spoolReadiness.load();
    if (state != SpoolReadiness::WaitingForStorage) return state == SpoolReadiness::Ready;
    vTaskDelay(1);
  }
}
String finishedName;
bool finishedOk = false;
// RegistryLock makes gate selection and its associated edge one publication.
uint32_t spoolEdge = 0;
bool scanNeeded = false;
bool coldPartials = true;

int present(const String& path) {
  struct stat info{};
  if (::stat(path.c_str(), &info) == 0) return 1;
  return errno == ENOENT ? 0 : -1;
}

bool saveStageMarks(const String& base, const std::vector<uint32_t>& marks) {
  if (marks.empty()) return true;
  File f = SD_MMC.open((base + ".mom").c_str(), FILE_WRITE);
  if (!f) return false;
  for (auto value : marks) {
    const String line = String(value) + "\n";
    if (f.print(line) != line.length()) return false;
  }
  f.flush();
  return true;
}

bool selectSpoolEdge(uint32_t& edge, uint32_t& epoch) {
  RegistryLock lock;
  const bool selected = gate.select([] { return spoolEdge; }, [](uint32_t& candidate) {
    if (!physicalRecordingHold(candidate)) return false;
    candidate = takeRegistry.oldest(candidate);
    return takeRegistry.mayAttach(candidate);
  }, edge);
  if (!selected) return false;
  spoolEdge = edge;
  epoch = captureEpochAtRec(edge);
  // A take started while the preceding writer was blocked retains the clock
  // state at acceptance, even if a trustworthy clock arrived in the meantime.
  for (int i = 0; i < capture_slots::Registry::count; ++i)
    if (takeRegistry.queued(i) && takeRegistry.edge(i) == edge) epoch = captures[i].epoch;
  return true;
}

void spoolWriter(void*) {
  for (;;) {
    uint32_t edge, epoch;
    if (selectSpoolEdge(edge, epoch)) {
      owner_trace::WorkScope ownerWork(owner_trace::Work::Spool);
      // A private file can be recorded before the old catalog's high-water ID
      // is known. Only the foreground may adopt it into the numbered catalog.
      char name[48];
      snprintf(name, sizeof(name), "s%lu_%08lx", (unsigned long)epoch, (unsigned long)esp_random());
      finishedName = name;
      const String base = String(spool) + "/" + name;
      const String temp = base + ".partial";
      std::vector<uint32_t> moments;
      finishedOk = recordHeldToPath(BTN_REC, true, edge, [&](int&, WavStartTiming& timing) -> String {
        const bool ready = waitForRecordingSpool();
        timing.mark("ready");
        if (!ready || recordingSpaceLow()) return {};
        const int wavExists = present(String("/sdcard") + base + ".wav");
        timing.mark("stat_wav");
        if (wavExists != 0) return {};
        // openExclusiveWav rejects an existing partial in the create operation.
        return temp;
      }, &moments) && saveStageMarks(base, moments);
      gate.complete();
    }
    if (gate.armed()) vTaskDelay(std::max<TickType_t>(1, pdMS_TO_TICKS(5)));
    else ulTaskNotifyTake(pdTRUE, portMAX_DELAY);
  }
}

// Resolve the target once and put it in the spool filename *before* moving any
// sidecars. Replaying a claim after a power cut uses exactly the same ID.
bool adopt(String name, String& utc, int& id) {
  owner_trace::WorkScope ownerWork(owner_trace::Work::Archive);
  int claimId = 0;
  uint32_t epoch = 0;
  std::string original;
  if (!rec_spool::parse(name.c_str(),claimId,original,epoch) ||
      !recording_parts::recover(std::string(mountedSpool)+"/"+original,esp_rom_crc32_le)) return false;
  return rec_spool::adopt("/sdcard/.rec-inbox/", "/sdcard" NOTES_DIR "/", name.c_str(), id,
      [](uint32_t seconds) { return nextNoteNumber(rec_spool::captureUtc(seconds).c_str()); },
      [&](int note, uint32_t seconds) {
        utc = rec_spool::captureUtc(seconds).c_str();
        return beginNoteRecording(note, utc);
      }, [&](int note, uint32_t) {
        return writeNoteMeta(note, "Inbox", utc);
      }, [&](int note) {
        stage2::cutWindow(stage2::Point::Publish, note);
        // Both direct and inbox captures are now owned by capture.pending.
        // Repair before saveTag can publish the row and remove that journal.
        char wav[72];
        monthly_recordings::path(wav, sizeof(wav), "/sdcard" NOTES_DIR, note, "wav");
        if (capture_wav::recover(wav) != capture_wav::State::Ready || !saveTag(note, "Inbox", utc)) return false;
#if PALA_STAGE2_BENCH
        stage2::captureInboxArchived(name.c_str(), note);
#endif
        Serial.printf("[Rec] archived %s\n", wav);
        return true;
      }, [](int note, const std::string&) {
        char wav[72];
        monthly_recordings::path(wav, sizeof(wav), "/sdcard" NOTES_DIR, note, "wav");
        return std::string(wav);
      }, [](int note, const std::string&) {
        char moments[72];
        monthly_recordings::path(moments, sizeof(moments), "/sdcard" NOTES_DIR, note, "moments");
        return std::string(moments);
      });
}

bool readSpoolNames(std::vector<String>& names) {
  auto* dir = opendir(mountedSpool);
  if (!dir) return false;
  std::vector<String> partials;
  bool ok = true;
  for (;;) {
    errno = 0;
    const auto* entry = readdir(dir);
    if (!entry) { ok = errno == 0; break; }
    String name(entry->d_name);
    if (name[0] != 's' && name[0] != 'c') continue;
    if (coldPartials && name[0] == 's' && name.endsWith(".partial")) partials.push_back(name);
    else if (name.endsWith(".wav")) names.push_back(name);
  }
  if (closedir(dir) != 0 || !ok) return false;
  // Finish enumerating before renaming entries on FAT. A failed startup scan
  // is retried while the worker is still stopped, never against its live WAV.
  for (const auto& partial : partials) {
    std::string name;
    const auto state = rec_spool::promotePartial("/sdcard/.rec-inbox/", partial.c_str(), name);
    if (state == capture_wav::State::Empty) continue;
    if (state != capture_wav::State::Ready) return false;
    names.push_back(name.c_str());
  }
  coldPartials = false;
  return true;
}
}

RecordingWindow::RecordingWindow(bool enabled) : enabled_(enabled && spoolWriterTask) {
  if (enabled_ && windowDepth++ == 0) {
    gate.arm();
    xTaskNotifyGive(spoolWriterTask);
  }
}
RecordingWindow::~RecordingWindow() {
  if (enabled_ && --windowDepth == 0) gate.disarm();
}

void initializeRecordingPriority() {
  if (!registryMutex || !captureTask || spoolWriterTask) return;
  if (xTaskCreatePinnedToCore(spoolWriter, "rec-priority", 6144, nullptr, 3, &spoolWriterTask, 0) != pdPASS) {
    spoolWriterTask = nullptr;
    Serial.println("[Rec] priority worker unavailable");
  } else spoolAvailable.store(true);
}

void completeRecordingStorageInit(bool storageReady) {
  scanNeeded = true;
  if (!storageReady || (mkdir(mountedSpool, 0777) != 0 && errno != EEXIST)) {
    spoolReadiness.store(SpoolReadiness::Unavailable);
    return;
  }
  markWavBoot("spool_dir");
  std::vector<String> existing;
  // Inspect old partials before the writer is allowed to create a live WAV.
  // A failed scan still allows a new private WAV. Existing claims must be
  // inspected successfully before foreground adoption allocates its note ID.
  if (readSpoolNames(existing)) scanNeeded = !existing.empty();
  spoolReadiness.store(SpoolReadiness::Ready);
}

static bool queuedFollowup() {
  if (!registryMutex) return false;
  RegistryLock lock;
  return takeRegistry.anyQueued();
}

#if PALA_USB_CARD_SERVICE
bool claimRecorderForUsbStorage() {
  if (!registryMutex) return gate.claimForeground();
  RegistryLock lock;
  return !takeRegistry.anyQueued() && gate.claimForeground();
}
#endif

bool pendingPriorityRecording() { return gate.pending() || queuedFollowup(); }
uint32_t recordingEdgeForSave(uint32_t fallback) {
  if (!registryMutex) return fallback;
  RegistryLock lock;
  return gate.pending() ? spoolEdge : takeRegistry.oldest(fallback);
}
bool newerRecordingPending(uint32_t edge) {
  if (!registryMutex) return false;
  RegistryLock lock;
  return takeRegistry.newer(edge);
}
bool recordingEdgeHandled(uint32_t edge) {
  if (!registryMutex) return false;
  RegistryLock lock;
  return takeRegistry.handled(edge);
}
bool takeRecordingQueueFull() {
  if (!registryMutex) return false;
  RegistryLock lock;
  return takeRegistry.takeFull();
}

void finishRecordingSave(uint32_t edge, bool success) {
  if (!registryMutex) return;
  RegistryLock lock;
  const int next = takeRegistry.finish(edge);
  if (next >= 0) {
    const auto other = takeRegistry.edge(next);
    stage2::previousSaveFinished(other, millis(), success);
    note_display::setPriorSavePending(other, false);
  }
}

RecordingSaveWindow::RecordingSaveWindow(uint32_t edge) : edge_(edge) {
  if (!registryMutex) return;
  RegistryLock lock;
  takeRegistry.openWindow(edge);
  xTaskNotifyGive(captureTask);
}

RecordingSaveWindow::~RecordingSaveWindow() {
  if (!registryMutex) return;
  finishRecordingSave(edge_, false);
  RegistryLock lock;
  takeRegistry.closeWindow(edge_);
}

bool RecordingSaveWindow::closeIfNoFollowup() {
  if (!registryMutex) return true;
  for (;;) {
    {
      RegistryLock lock;
      if (takeRegistry.newer(edge_)) return false;
      uint32_t down;
      if (!takeRegistry.watch() || !physicalRecordingPress(down) || down == edge_)
        return takeRegistry.seal(edge_);
      if (physicalRecordingHold(down)) {
        admitFollowup(down);
        return takeRegistry.seal(edge_);
      }
    }
    vTaskDelay(1);
  }
}

void latchPriorityRecording(uint32_t edge) {
  if (!windowDepth || !spoolWriterTask) return;
  RegistryLock lock;
  edge = takeRegistry.oldest(edge);
  if (takeRegistry.mayAttach(edge) && gate.request()) {
    spoolEdge = edge;
    xTaskNotifyGive(spoolWriterTask);
  }
}
void yieldToPriorityMicrophone() { while (gate.recording()) delay(5); }

static bool recoverInbox(const String& justRecorded, String& capturedUtc, int& capturedId) {
  owner_trace::WorkScope ownerWork(owner_trace::Work::Recovery);
  std::vector<String> names;
  if (!readSpoolNames(names)) return false;
  std::sort(names.begin(), names.end(), [](const String& a, const String& b) {
    return a.compareTo(b) < 0; // existing claims precede new admissions
  });
  for (const auto& name : names) {
    String utc;
    int id = 0;
    if (!adopt(name, utc, id)) return false;
    if (justRecorded.length() && (name == justRecorded || name.endsWith(String("-") + justRecorded))) {
      capturedId = id;
      capturedUtc = utc;
    }
  }
  scanNeeded = false;
  return true;
}

bool recoverSpooledRecordings() {
  if (!scanNeeded || gate.pending()) return true;
  String utc;
  int id = 0;
  return recoverInbox("", utc, id);
}

bool takePriorityRecording(String& capturedUtc, bool& captured, uint32_t edge) {
  const bool preferSpool = windowDepth || !noteCatalogReady();
  if (registryMutex) {
    RegistryLock lock;
    if (spoolWriterTask && (preferSpool || takeRegistry.anyQueued()) &&
        takeRegistry.mayAttach(edge) && gate.request()) {
      spoolEdge = edge;
      xTaskNotifyGive(spoolWriterTask);
    }
    captured = !gate.claimForeground();
  } else captured = false;
  if (!captured) return true;
  yieldToPriorityMicrophone();
  // Publish on the foreground after joining. A boot scan cannot accidentally
  // consume the current worker's completed file before its caller claims it.
  const String base = String(spool) + "/" + finishedName;
  bool ok = finishedOk && present(String("/sdcard") + base + ".wav") == 0 &&
      SD_MMC.rename((base + ".partial").c_str(), (base + ".wav").c_str());
  if (ok) {
#if PALA_STAGE2_BENCH
    stage2::bindInboxFile(lastCaptureEdge(), (finishedName + ".wav").c_str());
#endif
    scanNeeded = true;
    if (!noteCatalogBaseLoaded()) {
      // The audio and capture time are durable. Admit it when the old catalog
      // has been loaded, and never display a speculative note number.
      lastRecNum = -1;
      return true;
    }
    int id = 0;
    ok = recoverInbox(finishedName + ".wav", capturedUtc, id) && id > 0;
    if (ok) lastRecNum = id;
  }
  return ok;
}

void releaseRecordingPriority() { gate.release(); }
void refreshRecordingSpace() {
  owner_trace::WorkScope ownerWork(owner_trace::Work::Space);
  const auto total = SD_MMC.totalBytes(), used = SD_MMC.usedBytes();
  if (total && total >= used) {
    availableBytes = std::min<uint64_t>(UINT32_MAX, total-used);
    usedMiB = used / (1024ULL * 1024ULL);
    totalMiB = total / (1024ULL * 1024ULL);
  }
}
uint32_t recordingTotalMiB() { return totalMiB.load(); }
uint32_t recordingUsedMiB() { return usedMiB.load(); }
bool recordingSpaceLow() { return availableBytes.load() < 10UL * 1024UL * 1024UL; }
bool record(const String& capturedUtc, uint32_t edge) {
  int num = 0;
  const bool ok = recordHeldToPath(BTN_REC, true, edge, [&](int& noteNum, WavStartTiming& timing) -> String {
    if (noteNum < 0) {
      const bool recovered = recoverSpooledRecordings();
      timing.mark("recover");
      if (!recovered) return {};
    }
    num = nextNoteNumber(capturedUtc);
    timing.mark("allocate");
#if PALA_RECORDER_FIRST
    if (num <= 0) return {};
    const bool prepared = prepareNoteRecordingDirectory(num);
    timing.mark("directory");
    if (!prepared) return {};
#else
    if (num <= 0 || !beginNoteRecording(num, capturedUtc)) return {};
#endif
    noteNum = num;
    char path[64];
    monthly_recordings::path(path, sizeof(path), NOTES_DIR, num, "wav");
    return String(path);
  }, nullptr, &capturedUtc);
  if (ok) lastRecNum = num;
#if PALA_RECORDER_FIRST
  else if (num > 0) startRecorderWork();
#endif
  return ok;
}

bool recordQuestion(const char* path, int holdPin) {
  return recordHeldToPath(holdPin, false, millis(), [&](int&, WavStartTiming&) -> String {
    SD_MMC.remove(path);
    return String(path);
  });
}

bool playWavFile(const char* path) {
  File f = SD_MMC.open(path);
  if (!f) return false;
  if (f.size() <= 44) { f.close(); return false; }

  // Do not assume that PCM starts at byte 44. Speech services may add LIST,
  // JUNK or an extended fmt chunk, especially for longer generations.
  uint8_t riff[12];
  if (f.read(riff, sizeof(riff)) != sizeof(riff) ||
      memcmp(riff, "RIFF", 4) != 0 || memcmp(riff + 8, "WAVE", 4) != 0) {
    f.close(); return false;
  }
  uint16_t format = 0, channels = 0, bits = 0;
  uint32_t sampleRate = 0, dataOffset = 0, dataBytes = 0;
  while (f.available() >= 8) {
    uint8_t chunk[8];
    if (f.read(chunk, sizeof(chunk)) != sizeof(chunk)) break;
    const uint32_t chunkSize = (uint32_t)chunk[4] | ((uint32_t)chunk[5] << 8) |
                               ((uint32_t)chunk[6] << 16) | ((uint32_t)chunk[7] << 24);
    const uint32_t chunkData = f.position();
    if (!wavChunkFits(chunkData, chunkSize, f.size())) { f.close(); return false; }
    if (memcmp(chunk, "fmt ", 4) == 0 && chunkSize >= 16) {
      uint8_t fmt[16];
      if (f.read(fmt, sizeof(fmt)) != sizeof(fmt)) break;
      format = fmt[0] | ((uint16_t)fmt[1] << 8);
      channels = fmt[2] | ((uint16_t)fmt[3] << 8);
      sampleRate = (uint32_t)fmt[4] | ((uint32_t)fmt[5] << 8) |
                   ((uint32_t)fmt[6] << 16) | ((uint32_t)fmt[7] << 24);
      bits = fmt[14] | ((uint16_t)fmt[15] << 8);
    } else if (memcmp(chunk, "data", 4) == 0) {
      dataOffset = chunkData;
      dataBytes = chunkSize;
      break;
    }
    const uint64_t next = uint64_t(chunkData) + chunkSize + (chunkSize & 1U);
    if (next > f.size() || !f.seek(static_cast<uint32_t>(next))) break;
  }
  if (format != 1 || (channels != 1 && channels != 2) || bits != 16 ||
      (sampleRate != 16000 && sampleRate != 24000) || !dataOffset || !wavFramesValid(dataBytes, channels) ||
      !f.seek(dataOffset)) {
    Serial.printf("[Audio] invalid WAV fmt=%u ch=%u rate=%lu bits=%u data=%lu\n",
                  format, channels, static_cast<unsigned long>(sampleRate), bits,
                  static_cast<unsigned long>(dataBytes));
    f.close(); return false;
  }
  Serial.printf("[Audio] playing %s: %lu bytes, %lu Hz, %u ch\n", path,
                static_cast<unsigned long>(dataBytes),
                static_cast<unsigned long>(sampleRate), channels);

  const int monoBytes = 1024;
  uint8_t* monoBuf   = (uint8_t*)heap_caps_malloc(monoBytes,     MALLOC_CAP_8BIT);
  int16_t* stereoBuf = (int16_t*)heap_caps_malloc(monoBytes * 2, MALLOC_CAP_8BIT);

  if (!monoBuf || !stereoBuf) {
    if (monoBuf)   heap_caps_free(monoBuf);
    if (stereoBuf) heap_caps_free(stereoBuf);
    f.close();
    return false;
  }

  audioPlaying  = true;
  stopPlayback  = false;

  palaSoundSetEnabled(false);
  audio_playback_set_vol(palaPlaybackVolume());
  delay(25); // Give the codec amplifier time to leave mute before first PCM.

  uint32_t remaining = dataBytes;
  uint32_t playedBytes = 0;
  bool playbackOk = true;
  bool cancelArmed = digitalRead(BTN_REC) == HIGH;
  PalaPcmResampler resampler;
  while (remaining > 0 && !stopPlayback) {
    int readBytes = f.read(monoBuf, min<uint32_t>(monoBytes, remaining));
    if (readBytes <= 0) { playbackOk = false; break; }
    remaining -= readBytes;
    if (!wavFramesValid(readBytes, channels)) { playbackOk = false; break; }

    int samples = readBytes / 2;
    int16_t* input = (int16_t*)monoBuf;
    const int frames = samples / channels;
    const int outputSamples = resampler.convert(input, frames, channels,
                                                sampleRate == 24000, stereoBuf);
    const uint32_t outputBytes = outputSamples * 2 * sizeof(int16_t);
    if (outputBytes) {
      const int result = audio_playback_write((void*)stereoBuf, outputBytes);
      if (result != 0) {
        Serial.printf("[Audio] codec write failed: %d\n", result);
        playbackOk = false;
        break;
      }
      playedBytes += outputBytes;
    }

    if (digitalRead(BTN_REC) == HIGH) cancelArmed = true;
    else if (cancelArmed) {
      delay(20);
      if (digitalRead(BTN_REC) == LOW) {
        stopPlayback = true;
      }
    }
  }

  // I2S writes queue DMA buffers; muting immediately clips the final audio.
  // Flush with 160 ms of silence (more than the configured 6 x 240-frame DMA).
  if (playbackOk && !stopPlayback && playedBytes) {
    memset(stereoBuf, 0, monoBytes * 2);
    for (int i = 0; i < 5; ++i) {
      if (audio_playback_write(stereoBuf, monoBytes * 2) != 0) {
        playbackOk = false;
        break;
      }
    }
  }
  audio_playback_set_vol(0);
  audio_idle_suspend();
  palaSoundSetEnabled(true);

  heap_caps_free(monoBuf);
  heap_caps_free(stereoBuf);
  f.close();

  audioPlaying = false;
  stopPlayback = false;
  Serial.printf("[Audio] playback complete: %lu output bytes\n",
                static_cast<unsigned long>(playedBytes));
  return playbackOk && playedBytes > 0;
}
