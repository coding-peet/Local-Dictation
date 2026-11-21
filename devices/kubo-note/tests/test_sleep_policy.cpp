#include "../pala_note/src/app/recording_sync_schedule.h"
#include "../pala_note/src/app/sleep_policy.h"
#include <initializer_list>

static_assert(validIdleSleepMinutes(0) && validIdleSleepMinutes(1) && validIdleSleepMinutes(99));
static_assert(!validIdleSleepMinutes(-1) && !validIdleSleepMinutes(100));

constexpr auto defaults = idleSleepPolicy(0);
static_assert(defaults.homeMs == 60000 && defaults.queuedMs == 180000 && defaults.companionMs == 300000);
constexpr auto oneMinute = idleSleepPolicy(1);
static_assert(oneMinute.homeMs == 60000 && oneMinute.queuedMs == 60000 && oneMinute.companionMs == 60000);
constexpr auto maxMinutes = idleSleepPolicy(99);
static_assert(maxMinutes.homeMs == 5940000 && maxMinutes.queuedMs == 5940000 && maxMinutes.companionMs == 5940000);



constexpr bool syncOnlyOpensOnItsSelectedEvent() {
  for (auto mode : {SyncTiming::BeforeSleep, SyncTiming::OnWake,
                    SyncTiming::AfterRecording, SyncTiming::Manual}) {
    for (auto event : {SyncTiming::BeforeSleep, SyncTiming::OnWake,
                       SyncTiming::AfterRecording, SyncTiming::Manual}) {
      RecordingSyncSchedule sync;
      const bool scheduled = event == mode || event == SyncTiming::Manual;
      if (sync.begin(mode, event, 100) != scheduled) return false;
      if (sync.permits(mode, 60000, 100, idleSleepPolicy(0)) != scheduled) return false;
      if (sync.permits(mode, 180100, 100, idleSleepPolicy(0))) return false;
    }
  }
  return true;
}

constexpr bool beforeSleepGetsOneBoundedAttemptPerIdlePeriod() {
  RecordingSyncSchedule sync;
  const auto sleep = idleSleepPolicy(1);
  if (!sync.begin(SyncTiming::BeforeSleep, SyncTiming::BeforeSleep, 100)) return false;
  if (!sync.permits(SyncTiming::BeforeSleep, 180099, 100, sleep)) return false;
  if (sync.permits(SyncTiming::BeforeSleep, 180100, 100, sleep)) return false;
  if (sync.permits(SyncTiming::BeforeSleep, 70000, 65000, sleep)) return false;
  if (sync.permits(SyncTiming::Manual, 70000, 100, sleep)) return false;
  sync.finish();
  return !sync.begin(SyncTiming::BeforeSleep, SyncTiming::BeforeSleep, 100) &&
          sync.begin(SyncTiming::BeforeSleep, SyncTiming::BeforeSleep, 65000);
}

static_assert(syncOnlyOpensOnItsSelectedEvent());
static_assert(beforeSleepGetsOneBoundedAttemptPerIdlePeriod());
