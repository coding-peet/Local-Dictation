# Verification banner preview

User-approved design: after Finish with Verify enabled, show the first-model
transcript only in the nonactivating banner while verification runs. Agreement
returns the final text to the original validated cursor and closes the banner.
Differences open the existing Review. Peter approved this behavior explicitly;
no additional design confirmation is needed.

## Implementation plan

- Preserve current source and reproduce the missing preview with a silent WPF
  renderer using real banner controls but no product startup or native HWND.
- Expose the current primary text only for a stopped Verify-enabled run. Keep
  verifier output separate. Preserve the guarded draft withdrawal/final insertion.
- Add an 80-DIP read-only scroll viewport. Expand the cloud to440x176 only for
  this preview; ordinary recording remains328x88. Preserve all nonactivation
  flags, dragging, theme resources and sharp content/transparent edges.
- Use actual mode dimensions for monitor positioning. Show Verifying while the
  second model runs, then collapse the preview after completion/cancellation.
- Run silent WPF Light/Dark layout/render checks, existing completion/ownership
  tests and full relevant suites. Obtain one focused review and publish a
  separate candidate. No PC control, recording, playback, native input or UIA.
- Update product requirements, offline Help and validation/deployment evidence.
  Replacing a running client requires normal Exit and a verified backup.

## Invariants

Keep the original destination identity and Finish-time safety gate. A changed,
unavailable or uncertain destination retains protected text for explicit Review;
showing a preview must never authorize deleting or pasting into another field.
The preview is a TextBlock, not an editor, and does not capture keyboard focus.
Live behavior during recording and Outlook final-only handling remain unchanged.

## Source scope

AppController.cs; Views/BannerWindow.cs; diagnostics/VerificationBannerRender;
requirements/Help and this plan. Native delivery and API/model behavior do not
need changes. Preserve all existing dirty work; no commit/push/worktree.
