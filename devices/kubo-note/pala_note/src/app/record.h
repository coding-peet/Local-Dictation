#pragma once
#include <Arduino.h>

bool record(const String& capturedUtc, uint32_t edge);
bool recordQuestion(const char* path, int holdPin);
bool playWavFile(const char* path);


// Active only in foreground operations which may block Home's REC handling.
// A spool writer uses the shared I2S capture task to save a private WAV.
class RecordingWindow {
 public:
  explicit RecordingWindow(bool enabled = true);
  ~RecordingWindow();
  RecordingWindow(const RecordingWindow&) = delete;
 private:
  bool enabled_;
};
#if PALA_WAV_TIMING
void markWavBoot(const char* phase);
#else
inline void markWavBoot(const char*) {}
#endif
bool initializeRecordingAudio();
uint32_t lastCaptureEdge();
void initializeRecordingPriority();
void completeRecordingStorageInit(bool storageReady);
void yieldToPriorityMicrophone();
void latchPriorityRecording(uint32_t edge);
bool pendingPriorityRecording();
#if PALA_USB_CARD_SERVICE
bool claimRecorderForUsbStorage();
#endif
bool physicalRecordingHold(uint32_t& edgeMs);
uint32_t recordingPressTime();
void consumeRecordingStop(uint32_t captureEdge);
bool takePriorityRecording(String& capturedUtc, bool& captured, uint32_t edge);
// Foreground owns the SD transaction. The PCM task may accept one later
// physical REC edge into its own ring while publication is still pending.
class RecordingSaveWindow {
 public:
  explicit RecordingSaveWindow(uint32_t edge);
  ~RecordingSaveWindow();
  bool closeIfNoFollowup();
  RecordingSaveWindow(const RecordingSaveWindow&) = delete;
 private:
  uint32_t edge_;
};
uint32_t recordingEdgeForSave(uint32_t fallback);
bool newerRecordingPending(uint32_t edge);
void finishRecordingSave(uint32_t edge, bool success);
bool takeRecordingQueueFull();
bool recordingEdgeHandled(uint32_t edge);
uint32_t allocatedRecordingBuffers();
bool physicalRecordingPress(uint32_t& edge);
bool recoverSpooledRecordings();
void releaseRecordingPriority();
void refreshRecordingSpace();
uint32_t recordingTotalMiB();
uint32_t recordingUsedMiB();
bool recordingSpaceLow();
