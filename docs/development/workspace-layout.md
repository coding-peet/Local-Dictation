# Workspace layout

## Locations on this PC

~~~text
C:\AI\
  repos\
    Local-Dictation\            Git checkout: all editable project source
      server\
      docs\
      .git\
  build\
    Local-Dictation\            Generated build/test output
  LocalDictation\
    app\                       Published Windows executable and dependencies
    data\
      review\                  Protected pending Review items
    logs\                      Client operational diagnostics
  PalaNote-ASR\                 Existing deployed backend
    .venv\
    models\
    logs\
~~~

The source checkout and installed application are separate sibling trees.
Do not launch a development binary accidentally in place of the installed
client. Build/publish scripts must use explicit output paths.

The native executable is published in the app directory. Corporate execution
policy currently blocks launch; native acceptance is pending. See validation.md.


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

Publish the Windows client to C:\AI\LocalDictation\app.

Keep settings and pending reviews in the data tree, so replacing application
files does not overwrite user data. A future update must preserve data and
must not replace a running executable without a controlled handover.


## Retention policy

Peter selected only outstanding Review items with temporary audio.

- Successful ordinary dictation remains in its destination application.
- Pending Review text survives a client restart until explicitly dismissed.
- Copying a Review item does not automatically delete it.
- Live audio uses bounded rolling RAM when no later pass needs the beginning.
- An after-recording primary or an enabled verification pass needs complete
  audio in bounded RAM until the last required pass finishes.
- Recordings and normal completed transcripts are not archived.
- Operational logs do not contain transcript text or audio by default.

The native client protects pending Review with current-user Windows DPAPI.
The browser concept remains a design artifact and writes no real user data.


## Existing deployment

The existing endpoint is http://127.0.0.1:8123.
The native client session API is separate at http://127.0.0.1:48125.

Repository initialization does not change its model IDs, weights, startup
shortcut, process, or TypeWhisper profiles. Test new server behavior on an
isolated loopback listener before promoting it.
