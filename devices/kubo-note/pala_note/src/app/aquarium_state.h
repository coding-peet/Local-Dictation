#pragma once

#include <stdint.h>

enum class UpperAction { None, Single, Double, Triple, Hold };

class UpperGesture {
 public:
  constexpr void reset() { *this = UpperGesture{}; }

  constexpr bool pending() const { return taps_ || (!held_ && (rawDown_ || settledDown_)); }

  constexpr UpperAction edge(bool down, uint32_t at, uint8_t completeAt) {
    const UpperAction action = poll(at, completeAt);
    if (rawDown_ != down) {
      rawDown_ = down;
      rawAt_ = at;
    }
    return action;
  }

  constexpr UpperAction poll(uint32_t now, uint8_t completeAt) {
    if (settledDown_ != rawDown_ && now - rawAt_ >= kDebounceMs) {
      settledDown_ = rawDown_;
      if (settledDown_) {
        pressedAt_ = rawAt_;
      } else {
        if (!held_) {
          releasedAt_ = rawAt_;
          if (++taps_ == completeAt) return finishTaps();
        }
        held_ = false;
      }
    }
    if (settledDown_ && rawDown_ && !held_ && now - pressedAt_ >= kHoldMs) {
      held_ = true;
      taps_ = 0;
      return UpperAction::Hold;
    }
    // A press that started inside the window still counts when released later.
    if (taps_ && !settledDown_ && !rawDown_ && now - releasedAt_ >= kInterTapMs) {
      return finishTaps();
    }
    return UpperAction::None;
  }

 private:
  constexpr UpperAction finishTaps() {
    const uint8_t count = taps_;
    taps_ = 0;
    return count == 1 ? UpperAction::Single : count == 2 ? UpperAction::Double : UpperAction::Triple;
  }

  static constexpr uint32_t kDebounceMs = 14;
  static constexpr uint32_t kHoldMs = 320;
  static constexpr uint32_t kInterTapMs = 360;
  uint32_t rawAt_ = 0;
  uint32_t pressedAt_ = 0;
  uint32_t releasedAt_ = 0;
  uint8_t taps_ = 0;
  bool rawDown_ = false;
  bool settledDown_ = false;
  bool held_ = false;
};

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

  constexpr void upper(UpperAction action, uint32_t now) {
    if (action == UpperAction::Double && scene != AquariumScene::Closed &&
        scene != AquariumScene::Hiding && scene != AquariumScene::Peek) {
      enter(AquariumScene::Hiding, now);
    } else if (action == UpperAction::Single && scene == AquariumScene::Ready) {
      enter(AquariumScene::Feeding, now);
    }
  }

  constexpr void lower(uint32_t now) {
    if (scene == AquariumScene::Ready) enter(AquariumScene::Current, now);
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
