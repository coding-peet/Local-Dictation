# Live cursor continuation repair plan

**Goal:** Automatic cursor updates after speech pauses, with final-only Outlook
delivery after Ctrl+Shift. No ordinary banner action is required.

**Specification:** docs/PRODUCT_REQUIREMENTS.md, Banner and live cursor delivery.

## Current implementation and verification

- [x] Inspect the canonical checkout and preserve existing work.
- [x] Reproduce the initial generated-placeholder baseline failure natively.
- [x] Inspect real evidence of a later owned-range failure before input.
- [x] Replace the first provider-name/LF exception with independently proven,
  lease-scoped empty-value projection.
- [x] Add an exact, bounded suffix lookup after character construction fails.
- [x] Add native continuation, contradictory-value and failed-withdrawal cases.
- [x] Run Core281 and compile/publish the client and native diagnostic.
- [ ] Run current native GREEN through a policy-approved execution route.
  The new diagnostic EXE is blocked by the company ASR rule; do not bypass it.
- [x] Complete standard product deployment and verify installed hashes/PID.
- [ ] Verify actual live pause/continue and final Outlook insertion with Peter.

The first candidate's synthetic success did not establish real compatibility.
Use docs/development/live-cursor-value-range-20261112.md for current evidence.
