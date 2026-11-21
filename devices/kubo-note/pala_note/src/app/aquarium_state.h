#pragma once

#include "button_gesture.h"

enum class AquariumScene { Closed, Opening, Ready, Feeding, Current, Hiding, Peek };

constexpr uint32_t aquariumDuration(AquariumScene scene) {
  switch (scene) {
    case AquariumScene::Opening: return 2190;
    case AquariumScene::Feeding: return 3700;
    case AquariumScene::Current: return 5700;
    case AquariumScene::Hiding: return 2490;
    case AquariumScene::Peek: return 850;
    case AquariumScene::Ready: return 15000;
    case AquariumScene::Closed: return 0;
  }
  return 0;
}

struct AquariumState {
  AquariumScene scene = AquariumScene::Closed;
  uint32_t since = 0;

  constexpr void open(uint32_t now) { enter(AquariumScene::Opening, now); }

  constexpr void upper(ButtonAction action, uint32_t now) {
    if (action == ButtonAction::Double && scene != AquariumScene::Closed &&
        scene != AquariumScene::Hiding && scene != AquariumScene::Peek) {
      enter(AquariumScene::Hiding, now);
    } else if (action == ButtonAction::Single && scene == AquariumScene::Ready) {
      enter(AquariumScene::Feeding, now);
    }
  }

  constexpr bool lower(ButtonAction action, uint32_t now) {
    if (action != ButtonAction::Single || scene != AquariumScene::Ready) return false;
    enter(AquariumScene::Current, now);
    return true;
  }

  constexpr void advance(uint32_t now, bool gesturePending = false) {
    if (now - since < aquariumDuration(scene)) return;
    switch (scene) {
      case AquariumScene::Opening:
      case AquariumScene::Feeding:
      case AquariumScene::Current:
        enter(AquariumScene::Ready, now);
        break;
      case AquariumScene::Ready:
        if (!gesturePending) enter(AquariumScene::Hiding, now);
        break;
      case AquariumScene::Hiding: enter(AquariumScene::Peek, now); break;
      case AquariumScene::Peek: enter(AquariumScene::Closed, now); break;
      case AquariumScene::Closed: break;
    }
  }

 private:
  constexpr void enter(AquariumScene next, uint32_t now) {
    scene = next;
    since = now;
  }
};
