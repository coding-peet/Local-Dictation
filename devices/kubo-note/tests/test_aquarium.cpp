#include "../pala_note/src/app/aquarium_state.h"

namespace {
constexpr bool preservesQueuedButtonGestures() {
  ButtonGesture tap;
  if (tap.edge(true, 100, 3) != ButtonAction::None || tap.edge(false, 150, 3) != ButtonAction::None) return false;
  if (tap.poll(509, 3) != ButtonAction::None || tap.poll(510, 3) != ButtonAction::Single) return false;

  tap.reset();
  tap.edge(true, 100, 3);
  tap.edge(false, 150, 3);
  tap.edge(true, 290, 3);
  if (tap.edge(false, 340, 3) != ButtonAction::None || tap.poll(699, 3) != ButtonAction::None) return false;
  if (tap.poll(700, 3) != ButtonAction::Double) return false;

  tap.reset();
  tap.edge(true, 100, 3);
  tap.edge(false, 150, 3);
  tap.edge(true, 290, 3);
  tap.edge(false, 340, 3);
  if (tap.edge(true, 699, 3) != ButtonAction::None || tap.edge(false, 745, 3) != ButtonAction::None) return false;
  if (tap.poll(760, 3) != ButtonAction::Triple) return false;

  tap.reset();
  tap.edge(true, 20, 3);
  tap.edge(false, 24, 3);
  if (tap.poll(450, 3) != ButtonAction::None) return false;
  tap.edge(true, 500, 3);
  if (tap.poll(820, 3) != ButtonAction::Hold || tap.poll(900, 3) != ButtonAction::None) return false;
  tap.edge(false, 912, 3);
  return tap.poll(1400, 3) == ButtonAction::None && !tap.pending();
}

constexpr bool aquariumDoubleNeverFeeds() {
  ButtonGesture tap;
  tap.edge(true, 0xfffffff0, 2);
  tap.edge(false, 20, 2);
  if (tap.poll(379, 2) != ButtonAction::None) return false;
  tap.edge(true, 379, 2);
  tap.edge(false, 421, 2);
  return tap.poll(440, 2) == ButtonAction::Double && tap.poll(1000, 2) == ButtonAction::None;
}

constexpr bool aquariumTimeline() {
  AquariumState state;
  state.open(250);
  if (state.scene != AquariumScene::Opening) return false;
  state.advance(2439);
  if (state.scene != AquariumScene::Opening) return false;
  state.advance(2440);
  if (state.scene != AquariumScene::Ready) return false;
  state.upper(ButtonAction::Single, 2441);
  if (state.scene != AquariumScene::Feeding) return false;
  state.advance(6141);
  state.lower(ButtonAction::Single, 6142);
  if (state.scene != AquariumScene::Current) return false;
  state.advance(11842);
  state.advance(26841);
  if (state.scene != AquariumScene::Ready) return false;
  state.advance(26842, true);
  if (state.scene != AquariumScene::Ready) return false;
  state.advance(26842);
  if (state.scene != AquariumScene::Hiding) return false;
  state.advance(29332);
  if (state.scene != AquariumScene::Peek) return false;
  state.advance(30182);
  if (state.scene != AquariumScene::Closed) return false;
  state.open(31000);
  state.upper(ButtonAction::Double, 31042);
  if (state.scene != AquariumScene::Opening) return false;
  state.advance(31000 + aquariumDuration(AquariumScene::Opening));
  return state.scene == AquariumScene::Hiding;
}

constexpr bool lowerTapDuringDisplayRefresh() {
  AquariumState state;
  state.open(0);
  state.advance(2190);
  ButtonGesture gesture;
  gesture.edge(true, 2400, 1);
  if (gesture.poll(2420, 1) != ButtonAction::None) return false;

  // The panel held up the loop. The release still carries its original time.
  if (gesture.edge(false, 2460, 1) != ButtonAction::None) return false;
  state.lower(gesture.poll(2850, 1), 2850);
  return state.scene == AquariumScene::Current && state.since == 2850;
}

constexpr bool lowerHoldDoesNotStartCurrent() {
  AquariumState state;
  state.open(0);
  state.advance(2190);
  ButtonGesture gesture;
  gesture.edge(true, 2400, 1);
  const auto hold = gesture.edge(false, 2800, 1);
  if (hold != ButtonAction::Hold) return false;
  state.lower(hold, 3100);
  state.lower(gesture.poll(3100, 1), 3100);
  return state.scene == AquariumScene::Ready;
}

static_assert(preservesQueuedButtonGestures());
static_assert(aquariumDoubleNeverFeeds());
constexpr bool busyAquariumKeepsEightActionsInOrder() {
  AquariumState state;
  state.open(100);
  const AquariumScene actions[] = {
    AquariumScene::Feeding, AquariumScene::Current, AquariumScene::Feeding,
    AquariumScene::Feeding, AquariumScene::Current, AquariumScene::Feeding,
    AquariumScene::Current, AquariumScene::Feeding
  };
  for (const auto action : actions) {
    if (action == AquariumScene::Feeding) state.upper(ButtonAction::Single, 200);
    else state.lower(ButtonAction::Single, 200);
  }
  // A ninth request must not replace anything already queued.
  state.lower(ButtonAction::Single, 201);
  uint32_t now = 100 + aquariumDuration(AquariumScene::Opening);
  for (const auto action : actions) {
    state.advance(now - 1);
    if (state.scene == AquariumScene::Ready) return false;
    state.advance(now);
    if (state.scene != action || state.since != now) return false;
    now += aquariumDuration(action);
  }
  state.advance(now);
  return state.scene == AquariumScene::Ready;
}

constexpr bool queuedGesturesStayDistinctWhenReplayedLate() {
  AquariumState state;
  state.open(0);
  ButtonGesture tap;
  constexpr uint32_t delivery = 2000;
  // Two separated clicks, followed by a real double click. Read them in
  // one pass using the original timestamps, long after the buttons went up.
  const uint32_t edges[] = {100, 150, 560, 610, 1050, 1100, 1200, 1250};
  for (uint8_t i = 0; i < 8; ++i) {
    state.upper(tap.edge(i % 2 == 0, edges[i], 2), delivery);
  }
  state.upper(tap.poll(delivery, 2), delivery);
  state.lower(ButtonAction::Single, delivery + 1);
  if (state.scene != AquariumScene::Opening) return false;

  uint32_t now = aquariumDuration(AquariumScene::Opening);
  state.advance(now);
  if (state.scene != AquariumScene::Feeding || state.since != now) return false;
  now += aquariumDuration(AquariumScene::Feeding);
  state.advance(now);
  if (state.scene != AquariumScene::Feeding || state.since != now) return false;
  now += aquariumDuration(AquariumScene::Feeding);
  state.advance(now);
  if (state.scene != AquariumScene::Hiding || state.since != now) return false;

  now += aquariumDuration(AquariumScene::Hiding);
  state.advance(now);
  if (state.scene != AquariumScene::Peek) return false;
  now += aquariumDuration(AquariumScene::Peek);
  state.advance(now);
  if (state.scene != AquariumScene::Closed) return false;
  state.open(now + 1);
  state.advance(now + 1 + aquariumDuration(AquariumScene::Opening));
  return state.scene == AquariumScene::Ready;
}

constexpr bool lowerDoubleClickTravelKeepsActionsAndCanReturn() {
  AquariumState state;
  state.open(0);
  uint32_t now = aquariumDuration(AquariumScene::Opening);
  state.advance(now);
  if (state.reef != AquariumReef::Anemone || state.scene != AquariumScene::Ready) return false;
  if (!state.lower(ButtonAction::Double, ++now) || state.scene != AquariumScene::Travel) return false;
  if (!state.upper(ButtonAction::Single, now + 1) || !state.lower(ButtonAction::Single, now + 2)) return false;
  state.advance(now + aquariumDuration(AquariumScene::Travel) - 1);
  if (state.reef != AquariumReef::Anemone) return false;
  now += aquariumDuration(AquariumScene::Travel);
  state.advance(now);
  if (state.reef != AquariumReef::TorchCoral || state.scene != AquariumScene::Feeding) return false;
  now += aquariumDuration(AquariumScene::Feeding);
  state.advance(now);
  if (state.scene != AquariumScene::Current) return false;
  if (!state.lower(ButtonAction::Double, now + 1)) return false;
  now += aquariumDuration(AquariumScene::Current);
  state.advance(now);
  if (state.reef != AquariumReef::TorchCoral || state.scene != AquariumScene::Travel) return false;
  now += aquariumDuration(AquariumScene::Travel);
  state.advance(now);
  if (state.reef != AquariumReef::Anemone || state.scene != AquariumScene::Ready) return false;
  state.lower(ButtonAction::Double, ++now);
  state.advance(now + aquariumDuration(AquariumScene::Travel));
  state.open(now + aquariumDuration(AquariumScene::Travel) + 1);
  return state.reef == AquariumReef::Anemone && state.scene == AquariumScene::Opening;
}

static_assert(lowerDoubleClickTravelKeepsActionsAndCanReturn());
static_assert(queuedGesturesStayDistinctWhenReplayedLate());
static_assert(busyAquariumKeepsEightActionsInOrder());
static_assert(aquariumTimeline());
static_assert(lowerTapDuringDisplayRefresh());
static_assert(lowerHoldDoesNotStartCurrent());
}
