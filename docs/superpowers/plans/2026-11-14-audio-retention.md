# Audio retention and shortcut heading plan

Goal: expose recording lifetime in Settings / History, default 7 days; retain
text until explicit deletion. Match Enable to the Keyboard shortcut heading.

## Design

Keep recordings for: 1 day, 7 days, 30 days, 90 days, 1 year, Forever. Persist
AudioRetentionDays (7 default, 0 Forever). Preserve valid custom day values.
Replace the old count20 eviction with time-based audio-only cleanup. Existing
settings without the property use 7. Text, edits, provenance and receipts remain
in protected History; Pending Review stays independent. Missing audio becomes
Text only and disables playback/export/retranscription/language replay.

Cleanup occurs on startup and hourly while idle. Changing retention applies
while idle. Busy operations defer it. Failures preserve files and show a local
warning. Only validated history identities are eligible; unknown/corrupt entries
are not removed. This updates an earlier explicit request, not a redesign.

## Steps

- [x] Core: setting/validation, audio-only retention, text-only library and tests.
- [x] Controller: idle scheduling, error feedback and audio eligibility guards.
- [x] UI: Settings/History dropdown, search route, local help, Enable typography.
- [x] Integrated synthetic checks, Core tests, independent review and Help update.

Do not use native input/audio or delete real data during verification. Preserve
API processes and loaded-DLL boundary when preparing deployment. Review and publish this follow-up under the existing instruction to commit/push
completed changes. Keep native verification limits explicit.
