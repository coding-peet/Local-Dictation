# Audio and banner diagnostic

Read Program.cs and select a mode before launching this helper.

The default mode opens the system default microphone, observes capture for a
short interval, and discards the sample. It does not connect to the ASR API.
Do not run it as a read-only or synthetic check without a microphone test task.

The render modes use synthetic text and do not start microphone capture:

~~~powershell
.\AudioProbe.exe --render-compact-review C:\AI\build\Local-Dictation\compact-review.png
~~~

Other available render switches are --render-banner, --render-review-banner,
and --render-review. They create native WPF previews, not recognition results.
Build/publish this diagnostic outside the source checkout and keep generated
images or operational results outside Git unless explicitly chosen as synthetic
design references.
