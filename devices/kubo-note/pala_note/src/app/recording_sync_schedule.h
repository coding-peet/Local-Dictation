#pragma once

#include "sleep_policy.h"

enum class SyncTiming : uint8_t { BeforeSleep, OnWake, AfterRecording, Manual };

constexpr bool validSyncTiming(int mode) {
  return mode >= 0 && mode <= static_cast<int>(SyncTiming::Manual);
}

class RecordingSyncSchedule {
 public:
  constexpr bool begin(SyncTiming mode, SyncTiming event, uint32_t activityAt) {
    if (event != SyncTiming::Manual && event != mode) return false;
    if (running_ && event != SyncTiming::Manual) return false;
    if (event == SyncTiming::BeforeSleep) {
      if (triedBeforeSleep_ && beforeSleepActivityAt_ == activityAt) return false;
      triedBeforeSleep_ = true;
      beforeSleepActivityAt_ = activityAt;
    }
    event_ = event;
    running_ = true;
    return true;
  }

  constexpr bool permits(SyncTiming mode, uint32_t now, uint32_t activityAt,
                         const IdleSleepPolicy& sleep) const {
    if (!running_ || (event_ != SyncTiming::Manual && event_ != mode)) return false;
    if (beforeSleep()) {
      return activityAt == beforeSleepActivityAt_ && now - activityAt < sleep.homeMs + 120000UL;
    }
    return now - activityAt < sleep.queuedMs;
  }

  // Capture suspends the actual request, including a manual request still
  // waiting for discovery. A metadata session never begins this schedule.
  constexpr void pauseForRecording() {
    resumeAfterCapture_ = running_ && event_ != SyncTiming::BeforeSleep;
    running_ = false;
  }
  constexpr bool resumeAfterRecording(SyncTiming mode, uint32_t activityAt) {
    if (!resumeAfterCapture_) return false;
    resumeAfterCapture_ = false;
    return begin(mode, event_, activityAt);
  }
  constexpr bool running() const { return running_; }
  constexpr bool beforeSleep() const { return running_ && event_ == SyncTiming::BeforeSleep; }
  constexpr void finish() { running_ = false; resumeAfterCapture_ = false; }

 private:
  SyncTiming event_ = SyncTiming::Manual;
  uint32_t beforeSleepActivityAt_ = 0;
  bool running_ = false;
  bool resumeAfterCapture_ = false;
  bool triedBeforeSleep_ = false;
};
