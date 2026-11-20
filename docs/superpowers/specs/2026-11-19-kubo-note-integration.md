# Kubo Note integration

Kubo Note is the integration for Pala Note hardware. The Windows client and the
paired Bridge use the same local speech recognition API.

## Connection and offline behavior

- Discover the paired Bridge by its stable local hostname. Support manual
  fallback to a private IPv4 address on the connected Wi-Fi subnet.
- Verify the paired Bridge certificate and use the device-specific token.
- Keep recordings on the device when it cannot reach its paired Bridge.
- Validate a physical device on the permitted home network before trying a
  batch. Check that Bridge receives the WAV and the device shows its transcript.

## Windows and Google pilot

- Put the local service controls in Connections. Bridge lifecycle and the
  shared installer are later implementation steps.
- Qualify the transfer on one PC before adding another paired PC. Preserve
  the recording queue on retries and guard against duplicate side effects.
- Run the Google Tasks pilot with a fresh, approved synthetic utterance and
  the selected test destination after the physical transcription test.
  General dictation routing and Google Docs support are separate work.

See the [implementation plan](../plans/2026-11-19-kubo-pala-implementation.md)
and the [device setup instructions](../../../devices/kubo-note/README.md).
