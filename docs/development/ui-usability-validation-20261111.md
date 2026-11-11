# UI usability validation — 11 November 2026

Scope: working-tree update after 7a99558 on feature/native-windows. Current user
input is recorded in docs/PRODUCT_REQUIREMENTS.md and the design in
docs/superpowers/specs/2026-11-11-direct-edit-and-settings.md.

## Result

Installed the tested candidate from C:\Git\Local-Dictation\candidate-ui into
C:\Git\Local-Dictation\app. The client is running normally, with the shortcut
enabled and no --no-hotkey argument. Deployment preserved the settings file;
subsequent native UI testing intentionally saved Large-v3/live selection.

The real Settings window was inspected after installation. It has flat content,
one primary recording action, automatic-save feedback, and no Save settings
button. Large-v3 was selected through the native dropdown/keyboard. Live was
enabled, the window showed Saved automatically, and settings.json confirmed:

~~~text
PrimaryModel: openai/whisper-large-v3
VerificationModel: kinit/whisper-large-v3-turbo-sk
Live: true
VerifyAfterRecording: true
Shortcut: Ctrl+Shift, enabled
~~~

The existing saved microphone ID was retained. That device is currently
unavailable; the window explicitly asks for an available input before recording.
This does not block independent saving of model and live settings.

## Checks passed

- Release build and Core tests: 84/84, zero skipped.
- Backend suite: 87/87, including Large-v3 live capability independent of readiness.
- CompactFlowProbe: 15/15.
- ReviewInputProbe: 8/8.
- SettingsProbe: 15/15, including failed writes, independent settings, unavailable
  microphone/verifier, invalid drafts, passive refresh, and Stop while offline.
- InlineReviewProbe: 15/15, including exact edits/paste, source preservation,
  caret/Undo across refresh, explicit approval, restart, protected save failure,
  and choosing a destination after failed insertion.
- Help: 20 unique IDs, 12 valid internal links, no remote resource dependency.
- Installed EXE, App/Core DLL and Help hashes match the final candidate.
- Source review found no remaining blocker in the targeted UI/persistence flows.

Reports and synthetic native renders are outside Git under
C:\AI\build\Local-Dictation-ui-final. Earlier red/green fixtures are under the
per-probe build/output directories documented in diagnostics/*/README.md.

Native inspection caught damaged UTF-8 status text inherited in AppController;
the nine affected lines were corrected, rebuilt, and visually checked. No other
source C# files contained the checked encoding patterns.

## Limits and preserved boundaries

The controller/WPF probes use synthetic text and temporary protected data.
They do not type into external applications, capture microphone audio, or run
inference. Direct editing was also inspected through its synthetic native WPF
render. Physical text delivery into Codex remains Peter's manual acceptance test.

Large-v3 capability reuses the original FP16 session path tested earlier; this
task did not repeat recognition quality/latency benchmarks. The candidate API
was initially stopped and was started from this checkout. Its model endpoint
reports Large-v3 installed, live_supported=true and live_ready=false.

Production API, TypeWhisper, original weights, precision and private history were
not changed. High Contrast improvements were reviewed in source; Windows High
Contrast settings were not toggled for a native acceptance test. The SDK printed
a certificate setup message during an agent build; a read-only check found only
the existing localhost development certificate dated 10 November, with no new
certificate created today.
