#pragma once
#include <cstdint>

namespace button_prompt {
constexpr bool retryDue(uint32_t now, uint32_t failedAt) {
  return now - failedAt >= 1000;
}

// Called after settling both IRQ rings, with GPIO interrupts disabled.
// Preserve releases of pre-existing holds and every edge after visibility.
template <typename Button>
constexpr void discardThrough(Button& button, uint32_t visibleAt) {
  constexpr auto capacity = sizeof(button.edges) / sizeof(button.edges[0]);
  while (button.read != button.write) {
    const auto& next = button.edges[button.read];
    if (static_cast<int32_t>(next.at - visibleAt) > 0) break;
    button.replayDown = next.down;
    button.read = (button.read + 1) % capacity;
  }
  // The most recent raw edge may still be inside the debounce interval.
  if (button.rawDown != button.stableDown && static_cast<int32_t>(button.rawAt - visibleAt) <= 0) {
    button.stableDown = button.rawDown;
    button.replayDown = button.rawDown;
  }
  button.gesture.reset();
}
}  // namespace button_prompt
