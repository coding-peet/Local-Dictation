#include "../pala_note/src/app/capture_clock.h"
#include <cassert>

int main() {
  // Midnight UTC on 1 December. The press was 350 ms earlier.
  constexpr uint32_t midnight = 1796083200;
  assert(capture_clock::atEdge(true, midnight, 100000, 1200, 850) == midnight - 1);
  assert(capture_clock::atEdge(true, midnight, 350000, 1200, 850) == midnight);
  assert(capture_clock::atEdge(true, midnight, 100000, 100, UINT32_MAX - 249) == midnight - 1);
  capture_clock::Anchor clock;
  assert(clock.epoch(1100000, 850) == 0);
  clock.sync(int64_t(midnight) * 1000000 + 100000, 1200000, false, true);
  assert(clock.epoch(1250000, 850) == 0);  // Clock repaired after the physical press.
  assert(clock.epoch(1250000, 1220) == midnight);
  clock.sync(int64_t(midnight) * 1000000 + 100000, 1200000, true, true);
  assert(clock.epoch(2000000, 850) == midnight - 1);  // Release after midnight.
  assert(capture_clock::atEdge(false, midnight, 100000, 1200, 850) == 0);
  assert(capture_clock::atEdge(true, 0, 0, 400, 0) == 0);
  assert(capture_clock::atEdge(true, 1704067200, 0, 400, 0) == 0);
  assert(capture_clock::atEdge(true, 4102444800LL, 0, 0, 0) == 0);
}
