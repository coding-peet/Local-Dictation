# Pala Note Shared Settings Implementation Plan

> Use the firmware and Local Dictation tracks in parallel. Verify the shared
> contract together before publishing either build.

**Goal:** Use the same settings page on the paired PC and the Note's Wi-Fi
Companion, and support adding a Bridge with physical USB or Bluetooth
confirmation.

**Architecture:** Bridge serves the page on loopback and brokers Note requests
through the active or an explicitly opened USB or BLE session. The Note serves
the identical HTML in Wi-Fi Companion mode. The Note stores up to five peer
credentials and prefers the last working authorized Bridge over Wi-Fi.

**Tech stack:** Arduino ESP32-S3, Bluedroid, ArduinoJson, Preferences, Python
Bridge, WPF desktop entry point, HTML and JavaScript.

**Spec:** [Kubo Note integration](../specs/2026-11-19-kubo-note-integration.md).

## Settings contract

- Serve one asset from the PC and from the Note. Keep only the current
  transport and the actual host capabilities in the response.
- GET /api/note-settings returns Note identity, Wi-Fi SSIDs and permitted
  peers. Bridge may add its own public configuration. Passwords and pairing
  keys stay outside the response.
- POST /api/note-settings accepts a same-origin authorized JSON action.
  Link requests use the existing authenticated session.
- PC enrollment starts a bounded broker operation. The page polls for the
  comparison code and explicitly matches or cancels it. The Note describes
  its upper and lower buttons during physical confirmation.

## Firmware track

- [x] Verify the current waveform and home layout are included in the build.
- [x] Persist up to five peers, migrate the legacy peer once and invalidate
      active authorization when removing a peer.
- [x] Use authenticated Numeric Comparison for BLE enrollment and physical
      confirmation for USB enrollment.
- [x] Serve the common page and enforce authorized settings operations.
- [x] Authenticate each Wi-Fi Bridge with its own paired certificate.
      Prefer the last working peer, then configured order. Keep an already
      submitted organization request assigned to its original Bridge.
- [x] Build and validate paired and generic candidates.
- [ ] Check removal, migration and cross-Bridge failover with physical peers.

## Local Dictation and Bridge track

- [x] Open the single browser UI from Connections. Keep existing desktop ASR
      controls and preserve user settings.
- [x] Bind the settings service to loopback and validate Host, Origin and
      write authorization. Serialize settings access with normal sync.
- [x] Require matching BLE codes on Windows and Note, and persist a distinct
      credential for each newly paired Note.
- [x] Exercise the common page and broker with isolated fixtures. Verify
      the normal Windows publish output.
- [ ] Confirm USB and BLE configuration on the physical Note and Windows PC.
