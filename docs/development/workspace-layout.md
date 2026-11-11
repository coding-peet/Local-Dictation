# Workspace layout

## Locations on this PC

~~~text
C:\Git\Local-Dictation\
  repo\                        Git checkout and linked native-windows worktree
  app\                         Published Windows executable and dependencies

C:\AI\
  build\
    Local-Dictation\            Generated build/test output
  LocalDictation\
    data\
      review\                  Protected pending Review items
      history\                 Latest 20 protected transcripts and WAV recordings
    logs\                      Client operational diagnostics
  PalaNote-ASR\                 Existing deployed backend
    .venv\
    models\
    logs\
~~~

The source checkout and installed application are separate sibling trees.
Do not launch a development binary accidentally in place of the installed
client. Build/publish scripts must use explicit output paths.

The native executable has run from the app directory. Later review fixes are
published separately as candidates until a controlled update. The latest review's
native delivery probe was interrupted; full application acceptance remains
separate from compilation and synthetic tests. See README.md for current status.

Source and generated application files can be restored from a fresh checkout;
see [rebuild and recovery](rebuild-and-recovery.md). External models and private
runtime data require their own preservation and are not restored by Git.


## Canonical source

Edit application code, tests, documentation, prototypes, and packaging
scripts in the Git checkout.

The Python files under server were imported from the working batch backend.
The deployed copy under C:\AI\PalaNote-ASR remains in place so existing
dictation continues working. Future deployment will copy reviewed source
from this repository into that runtime.

Do not move model weights, the Python environment, caches, recordings, or
operational logs into Git. Development candidates must reuse the existing
model files through an explicit model-root configuration rather than
duplicating them.


## Build and publish

Use C:\AI\build\Local-Dictation for generated compiler output and test
artifacts. The SDK artifacts layout separates project/configuration output;
override Build.ps1 -BuildRoot when building another worktree concurrently.

Publish the Windows client to C:\Git\Local-Dictation\app.

Keep settings and pending reviews in the data tree, so replacing application
files does not overwrite user data. A future update must preserve data and
must not replace a running executable without a controlled handover.


## Retention policy

On 2026-11-11 Peter changed retention to the latest 20 transcripts and their
recordings. Pending Review remains a separate recovery store.

- Successful ordinary dictation remains in its destination application.
- Pending Review text survives a client restart until explicitly dismissed.
- Copying a Review item does not automatically delete it.
- Live audio uses bounded rolling RAM when no later pass needs the beginning.
- An after-recording primary or an enabled verification pass needs complete
  audio in bounded RAM until the last required pass finishes.
- History saves transcripts and original WAV recordings protected for the current
  Windows user. Adding a 21st item removes the oldest history transcript and audio.
- History offers playback, text copy, Review, explicit WAV export, and deletion.
- Exported WAV files are ordinary files in the destination chosen by the user.
- Operational logs do not contain transcript text or audio by default.

The native client protects pending Review and history with current-user DPAPI.
The browser concept remains a design artifact and writes no real user data.


## Existing deployment

The existing endpoint is http://127.0.0.1:8123.
The native client session API is separate at http://127.0.0.1:48125.

Repository initialization does not change its model IDs, weights, startup
shortcut, process, or TypeWhisper profiles. Test new server behavior on an
isolated loopback listener before promoting it.
