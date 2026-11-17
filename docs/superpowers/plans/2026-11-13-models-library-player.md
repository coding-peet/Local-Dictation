# Models, unified History and compact player

Goal: Simplify Models, merge Pending Review into History, and shrink playback.

User requirements: Two visible Models blocks (installed management and adding),
plain copy, test the selected installed model below its table, add safe Remove.
History keeps unfinished text as a status, replacing the duplicate Pending Review
navigation/opener. Put text actions above playback; use a shorter player with a
faint underlay and a meaningful silence/speech energy scale rather than maxima.

Keep protected pending persistence independent from the 20-audio history limit.
Merge only the presentation, deduplicate IDs, preserve edits and final receipts.
Rows without audio remain reviewable. All Open Review actions use the working
History route. No desktop, playback, real inference or actual model removal.
Shared TypeWhisper weights must be preserved; only app-owned managed installs
can be removed after explicit future user action and confirmation.

Tasks: safe backend removal/client contract; installed/add Models UI; pure
History projection + tests and navigation; compact fixed-RMS waveform + tests;
windowless render/review; Help/requirements; publish candidate; comment/commit/
non-force push with remote HEAD and clean tree verification.

Checkout: C:\Git\Local-Dictation\repo, feature/native-windows.
Base: f5910eba3bfe151c615aecfa1b66c91a5baba988.
