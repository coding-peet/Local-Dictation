"""Extract the current Note handlers verbatim for the host-side fixtures."""

import argparse
from pathlib import Path


def handlers(source):
    first = "static void sendFileByNum("
    end = "static bool positiveFormValue(const char* name, int& value) {"
    if source.count(first) != 1 or source.count(end) != 1:
        raise ValueError("Archive handler boundaries changed. Update the host harness.")
    start = source.index(first)
    stop = source.index(end, start)
    block = source[start:stop]
    if block.count("static void handleRecorderArchive()") != 1:
        raise ValueError("Archive handler moved. Update the host harness.")
    form = "#if PALA_STAGE3\nstatic void handleArchiveMonthDeletion("
    next_handler = "static void handleNoteDelete()"
    if source.count(form) != 1 or source.count(next_handler) != 1:
        raise ValueError("Archive confirmation boundaries changed. Update the host harness.")
    return block + source[source.index(form):source.index(next_handler)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("--recording-screen",type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    source = (root / "pala_note/src/app/network.cpp").read_text(encoding="utf-8")
    args.output.write_text(handlers(source), encoding="utf-8", newline="\n")
    if args.recording_screen:
        ui=(root / "pala_note/src/app/ui.cpp").read_text(encoding="utf-8")
        start=ui.index("void paintRecordingScreen(")
        stop=ui.index("void paintSavingScreen()",start)
        args.recording_screen.write_text(ui[start:stop],encoding="utf-8",newline="\n")


if __name__ == "__main__":
    main()
