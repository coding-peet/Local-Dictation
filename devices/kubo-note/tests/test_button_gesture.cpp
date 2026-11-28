#include "../pala_note/src/app/button_gesture.h"
#include "../pala_note/src/app/prompt_input.h"

namespace {
constexpr uint8_t kHomeClickCount = 3;

constexpr bool distinguishesOneTwoAndThreeHomeClicks() {
  ButtonGesture lower;
  lower.edge(true, 100, kHomeClickCount);
  lower.edge(false, 150, kHomeClickCount);
  if (lower.poll(509, kHomeClickCount) != ButtonAction::None || !lower.pending()) return false;
  if (lower.poll(510, kHomeClickCount) != ButtonAction::Single || lower.pending()) return false;

  lower.edge(true, 600, kHomeClickCount);
  lower.edge(false, 650, kHomeClickCount);
  lower.edge(true, 750, kHomeClickCount);
  lower.edge(false, 800, kHomeClickCount);
  if (lower.poll(1159, kHomeClickCount) != ButtonAction::None) return false;
  if (lower.poll(1160, kHomeClickCount) != ButtonAction::Double) return false;

  lower.edge(true, 1200, kHomeClickCount);
  lower.edge(false, 1250, kHomeClickCount);
  lower.edge(true, 1350, kHomeClickCount);
  lower.edge(false, 1400, kHomeClickCount);
  lower.edge(true, 1510, kHomeClickCount);
  if (lower.edge(false, 1550, kHomeClickCount) != ButtonAction::None) return false;
  if (lower.poll(1564, kHomeClickCount) != ButtonAction::Triple) return false;
  return lower.poll(2000, kHomeClickCount) == ButtonAction::None && !lower.pending();
}

constexpr bool holdClearsEarlierTapsAndDoesNotBecomeOneOnRelease() {
  ButtonGesture lower;
  lower.edge(true, 100, kHomeClickCount);
  lower.edge(false, 150, kHomeClickCount);
  lower.edge(true, 250, kHomeClickCount);
  if (lower.poll(569, kHomeClickCount) != ButtonAction::None) return false;
  if (lower.poll(570, kHomeClickCount) != ButtonAction::Hold) return false;
  lower.edge(false, 700, kHomeClickCount);
  return lower.poll(1200, kHomeClickCount) == ButtonAction::None && !lower.pending();
}

constexpr bool clearingChordDropsAnIncompleteTapSequence() {
  ButtonGesture lower;
  lower.edge(true, 100, kHomeClickCount);
  lower.edge(false, 145, kHomeClickCount);
  lower.poll(200, kHomeClickCount);
  if (!lower.pending()) return false;
  lower.reset();
  return lower.poll(600, kHomeClickCount) == ButtonAction::None && !lower.pending();
}

constexpr bool recordingReleaseDoesNotConsumeBufferedManualSync() {
  ButtonGesture upper;
  upper.edge(true, 100, kHomeClickCount);
  if (upper.poll(420, kHomeClickCount) != ButtonAction::Hold) return false;
  upper.edge(false, 1000, kHomeClickCount);
  upper.edge(true, 1060, kHomeClickCount);
  upper.edge(false, 1100, kHomeClickCount);
  upper.edge(true, 1200, kHomeClickCount);
  upper.edge(false, 1240, kHomeClickCount);
  upper.edge(true, 1340, kHomeClickCount);
  upper.edge(false, 1380, kHomeClickCount);
  // Replay the timestamped edges after several blocking screen updates.
  if (upper.poll(2400, kHomeClickCount) != ButtonAction::Triple) return false;
  return upper.poll(2600, kHomeClickCount) == ButtonAction::None && !upper.pending();
}

struct PromptTestInput {
  struct Edge { uint32_t at; bool down; } edges[4]{};
  unsigned read = 0, write = 0;
  bool replayDown = false, rawDown = false, stableDown = false;
  uint32_t rawAt = 0;
  ButtonGesture gesture;
};

constexpr bool preservedPostVisibilityInput() {
  PromptTestInput button;
  button.read = 2;
  button.write = 1;
  button.edges[2] = {UINT32_MAX - 12, true};
  button.edges[3] = {0, false};
  button.edges[0] = {24, true};
  button_prompt::discardThrough(button, 0);
  if (button.read != 0 || button.replayDown || !button.edges[button.read].down) return false;

  // A press exactly at the boundary might still be debouncing when the
  // panel returns. It belongs to the old screen until its release.
  PromptTestInput atBoundary;
  atBoundary.rawAt = 90;
  atBoundary.rawDown = true;
  button_prompt::discardThrough(atBoundary, 90);
  return atBoundary.replayDown && atBoundary.stableDown;
}

static_assert(preservedPostVisibilityInput());
static_assert(!button_prompt::retryDue(400, UINT32_MAX - 99));
static_assert(button_prompt::retryDue(900, UINT32_MAX - 99));
static_assert(recordingReleaseDoesNotConsumeBufferedManualSync());
static_assert(distinguishesOneTwoAndThreeHomeClicks());
static_assert(holdClearsEarlierTapsAndDoesNotBecomeOneOnRelease());
static_assert(clearingChordDropsAnIncompleteTapSequence());
}
