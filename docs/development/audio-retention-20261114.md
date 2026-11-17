# Audio retention and shortcut heading validation

Date: 2026-11-14

## Changes

Settings / History / Keep recordings for now controls protected audio lifetime.
The default is 7 days; options are 1/7/30/90 days, 1 year and Forever. Valid custom
stored day values remain visible. Text, edits, choices, provenance and insertion
receipts remain until explicit deletion. This replaces the old count20 eviction.

Only validated expired audio is removed. Cleanup runs on startup, hourly while
idle, and on a saved policy change while idle. Busy operations defer cleanup.
Missing audio stays as Text only; replay/export/playback require HasAudio.
Locked/corrupt/unknown records are preserved; cleanup errors have local feedback.
Choosing a longer period cannot restore audio already removed.

Enable now matches Keyboard shortcut: 18-DIP bold text and the same vertical
center at 640 and 1040 widths. Recording-mode labels use the semantic foreground
so they remain readable in Dark as well as Light.

## Verification

- 542 Core tests passed; zero failed/skipped. Eleven added cases and two updated
  legacy-policy cases cover defaults, range, age boundary, Forever, failure,
  repeated cleanup, text/pending/edits/receipt preservation and no count eviction.
- 176 integrated windowless WPF/controller checks passed; build: 0 warnings/errors.
- Real Settings renders inspected at 640/1040 in Light/Dark. Enable geometry and
  mode label foregrounds are asserted. Default/search/autosave tests expire only
  synthetic old audio while preserving recent audio and text-only rows.
- Independent read-only retention/integration review: no actionable findings.
- Help parse and internal links passed; current requirements, README and AGENTS
  replace obsolete count20 policy. Old dated delivery records remain historical.

No native window/input/microphone/playback or real-data deletion in verification.
Source checks and synthetic renders do not prove physical audio or native Insert.

Artifacts and guarded deployment manifest:

    C:\AI\build\Local-Dictation-retention

Existing API processes and protected text/settings must remain intact during
client deployment. Client files are replaced only after its normal shutdown.
