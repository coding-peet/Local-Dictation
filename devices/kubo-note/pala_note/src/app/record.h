#pragma once
#include <Arduino.h>

bool record(const String& capturedUtc);
bool recordQuestion(const char* path, int holdPin);
bool playWavFile(const char* path);


// Active only in foreground operations which may block Home's REC handling.
// The worker owns I2S and a private spool WAV, never UI, queue or catalog.
class RecordingWindow {
 public:
  explicit RecordingWindow(bool enabled = true);
  ~RecordingWindow();
  RecordingWindow(const RecordingWindow&) = delete;
 private:
  bool enabled_;
};
void initializeRecordingPriority();
void yieldToPriorityMicrophone();
void latchPriorityRecording(uint32_t edge);
bool pendingPriorityRecording();
bool physicalRecordingHold(uint32_t& edgeMs);
uint32_t recordingPressTime();
bool takePriorityRecording(String& capturedUtc, bool& captured);
bool recoverSpooledRecordings();
void releaseRecordingPriority();
void refreshRecordingSpace();
bool recordingSpaceLow();
