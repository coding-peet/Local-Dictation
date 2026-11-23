#pragma once

#include "button_gesture.h"

enum class AquariumScene : uint8_t { Closed, Opening, Ready, Feeding, Current, Travel, Hiding, Peek };
enum class AquariumReef : uint8_t { Anemone, TorchCoral };

constexpr uint32_t aquariumDuration(AquariumScene scene) {
  switch (scene) {
    case AquariumScene::Opening: return 2190;
    case AquariumScene::Feeding: return 3700;
    case AquariumScene::Current: return 5700;
    case AquariumScene::Travel: return 2400;
    case AquariumScene::Hiding: return 2490;
    case AquariumScene::Peek: return 850;
    case AquariumScene::Ready: return 15000;
    case AquariumScene::Closed: return 0;
  }
  return 0;
}

struct AquariumState {
  AquariumScene scene = AquariumScene::Closed;
  AquariumReef reef = AquariumReef::Anemone;
  uint32_t since = 0;

  constexpr void open(uint32_t now) {
    reef = AquariumReef::Anemone;
    nextAction_ = pendingActions_ = 0;
    enter(AquariumScene::Opening, now);
  }

  constexpr bool upper(ButtonAction action, uint32_t now) {
    if (action == ButtonAction::Double) return schedule(AquariumScene::Hiding, now);
    return action == ButtonAction::Single && schedule(AquariumScene::Feeding, now);
  }

  constexpr bool lower(ButtonAction action, uint32_t now) {
    if (action == ButtonAction::Double) return schedule(AquariumScene::Travel, now);
    return action == ButtonAction::Single && schedule(AquariumScene::Current, now);
  }

  constexpr void advance(uint32_t now, bool gesturePending = false) {
    if (now - since < aquariumDuration(scene)) return;
    switch (scene) {
      case AquariumScene::Travel:
        reef = reef == AquariumReef::Anemone ? AquariumReef::TorchCoral : AquariumReef::Anemone;
        [[fallthrough]];
      case AquariumScene::Opening:
      case AquariumScene::Feeding:
      case AquariumScene::Current:
        if (pendingActions_) {
          const auto next = actions_[nextAction_];
          nextAction_ = (nextAction_ + 1) % kActionCapacity;
          --pendingActions_;
          enter(next, now);
        } else {
          enter(AquariumScene::Ready, now);
        }
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
  static constexpr uint8_t kActionCapacity = 8;
  AquariumScene actions_[kActionCapacity]{};
  uint8_t nextAction_ = 0;
  uint8_t pendingActions_ = 0;

  constexpr bool acceptsActions() const {
    return scene != AquariumScene::Closed && scene != AquariumScene::Hiding && scene != AquariumScene::Peek;
  }

  constexpr bool schedule(AquariumScene action, uint32_t now) {
    if (!acceptsActions() || pendingActions_ == kActionCapacity) return false;
    if (scene == AquariumScene::Ready) {
      enter(action, now);
    } else {
      actions_[(nextAction_ + pendingActions_) % kActionCapacity] = action;
      ++pendingActions_;
    }
    return true;
  }

  constexpr void enter(AquariumScene next, uint32_t now) {
    if (next == AquariumScene::Hiding) nextAction_ = pendingActions_ = 0;
    scene = next;
    since = now;
  }
};
