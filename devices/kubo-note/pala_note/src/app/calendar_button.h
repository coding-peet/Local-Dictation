#pragma once
#include <cstdint>

namespace calendar_button {
// 0 and 1 are short choices. An interrupted prompt leaves its Bridge proposal pending.
template<class Now, class Read, class Pause, class KeepWaiting>
int choose(Now now, Read buttons, Pause pause, KeepWaiting keepWaiting) {
  bool armed = false;
  const uint32_t since = now();
  while (now() - since < 30000U) {
    if (!keepWaiting()) return -1;
    const int input = buttons();
    if (input == 0) armed = true;
    if (armed && (input == 1 || input == 2)) {
      pause(35);
      if (buttons() != input) continue;
      while (buttons() == input) {
        if (!keepWaiting() || now() - since >= 30000U) return -1;
        pause(10);
      }
      if (!keepWaiting() || buttons() != 0) return -1;
      return input - 1;
    }
    pause(10);
  }
  return -1;
}
}
