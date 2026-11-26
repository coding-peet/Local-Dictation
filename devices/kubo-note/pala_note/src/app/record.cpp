#include "Arduino.h"
#include "../../config.h"
#include "../../globals.h"
#include "../../types.h"
#include "monthly_recordings.h"
#include "record.h"
#include "recorder_store.h"
#include "SD_MMC.h"
#include "esp_heap_caps.h"
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
#include "record_priority.h"
#include "rec_spool.h"
#include "capture_wav_recovery.h"

extern "C" {
#include "../../src/audio/audio_bsp.h"
}

namespace {
constexpr size_t pcmFrames = 256;
using CaptureBuffer = pcm_capture::Buffer<128, pcmFrames>;
// Keep the cross-core indices in internal RAM. Only block storage goes to PSRAM.
alignas(CaptureBuffer) uint8_t captureBufferControl[sizeof(CaptureBuffer)];
CaptureBuffer* captureBuffer = nullptr;
TaskHandle_t captureTask = nullptr;
std::atomic<bool> captureOwned{false};
std::atomic<bool> captureStop{false};
int captureHoldPin = -1;
bool captureMarkers = false;
// The session owner is the only writer that can use this buffer.
int16_t writeBatch[REC_BUF / (2 * sizeof(int16_t))];
enum class CaptureResult { Running, Complete, Cancelled, Overflow, CodecError };
std::atomic<CaptureResult> captureResult{CaptureResult::Complete};

void microphoneProducer(void*) {
  int16_t stereo[pcmFrames * 2];
  constexpr size_t minBlocks = (SAMPLE_RATE / 2 + pcmFrames - 1) / pcmFrames;
  for (;;) {
    ulTaskNotifyTake(pdTRUE, portMAX_DELAY);
    if (captureResult.load(std::memory_order_acquire) != CaptureResult::Running) continue;
    const auto holdPin = captureHoldPin;
    const bool markers = captureMarkers;
    size_t blocks = 0;
    CaptureResult result = CaptureResult::Complete;
    for (;;) {
      if (captureStop.load()) {
        result = CaptureResult::Cancelled;
        break;
      }
      if (blocks >= minBlocks && digitalRead(holdPin) != LOW) break;
      if (audio_playback_read(stereo, sizeof(stereo)) != 0) {
        result = CaptureResult::CodecError;
        break;
      }
      if (!captureBuffer->pushStereo(stereo, millis(), markers && digitalRead(BTN_PWR) == LOW)) {
        result = CaptureResult::Overflow;
        break;
      }
      ++blocks;
    }
    audio_idle_suspend();
    captureResult.store(result, std::memory_order_release);
  }
}

class MicrophoneSession {
 public:
  MicrophoneSession(int holdPin, bool markers) {
    if (!captureTask || captureOwned.exchange(true)) return;
    active_ = true;
    captureBuffer->reset();
    captureHoldPin = holdPin;
    captureMarkers = markers;
    captureStop.store(false);
    captureResult.store(CaptureResult::Running, std::memory_order_release);
    xTaskNotifyGive(captureTask);
  }

  ~MicrophoneSession() {
    if (!active_) return;
    stop();
    while (result() == CaptureResult::Running) vTaskDelay(1);
    captureOwned.store(false);
  }

  MicrophoneSession(const MicrophoneSession&) = delete;
  MicrophoneSession& operator=(const MicrophoneSession&) = delete;

  bool read(CaptureBuffer::Block& block) {
    if (!active_) return false;
    for (;;) {
      const bool stopped = result() != CaptureResult::Running;
      if (captureBuffer->pop(block)) return true;
      if (stopped) return false;
      vTaskDelay(1);
    }
  }

  void stop() { captureStop.store(true); }
  CaptureResult result() const { return captureResult.load(std::memory_order_acquire); }

 private:
  bool active_ = false;
};

std::atomic<bool> recordingUiBusy{false};
// Saturating estimate. Expensive FAT free-cluster queries run on the main
// loop after Home appears. The sample loop only compares an in-memory budget.
std::atomic<uint32_t> availableBytes{UINT32_MAX};
std::atomic<uint32_t> totalMiB{0}, usedMiB{0};
bool recordingUiProgress = false;
bool recordingUiQuestion = false;
uint32_t recordingUiMarkerMs = 0;
int recordingUiMarkerCount = 0;

void recordingUiTask(void*) {
  // E-paper is intentionally refreshed on the other ESP32 core. The recording
  // loop must keep draining I2S while the comparatively slow panel updates.
  if (recordingUiProgress) showRecordingProgress(recordingUiMarkerMs, recordingUiMarkerCount);
  else if (recordingUiQuestion) showAskListening();
  else showRecording();
  recordingUiBusy = false;
  vTaskDelete(nullptr);
}

bool startRecordingUi(bool progress = false, uint32_t markerMs = 0, int markerCount = 0, bool question = false) {
  // Never wait for e-paper while draining the microphone. A marker remains
  // saved even when its visual update is skipped during an existing refresh.
  if (recordingUiBusy.load()) return true;
  recordingUiProgress = progress;
  recordingUiQuestion = question;
  recordingUiMarkerMs = markerMs;
  recordingUiMarkerCount = markerCount;
  recordingUiBusy = true;
  const BaseType_t started = xTaskCreatePinnedToCore(
      recordingUiTask, "recording-ui", 4096, nullptr, 1, nullptr, 0);
  if (started != pdPASS) {
    recordingUiBusy = false;
    Serial.println("[Rec] warning: recording UI task could not be started");
  }
  return started == pdPASS;
}

void waitForRecordingUi() {
  while (recordingUiBusy) delay(1);
}
}  // namespace

bool initializeRecordingAudio() {
  if (captureTask) return true;
  void* storage = heap_caps_malloc(sizeof(CaptureBuffer::Storage), MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT);
  if (!storage) return false;
  auto* blocks = new (storage) CaptureBuffer::Storage;
  captureBuffer = new (captureBufferControl) CaptureBuffer(*blocks);
  if (xTaskCreatePinnedToCore(microphoneProducer, "pcm-capture", 4096, nullptr, 4, &captureTask, 1) == pdPASS) {
    return true;
  }
  captureBuffer->~CaptureBuffer();
  heap_caps_free(storage);
  captureBuffer = nullptr;
  return false;
}

static FILE* openExclusiveWav(const String& path) {
  const String mounted = String("/sdcard") + path;
  const int fd = ::open(mounted.c_str(), O_WRONLY | O_CREAT | O_EXCL, 0666);
  if (fd < 0) return nullptr;
  auto* file = fdopen(fd, "wb");
  if (!file) ::close(fd);
  else setvbuf(file, nullptr, _IONBF, 0);
  return file;
}

static bool writeWavHeader(FILE* file, uint32_t dataBytes) {
  uint8_t header[44] = {
    'R', 'I', 'F', 'F', 0, 0, 0, 0, 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ',
    16, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    2, 0, 16, 0, 'd', 'a', 't', 'a', 0, 0, 0, 0,
  };
  capture_wav::putSize(header + 4, dataBytes + 36);
  capture_wav::putSize(header + 24, SAMPLE_RATE);
  capture_wav::putSize(header + 28, SAMPLE_RATE * 2);
  capture_wav::putSize(header + 40, dataBytes);
  const long resumeAt = std::ftell(file);
  return resumeAt >= 0 && std::fseek(file, 0, SEEK_SET) == 0 &&
      std::fwrite(header, 1, sizeof(header), file) == sizeof(header) &&
      std::fflush(file) == 0 && ::fsync(fileno(file)) == 0 &&
      (resumeAt <= 44 || std::fseek(file, resumeAt, SEEK_SET) == 0);
}

template<class PreparePath>
static bool recordHeldToPath(int holdPin, bool captureMoments, uint32_t edgeMs,
                             PreparePath preparePath, std::vector<uint32_t>* stagedMoments = nullptr,
                             const String* recorderUtc = nullptr) {
  bool markReady = digitalRead(BTN_PWR) == HIGH;
  MicrophoneSession session(holdPin, captureMoments);
  CaptureBuffer::Block firstBlock;
  if (!session.read(firstBlock)) {
    Serial.println("[Rec] microphone did not provide PCM");
    return false;
  }
  const uint32_t firstPcm = firstBlock.sampledAt;
  int noteNum = -1;
  const String path = preparePath(noteNum);
  if (path.isEmpty()) return false;
  if (captureMoments) Serial.printf("[Rec] edge=%lu first_pcm=%lu latency_ms=%lu\n",
      (unsigned long)edgeMs, (unsigned long)firstPcm,
      (unsigned long)rec_priority::latency(edgeMs, firstPcm));
  Serial.printf("[Rec] %s\n", path.c_str());

  // Exclusive creation decides absence and opens in one FAT operation.
  // Reserve work only after success, and before any recoverable WAV data.
  FILE* f = openExclusiveWav(path);
  if (!f) {
    Serial.printf("[Rec] cannot create WAV: %d\n", errno);
    return false;
  }
#if PALA_RECORDER_FIRST
  if (recorderUtc && !reserveRecorderWork(noteNum, *recorderUtc)) {
    std::fclose(f);
    return false;
  }
#else
  (void)recorderUtc;
#endif
  if (!writeWavHeader(f, 0)) {
    std::fclose(f);
    return false;
  }

  uint32_t totalMono = 0;
  auto append = [&](const int16_t* samples, size_t bytes) {
    const size_t written = std::fwrite(samples, 1, bytes, f);
    totalMono += written & ~size_t{1};
    if (written == bytes) return true;
    Serial.println("[Rec] storage write failed; partial recording retained");
    return false;
  };

  constexpr size_t blockBytes = sizeof(firstBlock.samples);
  static_assert(sizeof(writeBatch) % blockBytes == 0);
  bool audioOk = append(firstBlock.samples, blockBytes);
  const uint32_t firstWrite = millis();
  if (audioOk && captureMoments)
    Serial.printf("[Rec] first_write=%lu write_latency_ms=%lu\n", (unsigned long)firstWrite,
                  (unsigned long)rec_priority::latency(edgeMs, firstWrite));
  if (audioOk && !stagedMoments) startRecordingUi(false, 0, 0, !captureMoments);

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
    if (!stagedMoments) startRecordingUi(true, markerMs, (int)momentsMs.size());
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

    const uint32_t now = millis();
    if (now - lastCheckpoint < 5000UL) continue;
    if (!writeWavHeader(f, totalMono)) {
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
  if (!audioOk) session.stop();
  const auto result = session.result();
  if (result == CaptureResult::Overflow)
    Serial.println("[Rec] PCM buffer overflow; partial recording retained");
  if (result == CaptureResult::CodecError)
    Serial.println("[Rec] microphone read failed; partial recording retained");
  audioOk &= result == CaptureResult::Complete || (spaceLimited && result == CaptureResult::Cancelled);

  const uint32_t savedMs = (totalMono / sizeof(int16_t) * 1000ULL) / SAMPLE_RATE;
  while (!momentsMs.empty() && momentsMs.back() > savedMs) momentsMs.pop_back();

  const bool headerOk = writeWavHeader(f, totalMono);
  const bool closeOk = std::fclose(f) == 0;
  if (!stagedMoments) waitForRecordingUi();

  const auto momentCount = momentsMs.size();
  if (stagedMoments) stagedMoments->swap(momentsMs);
  else if (captureMoments && !momentsMs.empty() && !saveNoteMoments(noteNum, momentsMs)) {
    Serial.println("[Rec] warning: moments were not saved");
  }

  Serial.printf("[Rec] done: %lu bytes, %u moments\n",
                (unsigned long)totalMono, (unsigned)momentCount);
  return audioOk && headerOk && closeOk && totalMono > 1000;
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
uint32_t requestedEdge = 0;
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

void spoolWriter(void*) {
  for (;;) {
    gate.service([] { return requestedEdge; }, physicalRecordingHold, [](uint32_t edge) {
      // A private file can be recorded before the old catalog's high-water ID
      // is known. Only the foreground may adopt it into the numbered catalog.
      char name[48];
      snprintf(name, sizeof(name), "s%lu_%08lx", (unsigned long)time(nullptr), (unsigned long)esp_random());
      finishedName = name;
      const String base = String(spool) + "/" + name;
      const String temp = base + ".partial";
      std::vector<uint32_t> moments;
      finishedOk = recordHeldToPath(BTN_REC, true, edge, [&](int&) -> String {
        if (!waitForRecordingSpool() || recordingSpaceLow() || present(String("/sdcard") + base + ".wav") != 0 ||
            present(String("/sdcard") + temp) != 0) return {};
        return temp;
      }, &moments) && saveStageMarks(base, moments);
    });
    if (gate.armed()) vTaskDelay(std::max<TickType_t>(1, pdMS_TO_TICKS(5)));
    else ulTaskNotifyTake(pdTRUE, portMAX_DELAY);
  }
}

// Resolve the target once and put it in the spool filename *before* moving any
// sidecars. Replaying a claim after a power cut uses exactly the same ID.
bool adopt(String name, String& utc, int& id) {
  return rec_spool::adopt("/sdcard/.rec-inbox/", "/sdcard" NOTES_DIR "/", name.c_str(), id,
      [](uint32_t seconds) { return nextNoteNumber(rec_spool::captureUtc(seconds).c_str()); },
      [&](int note, uint32_t seconds) {
        utc = rec_spool::captureUtc(seconds).c_str();
        return beginNoteRecording(note, utc);
      }, [&](int note, uint32_t) {
        return writeNoteMeta(note, "Inbox", utc);
      }, [&](int note) {
        // Both direct and inbox captures are now owned by capture.pending.
        // Repair before saveTag can publish the row and remove that journal.
        char wav[72];
        monthly_recordings::path(wav, sizeof(wav), "/sdcard" NOTES_DIR, note, "wav");
        if (capture_wav::recover(wav) != capture_wav::State::Ready || !saveTag(note, "Inbox", utc)) return false;
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
  if (xTaskCreatePinnedToCore(spoolWriter, "rec-priority", 6144, nullptr, 3, &spoolWriterTask, 0) != pdPASS) {
    spoolWriterTask = nullptr;
    Serial.println("[Rec] priority worker unavailable");
  }
}

void completeRecordingStorageInit(bool storageReady) {
  scanNeeded = true;
  if (!storageReady || (mkdir(mountedSpool, 0777) != 0 && errno != EEXIST)) {
    spoolReadiness.store(SpoolReadiness::Unavailable);
    return;
  }
  std::vector<String> existing;
  // Inspect old partials before the writer is allowed to create a live WAV.
  // A failed scan still allows a new private WAV. Existing claims must be
  // inspected successfully before foreground adoption allocates its note ID.
  if (readSpoolNames(existing)) scanNeeded = !existing.empty();
  spoolReadiness.store(SpoolReadiness::Ready);
}

bool pendingPriorityRecording() { return gate.pending(); }
void latchPriorityRecording(uint32_t edge) {
  if (windowDepth && spoolWriterTask && !gate.pending()) {
    requestedEdge = edge;
    if (gate.request()) xTaskNotifyGive(spoolWriterTask);
  }
}
void yieldToPriorityMicrophone() { while (gate.recording()) delay(5); }

static bool recoverInbox(const String& justRecorded, String& capturedUtc, int& capturedId) {
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

bool takePriorityRecording(String& capturedUtc, bool& captured) {
  if (spoolWriterTask && (windowDepth || !noteCatalogReady()) && !gate.pending()) {
    requestedEdge = recordingPressTime();
    if (gate.request()) xTaskNotifyGive(spoolWriterTask);
  }
  captured = !gate.claimForeground();
  if (!captured) return true;
  if (gate.recording()) startRecordingUi();
  yieldToPriorityMicrophone();
  waitForRecordingUi();
  // Publish on the foreground after joining. A boot scan cannot accidentally
  // consume the current worker's completed file before its caller claims it.
  const String base = String(spool) + "/" + finishedName;
  bool ok = finishedOk && present(String("/sdcard") + base + ".wav") == 0 &&
      SD_MMC.rename((base + ".partial").c_str(), (base + ".wav").c_str());
  if (ok) {
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
bool record(const String& capturedUtc) {
  int num = 0;
  const bool ok = recordHeldToPath(BTN_REC, true, recordingPressTime(), [&](int& noteNum) -> String {
    if (!recoverSpooledRecordings()) return {};
    num = nextNoteNumber(capturedUtc);
#if PALA_RECORDER_FIRST
    if (num <= 0 || !prepareNoteRecordingDirectory(num)) return {};
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
  return recordHeldToPath(holdPin, false, millis(), [&](int&) -> String {
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
