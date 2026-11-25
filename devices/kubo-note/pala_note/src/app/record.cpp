#include "Arduino.h"
#include "../../config.h"
#include "../../globals.h"
#include "../../types.h"
#include "record.h"
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
#include <sys/stat.h>
#include <ctime>
#include "record_priority.h"
#include "rec_spool.h"
#include "capture_wav_recovery.h"

extern "C" {
#include "../../src/audio/audio_bsp.h"
}

namespace {
std::atomic<bool> recordingUiBusy{false};
// Saturating estimate. Expensive FAT free-cluster queries run on the main
// loop after Home appears. The sample loop only compares an in-memory budget.
std::atomic<uint32_t> availableBytes{UINT32_MAX};
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

static bool writeWavHeader(File& file, uint32_t dataBytes) {
  const uint32_t resumeAt = file.position();
  if (!file.seek(0)) return false;
  const uint32_t fileSize = dataBytes + 36;
  const uint32_t byteRate = SAMPLE_RATE * 2;
  const uint32_t fmtLength = 16;
  const uint32_t sampleRate = SAMPLE_RATE;
  const uint16_t blockAlign = 2, audioFormat = 1, channels = 1, bits = 16;
  bool ok = file.write((uint8_t*)"RIFF", 4) == 4;
  ok &= file.write((uint8_t*)&fileSize, 4) == 4;
  ok &= file.write((uint8_t*)"WAVE", 4) == 4;
  ok &= file.write((uint8_t*)"fmt ", 4) == 4;
  ok &= file.write((uint8_t*)&fmtLength, 4) == 4;
  ok &= file.write((uint8_t*)&audioFormat, 2) == 2;
  ok &= file.write((uint8_t*)&channels, 2) == 2;
  ok &= file.write((uint8_t*)&sampleRate, 4) == 4;
  ok &= file.write((uint8_t*)&byteRate, 4) == 4;
  ok &= file.write((uint8_t*)&blockAlign, 2) == 2;
  ok &= file.write((uint8_t*)&bits, 2) == 2;
  ok &= file.write((uint8_t*)"data", 4) == 4;
  ok &= file.write((uint8_t*)&dataBytes, 4) == 4;
  file.flush();
  const bool restored = file.seek(max<uint32_t>(resumeAt, 44));
  return ok && restored;
}

static bool recordHeldToPath(const char* path, int holdPin, bool captureMoments, int noteNum,
                             uint32_t edgeMs, std::vector<uint32_t>* stagedMoments = nullptr) {
  // Read one stereo frame before SD queries, header writes or panel work.
  int16_t first[2]{};
  if (audio_playback_read(first, sizeof(first)) != 0) return false;
  struct SuspendMic { ~SuspendMic() { audio_idle_suspend(); } } suspendMic;
  const uint32_t firstPcm = millis();
  if (captureMoments) Serial.printf("[Rec] edge=%lu first_pcm=%lu latency_ms=%lu\n",
      (unsigned long)edgeMs, (unsigned long)firstPcm,
      (unsigned long)rec_priority::latency(edgeMs, firstPcm));
  Serial.printf("[Rec] %s\n", path);

  // A recording absent from the catalog must never be silently overwritten.
  // Questions remove their disposable file explicitly before reaching here.
  if (SD_MMC.exists(path)) return false;

  File f = SD_MMC.open(path, FILE_WRITE);
  if (!f) return false;

  uint8_t header[44]={};
  if (f.write(header, 44) != 44 || !writeWavHeader(f, 0)) {
    f.close();
    return false;
  }

  int16_t* sbuf = (int16_t*)heap_caps_malloc(REC_BUF,   MALLOC_CAP_8BIT);
  int16_t* mbuf = (int16_t*)heap_caps_malloc(REC_BUF/2, MALLOC_CAP_8BIT);
  if (!sbuf||!mbuf) {
    if(sbuf)heap_caps_free(sbuf); if(mbuf)heap_caps_free(mbuf);
    f.close(); return false;
  }

  if (!stagedMoments && !startRecordingUi(false, 0, 0, !captureMoments)) {
    heap_caps_free(sbuf); heap_caps_free(mbuf);
    f.close();
    SD_MMC.remove(path); // Only the empty file created by this attempt.
    return false;
  }

  const bool firstWritten = f.write(reinterpret_cast<uint8_t*>(first), sizeof(int16_t)) == sizeof(int16_t);
  uint32_t totalMono = firstWritten ? sizeof(int16_t) : 0, t0=millis(), lastCheckpoint=t0;
  std::vector<uint32_t> momentsMs;
  bool markReady = digitalRead(BTN_PWR) == HIGH;
  bool audioOk = firstWritten;

  while (audioOk && (digitalRead(holdPin)==LOW || millis()-t0 < 500)) {
    if (audio_playback_read((void*)sbuf, REC_BUF) != 0) {
      audioOk = false;
      Serial.println("[Rec] microphone read failed");
      break;
    }
    int mono = REC_BUF/4;
    for (int i=0;i<mono;i++) mbuf[i] = sbuf[i*2];
    size_t written = f.write((uint8_t*)mbuf, mono*2);
    if (written != (size_t)(mono * 2)) {
      audioOk = false;
      Serial.println("[Rec] storage write failed; partial recording retained");
      break;
    }
    totalMono += written;

    const uint32_t now = millis();
    if (now - lastCheckpoint >= 5000UL) {
      if (!writeWavHeader(f, totalMono)) {
        audioOk = false;
        Serial.println("[Rec] WAV checkpoint failed; partial recording retained");
        break;
      }
      lastCheckpoint = now;
      const auto freeAtStart = availableBytes.load();
      if (totalMono >= freeAtStart || freeAtStart - totalMono < 2UL * 1024UL * 1024UL) {
        Serial.println("[Rec] stopping: SD card is nearly full");
        break;
      }
    }

    bool markDown = captureMoments && digitalRead(BTN_PWR) == LOW;
    if (!markDown) {
      markReady = true;
    } else if (markReady) {
      markReady = false;
      uint32_t recordedSamples = totalMono / sizeof(int16_t);
      uint32_t markerMs = (recordedSamples * 1000ULL) / SAMPLE_RATE;
      if (momentsMs.empty() || markerMs - momentsMs.back() >= 250) {
        momentsMs.push_back(markerMs);
        if (!stagedMoments) startRecordingUi(true, markerMs, (int)momentsMs.size());
      }
    }
  }

  if (!stagedMoments) waitForRecordingUi();

  heap_caps_free(sbuf); heap_caps_free(mbuf);

  const bool headerOk = writeWavHeader(f, totalMono);
  f.close();

  if (stagedMoments) stagedMoments->swap(momentsMs);
  else if (captureMoments && !saveNoteMoments(noteNum, momentsMs)) {
    Serial.println("[Rec] warning: moments were not saved");
  }

  Serial.printf("[Rec] done: %lu bytes, %u moments\n",
                (unsigned long)totalMono, (unsigned)momentsMs.size());
  return audioOk && headerOk && totalMono > 1000;
}

namespace {
constexpr char spool[] = "/.rec-inbox";
constexpr char mountedSpool[] = "/sdcard/.rec-inbox";
rec_priority::Gate gate;
unsigned windowDepth = 0;
TaskHandle_t microphoneTask = nullptr;
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

void microphoneWorker(void*) {
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
      finishedOk = !recordingSpaceLow() && present(String("/sdcard") + base + ".wav") == 0 &&
          present(String("/sdcard") + temp) == 0 &&
          recordHeldToPath(temp.c_str(), BTN_REC, true, -1, edge, &moments) &&
          saveStageMarks(base, moments);
    });
    if (gate.armed()) vTaskDelay(std::max<TickType_t>(1, pdMS_TO_TICKS(5)));
    else ulTaskNotifyTake(pdTRUE, portMAX_DELAY);
  }
}

// Resolve the target once and put it in the spool filename *before* moving any
// sidecars. Replaying a claim after a power cut uses exactly the same ID.
bool adopt(String name, String& utc, int& id) {
  return rec_spool::adopt("/sdcard/.rec-inbox/", "/sdcard" NOTES_DIR "/", name.c_str(), id,
      nextNoteNumber, [&](int note, uint32_t seconds) {
        utc = rec_spool::captureUtc(seconds).c_str();
        return beginNoteRecording(note, utc);
      }, [&](int note, uint32_t) {
        return writeNoteMeta(note, "Inbox", utc);
      }, [&](int note) {
        // Both direct and inbox captures are now owned by capture.pending.
        // Repair before saveTag can publish the row and remove that journal.
        char wav[72];
        snprintf(wav, sizeof(wav), "/sdcard" NOTES_DIR "/note_%03d.wav", note);
        return capture_wav::recover(wav) == capture_wav::State::Ready && saveTag(note, "Inbox", utc);
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

RecordingWindow::RecordingWindow(bool enabled) : enabled_(enabled && microphoneTask) {
  if (enabled_ && windowDepth++ == 0) {
    gate.arm();
    xTaskNotifyGive(microphoneTask);
  }
}
RecordingWindow::~RecordingWindow() {
  if (enabled_ && --windowDepth == 0) gate.disarm();
}

void initializeRecordingPriority() {
  // An unreadable inbox may contain an existing ID claim. Retry inspection
  // before allowing another ordinary capture to allocate a number.
  scanNeeded = true;
  if (mkdir(mountedSpool, 0777) != 0 && errno != EEXIST) return;
  std::vector<String> existing;
  if (!readSpoolNames(existing)) return;
  scanNeeded = !existing.empty();
  if (xTaskCreatePinnedToCore(microphoneWorker, "rec-priority", 6144, nullptr, 3, &microphoneTask, 0) != pdPASS) {
    microphoneTask = nullptr;
    Serial.println("[Rec] priority worker unavailable");
  }
}

bool pendingPriorityRecording() { return gate.pending(); }
void latchPriorityRecording(uint32_t edge) {
  if (windowDepth && microphoneTask && !gate.pending()) {
    requestedEdge = edge;
    if (gate.request()) xTaskNotifyGive(microphoneTask);
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
  if (microphoneTask && (windowDepth || !noteCatalogReady()) && !gate.pending()) {
    requestedEdge = recordingPressTime();
    if (gate.request()) xTaskNotifyGive(microphoneTask);
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
  if (total && total >= used) availableBytes = std::min<uint64_t>(UINT32_MAX, total-used);
}
bool recordingSpaceLow() { return availableBytes.load() < 10UL * 1024UL * 1024UL; }
bool record(const String& capturedUtc) {
  if (!recoverSpooledRecordings()) return false;
  const uint32_t edgeMs = recordingPressTime();
  int num = nextNoteNumber();
  if (num <= 0 || !beginNoteRecording(num, capturedUtc)) return false;
  char path[64]; snprintf(path, sizeof(path), "%s/note_%03d.wav", NOTES_DIR, num);
  bool ok = recordHeldToPath(path, BTN_REC, true, num, edgeMs);
  if (ok) lastRecNum = num;
  return ok;
}

bool recordQuestion(const char* path, int holdPin) {
  SD_MMC.remove(path);
  return recordHeldToPath(path, holdPin, false, -1, millis());
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
