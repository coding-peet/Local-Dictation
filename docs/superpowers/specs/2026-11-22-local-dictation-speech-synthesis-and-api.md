# Local Dictation speech synthesis and API

Date 2026-11-22

Approved on 2026-11-22. The first implementation covers short selected
and clipboard text with Piper Lili and the scoped loopback API. The
release scope below also covers chapter playback, export and model controls.

## Goal

Add local read-aloud to Local Dictation and let other applications use the
same speech synthesis and transcription service. Support short highlighted
text, longer documents, and an optional LAN connection. Keep interactive
dictation responsive during speech synthesis.

See the [live dictation design](2026-11-10-live-dictation-design.md) for the
existing ASR service and the [Kubo Note integration](2026-11-19-kubo-note-integration.md)
for the existing Note and Bridge integration.

## Confirmed decisions

- Interactive transcription has first claim on GPU capacity. Long synthesis
  can pause at a paragraph boundary and continue from that point. A reader
  can prepare the next paragraph while the current one plays.
- Provide a compute selection for both transcription and speech synthesis
  models, with an option to test GPU operation when selecting it.
- Offer optional LAN access with the default set to off. Limit resource
  consumption so network requests cannot monopolize the PC.
- Use a separate **API** tab in Pala settings. Show application keys in a
  table with the application name, **Transcription** and **Speech**
  permissions, last use, and a revoke action. The authorized call processes
  the input supplied with that call. History and notes are outside the API
  permissions.

## Release scope

The first release reads selected text and individual chapters on this PC.
The loopback API requires per-application keys for both transcription and
speech synthesis from the first release.
Full-document reading and the opt-in HTTPS listener for the phone and
second PC follow in later releases.

## First-release workflow

### Read-aloud

- A Windows shortcut reads selected text in the focused application. The
  same shortcut stops playback. Starting dictation interrupts read-aloud
  and preserves the chapter position.
- Use Windows UI Automation where the application exposes a text selection.
  Provide an explicit **Read from clipboard** action for other applications.
- Keep **Read selection** and **Read chapter** as distinct actions.
  A chapter uses one language and one voice throughout, including when
  the text contains Slovak and English. Use the primary language as the
  default and allow the user to choose the language and voice manually.
  Keep the manual language override available for short selections.
- In **Read chapter**, accept pasted text or a single TXT or Markdown file
  containing one chapter.
- For chapters, preserve the input text across paragraphs. Maintain the
  chosen voice, position, and pause state while scheduling generation.
- In the first release, play one chapter with pause and resume and let the
  user save the complete chapter as an MP3 file. MP3 is the sole export
  format in the first-release chapter reader. Chapter export shares the
  GPU-priority policy.

### Manage synthesis models

Expose speech synthesis models in **Models** using the same workflow as
transcription. Show installed models and let users add a catalog entry or
a supported public Hugging Face model URL. Check size and runtime
compatibility before downloading. Show supported languages and, where a
model exposes multiple voices, let the user choose a voice separately.

Allow testing and removal of installed synthesis models. Apply the same
safeguards as for transcription when a model is in use. A removed or
unavailable model must be identifiable in any setting that still refers to
it, so the user can select a replacement. The suggested voices below are
defaults. Users can add other compatible models and remove these models.

### Compute and validation

- Offer **Auto**, **CPU**, and **GPU** where the installed model and
  runtime support those choices. Show the device that actually processed
  the last test. The current Whisper runtime requires CUDA. It retains
  selected weights in system RAM between requests, which is separate from
  CPU inference.
- Reuse the existing transcription model test. For speech, synthesize and
  play a short test sentence. Report the actual backend, time to first
  usable audio or text, and peak GPU memory. Distinguish a successful GPU
  run from CPU fallback and a failed test.
- In **Auto**, a short speech request can use CPU when Whisper needs GPU
  capacity. A long job can wait. An explicit **GPU** selection waits with
  a status when transcription takes priority. Only **Auto** may use CPU
  instead. An unsupported backend or failed GPU test needs a clear result.

### API access

- Use one service API. Apps on the same PC use loopback. The API tab
  shows the local address and whether the service is running.
- Add a speech endpoint such as **POST /v1/audio/speech** that accepts text
  and optional voice or language and returns audio. Existing transcription
  entry points remain available through the corresponding permission.
  Callers decide where to play or store the returned audio.
- Generate a key for each client. Select either or both permissions per
  key. An app can use two keys if the capabilities need separate
  revocation. Show the secret once and support revocation on the PC.
- Check the application key and its permission on every local transcription
  and speech synthesis operation, including batch and live transcription.
  Provision distinct keys for Local Dictation and Bridge automatically
  before enforcing key checks on the existing transcription routes.
  Other applications use their own keys from the API tab.
- Preserve the paired Note's recording, synchronization, and Ask Kubo
  flows through this migration. Verify both built-in clients can use
  their own credentials and that unauthorized requests are rejected.

## Evidence and model shortlist

### Local tests on RTX 3050 Laptop, 4 GiB VRAM

| Test | Result |
| --- | --- |
| Piper Lili, short Slovak sentence, 15 alternating warmed runs per device | CPU median 210 ms. CUDA median 146 ms and about 359 MiB additional dedicated VRAM. CUDA was slower in an earlier run with just one warm-up. |
| Piper Lili CUDA with KInIT Whisper Turbo on the same 6.16-second sample | Three paired runs. Median transcription request 2.03 s with Piper loaded and idle, 2.07 s during active synthesis. The difference was within run variation. Combined GPU peak 2.42 GiB. |
| Kokoro Q8 with the installed Windows Node runtime | CPU median 3.28 s for a three-second sentence after warm-up. DirectML selected the RTX, then failed on ConvTranspose with 0x80070057 before producing audio. The successful CPU timings apply to this runtime. |

Lili on CPU is the proposed baseline for short Slovak responses. Kokoro
remains the working English voice used for Help narration. The temporary
Piper CUDA package used for the benchmark was removed after the test.

See the [matching Kokoro DirectML issue](https://github.com/hexgrad/kokoro/issues/79)
and the [ONNX Runtime GPU package used for Piper](https://pypi.org/project/onnxruntime-gpu/1.26.0/).

### Public evidence

Hugging Face Open TTS Leaderboard, 2026-11-21 release, Slovak
ZTTS2-Mini-clean split. WER measures text fidelity via automatic
transcription of the synthesized audio.

| Model | Slovak WER without cloning |
| --- | ---: |
| Supertonic 3 | 5.96% |
| OmniVoice | 6.84% |
| Fish Audio Drama 3 | 7.47% |
| Higgs TTS 3 4B | 16.18% |

With cloning, Higgs and OmniVoice scored 5.21% and 7.46% respectively.
The table has no score for Piper Lili. The scores describe the published
dataset and configuration.

- [Open TTS Leaderboard, 2026-11-21](https://huggingface.co/spaces/hf-audio/open_tts_leaderboard?version=21-11-2026)
- [ZTTS2-Eval Mini dataset and methodology](https://huggingface.co/datasets/hf-audio/ztts2_eval_mini)

In one published four-core Xeon test, Supertonic 3 at five steps
generated speech at 5.1 times real-time versus 1.8 for Kokoro ONNX.
Published relative speed changes with the implementation and hardware.
A separate RTX 5090 test reported about 2.0 GB of GPU allocations for
OmniVoice with the default voice and 2.6 GB for VibeVoice Realtime 0.5B.
Those results do not establish headroom alongside Whisper on the RTX 3050.

- [Xeon CPU comparison](https://github.com/gauravvij/kokoro-tts-vs-supertonic-3-tts/blob/main/new_blog_post.md)
- [Windows RTX 5090 benchmark](https://github.com/5uck1ess/tts-bench/blob/master/docs/results.md#windows-desktop--rtx-5090-cuda)

The public blind-listening arena ordered the tested voices as Kokoro,
Supertonic 3, OmniVoice, VibeVoice Realtime, then Piper. This measures
preference for the arena clips. It does not score Lili specifically or
establish how a narration sounds over a full chapter.

Supertonic 3 is the first candidate for Slovak documents. Compare it with
Lili and OmniVoice on the same prose and technical text. Keep VibeVoice
Realtime as an English long-form candidate against Kokoro. Evaluate
pronunciation, omissions and repeats, pacing, pauses, voice consistency
across paragraphs, and the time until playback can begin.

- [Public listening-arena ranking](https://5uck1ess-tts-arena.hf.space/api/ranking?mode=open)
- [Supertonic 3 comparison samples](https://supertonic3.github.io/)
- [OmniVoice samples including Slovak](https://zhu-han.github.io/omnivoice/)
- [VibeVoice Realtime long-form documentation](https://github.com/microsoft/VibeVoice/blob/main/docs/vibevoice-realtime-0.5b.md)

### First-release voice choices

Keep Piper Lili for short Slovak responses and Kokoro for English.
Evaluate Supertonic 3 in the first release as the default for Slovak
chapters. Compare it with Lili on the same Slovak prose and technical
text. Use the chapter default after listening confirms it is suitable.

## First-release acceptance checks

- Highlight text in a supported Windows application, invoke the read-aloud
  shortcut, and confirm that the selected text is read and can be stopped
  from the keyboard. Exercise the explicit clipboard route as well.
- Paste a chapter and open a chapter from TXT or Markdown. One chosen voice
  stays in use across paragraphs. Pause and resume playback, then export a
  playable MP3 with the full chapter in order.
- Add a compatible synthesis model, test it, use a voice from it, and
  remove it. Record the actual compute backend and handle a setting that
  refers to a removed model. Check Supertonic 3 with Slovak prose and
  technical text alongside the working Lili baseline.
- Start dictation during GPU chapter generation. Transcription takes
  priority. The forced GPU job waits and resumes without losing position.
  In Auto, verify the CPU path wherever the selected model supports it.
- Test local transcription and synthesis with a suitable app key,
  a missing key, a wrong-scope key, and a revoked key. Confirm Local
  Dictation and Bridge use distinct automatically provisioned credentials
  and that the paired Note's existing workflows still succeed.
- Confirm external applications on this PC can call the authorized
  loopback service. LAN HTTPS exposure and remote-client capacity tests
  belong to the later LAN release.

## Acceptance evidence

Record the Windows applications exercised, time to first usable audio
with each tested backend, peak GPU memory, concurrent transcription
latency, and listening results. Use public benchmarks to prioritize
candidates and evaluate the chapter voice on the target PC.

## Later releases

- Add full-document reading. Consider pronunciation settings for units,
  acronyms, dates, and part numbers.
- Add an opt-in LAN HTTPS listener on approved networks for a phone or
  second PC. Use the same scoped API and show the LAN address in the API
  tab when available. Protect local dictation with per-key rate limits,
  input size or duration limits, timeouts, and a shared concurrency limit.
  Evaluate one concurrent network job as a starting point.
