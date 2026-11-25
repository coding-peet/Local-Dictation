#include "../pala_note/src/app/recording_sync_schedule.h"
#include "../pala_note/src/app/sync_queue_file.h"
#include <cassert>
#include <cstdio>
#include <initializer_list>

using Timing = SyncTiming;

static void metadataCannotStartManualSync() {
  RecordingSyncSchedule schedule;
  for (bool saved : {true, false}) {
    schedule.pauseForRecording();
    assert(!schedule.resumeAfterRecording(Timing::Manual, 2000));
    if (saved) assert(!schedule.begin(Timing::Manual, Timing::AfterRecording, 2000));
    assert(!schedule.permits(Timing::Manual, 2001, 2000, idleSleepPolicy(0)));
  }
}

static void manualRequestSurvivesCapture(bool authenticated) {
  InterruptedSyncResume resume;
  RecordingSyncSchedule schedule;
  assert(schedule.begin(Timing::Manual, Timing::Manual, 0));
  // Discovery has only the Note's preview. After selection, use the actual
  // authenticated transfer ID even when the progress preview is obsolete.
  const int activeA = authenticated ? 77 : 0;
  const int previewA = authenticated ? 4 : 77;
  resume.interrupted(activeA, previewA);
  schedule.pauseForRecording();
  assert(!schedule.permits(Timing::Manual, 10000, 0, idleSleepPolicy(0)));
  for (bool saved : {true, false}) {
    // A failed B leaves A eligible. A successful B joins the same request.
    assert(schedule.resumeAfterRecording(Timing::Manual, 11000));
    assert(!schedule.resumeAfterRecording(Timing::Manual, 11000));
    assert(schedule.permits(Timing::Manual, 11001, 11000, idleSleepPolicy(0)));
    if (saved) {
      assert(!schedule.begin(Timing::Manual, Timing::AfterRecording, 11000));
      assert(resume.preferred() == 77);
    }
    schedule.pauseForRecording();
  }
  // An EMPTY recording reply closes a completed manual request. The attached
  // cable can continue exchanging metadata while a later recording stays queued.
  schedule.finish();
  schedule.pauseForRecording();
  assert(!schedule.resumeAfterRecording(Timing::Manual, 15000));
  resume.stageCommitted(77, SyncQueueKind::Organization);
  assert(resume.preferred() == 77);
  resume.stageCommitted(77, SyncQueueKind::None);
  assert(resume.preferred() == 0);
  // Fresh BSS on reset, including a reset in the middle of a recording.
  RecordingSyncSchedule reboot;
  assert(!reboot.resumeAfterRecording(Timing::Manual, 0));
  assert(!reboot.begin(Timing::Manual, Timing::OnWake, 0));
}

static void automaticPolicySurvivesCapture() {
  for (Timing mode : {Timing::AfterRecording, Timing::OnWake}) {
    RecordingSyncSchedule schedule;
    schedule.pauseForRecording();
    assert(!schedule.resumeAfterRecording(mode, 1000));
    assert(schedule.begin(mode, Timing::AfterRecording, 1000) == (mode == Timing::AfterRecording));
    schedule.finish();
    assert(schedule.begin(mode, mode, 1000));
    schedule.pauseForRecording();
    assert(schedule.resumeAfterRecording(mode, 2000));
    assert(schedule.permits(mode, 2010, 2000, idleSleepPolicy(0)));
    schedule.pauseForRecording();
    // A configuration change cannot turn an automatic request into a manual one.
    assert(!schedule.resumeAfterRecording(Timing::Manual, 2500));
  }
  RecordingSyncSchedule beforeSleep;
  assert(beforeSleep.begin(Timing::BeforeSleep, Timing::BeforeSleep, 10));
  beforeSleep.pauseForRecording();
  assert(!beforeSleep.resumeAfterRecording(Timing::BeforeSleep, 2000));
  assert(!beforeSleep.begin(Timing::BeforeSleep, Timing::AfterRecording, 2000));
  assert(beforeSleep.begin(Timing::BeforeSleep, Timing::BeforeSleep, 2000));
  beforeSleep.finish();
  assert(beforeSleep.begin(Timing::BeforeSleep, Timing::Manual, 3000));
  beforeSleep.pauseForRecording();
  assert(beforeSleep.resumeAfterRecording(Timing::BeforeSleep, 3500));
  assert(beforeSleep.permits(Timing::BeforeSleep, 3510, 3500, idleSleepPolicy(0)));
}

int main() {
  metadataCannotStartManualSync();
  manualRequestSurvivesCapture(false);
  manualRequestSurvivesCapture(true);
  automaticPolicySurvivesCapture();
  std::puts("PASS: idle metadata, discovery and authenticated manual request, reset and automatic policies");
}
