# Named-menu layout implementation plan

Selected design: variant 2, Zachovane menu, from the History layout proposal.

Keep named navigation, narrow it to about 160 DIPs, and reduce the ordinary
header to 64 DIPs. Help moves to the header; empty status space and duplicate
Close disappear. Keep actionable errors, autosave feedback and recovery usable.

History uses a short date/duration row, status only when useful, and at most
two preview lines. The selected text gets more height. Review/Copy/More belong
to the detail; language/export/delete remain reachable from More. Playback
stays at the bottom across both History columns and retains existing behavior.

Validate real WPF chrome and History content without creating a window or
enumerating devices. Cover normal/compact widths, long text/full history,
named navigation, error/recovery rows and media/text-only lifetime. Keep the
shared MD3 templates/48-DIP hit targets. No desktop control, mic or playback.

Checkout C:\Git\Local-Dictation\repo, feature/native-windows.
Base HEAD: 0c22c86d92deff3c63055593bc1cf48088698032.
