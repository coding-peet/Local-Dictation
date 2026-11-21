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
  state.lower(6142);
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
  return state.scene == AquariumScene::Hiding;
}

static_assert(preservesQueuedButtonGestures());
static_assert(aquariumDoubleNeverFeeds());
static_assert(aquariumTimeline());
}
