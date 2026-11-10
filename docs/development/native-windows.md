# Native Windows development

## Development-machine prerequisites

- Windows x64 with .NET 10 Windows Desktop runtime.
- .NET 10 SDK. The verified portable SDK is 10.0.403 at
  C:\AI\tools\dotnet-10.0.403.
- Python 3.14 and the existing tested CUDA/Torch/Transformers environment.
- NVIDIA RTX 3050 Laptop 4 GiB on this machine.
- Original model files under C:\AI\PalaNote-ASR\models.

The candidate API uses a separate environment. A .pth entry points to the
existing environment's immutable Torch/Transformers packages. New HTTP and
WebSocket dependencies are installed into the candidate environment only.

~~~powershell
.\scripts\Setup-CandidateServer.ps1
~~~

This command creates C:\AI\build\Local-Dictation\server-env if needed,
writes its shared-package reference, and installs server/requirements-api.txt
using the machine's configured pip source. It does not install packages into
the deployed ASR environment.

For a different machine, first prepare a compatible environment using
server/requirements-lock.txt and its CUDA/Python requirements. The shared
environment shortcut assumes the same Python interpreter/ABI and existing
tested packages. It is not a portable environment export.


## Build outside the checkout

~~~powershell
.\scripts\Build.ps1
~~~

The default SDK path is explicit. Global PATH and Windows startup are unchanged.
You can override -DotnetPath, -BuildRoot and -AppDirectory.

The SDK's artifacts layout places bin/obj under the external build root.
Publish includes the EXE, managed assemblies, NAudio dependencies and runtime
configuration. Keep these files together in the app folder.

This is a framework-dependent Windows x64 build. It requires the .NET 10
Windows Desktop runtime; it is not a self-contained installer.


## Candidate API lifecycle

~~~powershell
.\scripts\Start-CandidateServer.ps1
~~~

This script refuses port 8123 and any already occupied candidate port.
Warm-up can take one to two minutes. Health responds while the runtime is
Preparing; wait for ready=true. Inspect readiness:

~~~powershell
Invoke-RestMethod http://127.0.0.1:48125/health
~~~

Health distinguishes readiness, admission/GPU phase, loaded model and allocated
GPU memory. CUDA contexts can retain device memory even when model weights and
allocator reservations are released.

Stop the idle candidate:

~~~powershell
.\scripts\Stop-CandidateServer.ps1
~~~

The stop script verifies the service, protocol, process ID and loopback listener.
It refuses a busy server or the production port. It terminates only the verified
idle candidate process and waits for it to exit.

The deployed TypeWhisper backend on 8123 is separate and has not been replaced.
Both processes use the same physical GPU. Avoid simultaneous inference through
both APIs. Keep TypeWhisper's shortcut inactive during Local Dictation acceptance.


## Native acceptance and execution check

Both the development EXE and Peter's manual launch of the published app-folder
EXE triggered Defender event 1121 for this ASR rule:

~~~text
01443614-CD74-433A-B99E-2ECDC07BFC25
~~~

Read-only Get-MpPreference and the corresponding Policies registry value both
reported action 6 (Warn) on 2026-11-10. Event 1121 alone did not distinguish this
from Block mode. This rule concerns unknown executable trust/prevalence/age;
relocating the package to Program Files has not been established as a fix.

Use the built-in approval flow when Windows Security offers Unblock:

1. Peter selects Unblock in the Windows Security prompt.
2. Complete any required administrator authentication directly in Windows.
3. Retry the same EXE. For the first check use --no-hotkey.

Microsoft documents that Warn-mode Unblock lasts 24 hours. Defender platform
4.18.26060 and later require administrator approval; the recorded machine version
is 4.18.26100.4. Unblock is temporary suppression, not a permanent exclusion.
The agent does not automate Windows Security or authentication dialogs.

Peter reported confirming Unblock. The subsequent normal agent launch at 17:27
(Europe/Bratislava) was still denied by the same ASR rule. Peter then confirmed
that direct launch and Run as administrator also failed. Subsequent event 1121
records corroborated the manual attempts. File ACLs grant Users read/execute.
No effective allowance or successful native launch has been confirmed. The next
step is approval/deployment of this specific client through the company process.

If the prompt is unavailable or approval is rejected, report that specific result
and use the normal company approval process. Do not change policy, add exclusions,
create an ad-hoc trust certificate, or use an alternative host to evade the rule.

References:

- [Microsoft ASR rule modes](https://learn.microsoft.com/en-us/defender-endpoint/attack-surface-reduction-rules-overview#modes-for-asr-rules)
- [Executable trust rule](https://learn.microsoft.com/en-us/defender-endpoint/attack-surface-reduction-rules-reference#block-executable-files-from-running-unless-they-meet-a-prevalence-age-or-trusted-list-criterion)

Manual acceptance checklist:

1. Launch LocalDictation.exe --no-hotkey.
2. Check wide and narrow Settings/Review windows and display scaling.
3. Select the microphone and use Record to Review. Confirm real level changes,
   Stop, Escape, unplug/error behavior and the 10-minute normal stop.
4. Quit TypeWhisper, launch without --no-hotkey, and recheck Ctrl+Shift.
5. Test a controlled native text field first. Dictate successive corrections,
   then move the cursor, type manually, switch fields and switch windows.
   Confirm delivery stops and full text remains in Review.
6. Check read-only/password/elevated/unsupported targets fall back to Review.
7. Check verification alternatives, empty choices, custom drafts, Undo,
   repeated Rebuild, saved manual draft restoration and exact plain-text Copy.
8. Restart the app with pending Review; confirm recovery and explicit Dismiss.
9. Test Codex insertion manually. Agent-driven Codex UI automation is excluded.

Native tests cannot be inferred from a successful compiler or core test run.


## Local model lifecycle

The initial catalog references pinned KInIT Turbo Slovak and OpenAI Large-v3.
The public importer supports original built-in Whisper safetensors only.
It inspects model/processor/tokenizer/generation metadata and exact file sizes.

Downloads go to a staging directory. Complete configuration, tokenizer files,
safetensors headers/shards and pinned file lengths are validated before atomic
publication. A failed registry write rolls back the registration and newly published
directory; same-revision repair restores the prior incomplete managed directory.
Healthy installed models are preserved. This is in-process rollback; durable
crash journaling/startup reconciliation is not implemented.

Managed models require their matching pinned manifest. Missing or invalid
identity clears runtime/quality evidence and enables repair.

Registry restoration checks installation completeness. File changes invalidate
the cached validation. A deleted/truncated weight or missing tokenizer disables
eligibility. Historical quality evidence does not make an incomplete installation
usable.

Language capability comes from validated generation metadata. English-only
checkpoints use English-only generation without unsupported language/task
arguments. A verifier must support the effective primary language. An auto-language
multilingual primary cannot silently select a Slovak-only verifier.

A successful runtime test enables verification eligibility. It does not establish
transcription quality or live readiness. The tested imported Tiny checkpoint is
installed as an additional after-recording option, not promoted as a Slovak
quality recommendation.


## Audio and text ownership

PCM is signed little-endian 16-bit, mono, 16 kHz. Maximum WebSocket frame:
64 KiB. Maximum recording: 600 seconds.

Live buffering is bounded to 32 seconds. Full audio is retained only for an
after-recording primary or verification. A duration/buffer limit stops normally,
preserves accepted audio and completes transcription. Further frames are ignored
after that preserving stop.

Cancellation sets the cooperative model event and joins inference before unload
and admission release. Request/task cancellation follows the same rule.
Worker exceptions are sanitized after leaving their original traceback scope,
so discarded generation frames cannot retain CUDA tensors through cleanup.

The client records a start-time destination generation and window. Keyboard,
mouse and focus events invalidate it, including while UIA capture is pending.
Insertion checks field identity, expected full text and selection. Only the changed
owned suffix is selected/replaced. No focus restoration, Enter or guessed
Backspace sequence is used. Elevated destination processes remain Review-only
even if the client accidentally inherits an elevated token.

Third-party accessibility providers can stall or implement ranges differently.
Those outcomes use Review; real application acceptance is still required.
