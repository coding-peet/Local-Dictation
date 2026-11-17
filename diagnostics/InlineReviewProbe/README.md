# Compact inline Review probe

This diagnostic exercises the real WPF compact Review panel, AppController,
protected Review storage, and routed WPF input with synthetic transcripts.

External text delivery is replaced by a small test double. It does not capture
microphone audio, contact either ASR API, generate native keyboard input, or
change the installed client.

## Build and run

Run from the repository root with the existing .NET 10 Windows Desktop SDK:

~~~powershell
& C:\AI\tools\dotnet-10.0.403\dotnet.exe build `
    diagnostics\InlineReviewProbe\InlineReviewProbe.csproj `
    --artifacts-path C:\AI\build\Local-Dictation-inline\dotnet

& C:\AI\tools\dotnet-10.0.403\dotnet.exe `
    C:\AI\build\Local-Dictation-inline\dotnet\bin\InlineReviewProbe\debug\InlineReviewProbe.dll `
    --report C:\Git\Local-Dictation\inline-probe\result.json
~~~

The commands write build files and a synthetic JSON report outside the checkout.
The probe creates a temporary, test-owned protected data directory and removes
that directory after its controllers close. It also renders the compact editing
panel beside the report as inline-review.png.

## Checks

- Direct entry by F2, normal-text click, and typed punctuation.
- A single disputed-phrase click retains the existing alternative flow.
- Enter never approves or inserts while the full-text editor has focus.
- Exact Unicode, whitespace, native paste, and protected reload after restart.
- Common-word deletion without changing original transcripts or earlier choices.
- Stable caret, selection, keyboard focus, and native Undo during 80 ms refresh.
- Explicit whole-draft approval and a durable, read-only insertion receipt.
- Recoverable messages for unsupported line breaks, lost destinations, and
  failed protected saving. Unsaved text must not be inserted.
- Accessible edit controls and a rendered compact panel.

These checks do not establish microphone behavior, ASR quality, native physical
double-click timing, or insertion compatibility with real external applications.
Codex insertion still requires Peter's manual acceptance test.
