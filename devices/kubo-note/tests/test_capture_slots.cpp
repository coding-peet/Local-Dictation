#include <cassert>
#include <cstdint>
#include <chrono>
#include <future>
#include "../pala_note/src/app/record_priority.h"
#include "../pala_note/src/app/capture_slots.h"
#include "../pala_note/src/app/pcm_capture_buffer.h"

// Run both actual producer rings against a writer that is deliberately unable
// to consume A until B's full hold AND release have already happened.
static void writerBlockedThroughTwoHolds() {
  using Ring = pcm_capture::Buffer<64, 256>;
  using namespace std::chrono_literals;
  const uint32_t edgeA = 0xffffff00U, edgeB = 450, edgeC = 1400;
  capture_slots::Registry q;
  rec_priority::Gate gate;
  Ring::Storage storageA{}, storageB{};
  Ring first(storageA), second(storageB);
  Ring* rings[] = {&first, &second};

  auto produce = [&](int slot, int blocks, int offset) {
    int16_t stereo[512];
    for (int n = 0; n < blocks; ++n) {
      for (int frame = 0; frame < 256; ++frame) {
        stereo[frame*2] = static_cast<int16_t>(((n*256+frame)*37+offset)%30000 - 15000);
        stereo[frame*2+1] = 32767;
      }
      assert(rings[slot]->pushStereo(stereo, q.edge(slot) + 320 + n*16, n == 23));
    }
    q.micStopped(slot);
  };
  auto drain = [&](int slot, int expectedBlocks, uint32_t expectedCrc) {
    uint32_t crc = 0xffffffffU;
    int count = 0, markers = 0;
    Ring::Block block{};
    while (rings[slot]->pop(block)) {
      assert(block.sampledAt == q.edge(slot) + 320 + count*16);
      if (block.marker) { assert(count == 23); ++markers; }
      for (uint16_t sample : block.samples) {
        for (uint16_t byte : {uint16_t(sample & 0xff), uint16_t(sample >> 8)}) {
          crc ^= byte;
          for (int bit = 0; bit < 8; ++bit)
            crc = (crc >> 1) ^ (crc & 1 ? 0xedb88320U : 0U);
        }
      }
      ++count;
    }
    assert(count == expectedBlocks && markers == 1 && ~crc == expectedCrc);
    assert(rings[slot]->producedBlocks() == unsigned(expectedBlocks));
    assert(rings[slot]->highWaterBlocks() == unsigned(expectedBlocks));
    q.drained(slot);
  };

  gate.arm();
  uint32_t selected;
  assert(gate.select([] { return 0U; }, [&](uint32_t& edge) { edge = edgeA; return true; }, selected));
  bool fresh = false;
  const int a = q.attach(selected, true, fresh);
  assert(a >= 0 && fresh);
  std::promise<void> unblockWriter;
  auto released = unblockWriter.get_future();
  auto writer = std::async(std::launch::async, [&] {
    assert(released.wait_for(5s) == std::future_status::ready);
    drain(a, 39, 0x9a3924ceU);
    gate.complete();
  });
  produce(a, 39, 117);
  const auto b = q.overlap(edgeB);
  assert(b.state == capture_slots::Admission::Accepted && b.prior == a);
  assert(!gate.claimForeground() && !gate.claimWorker());
  produce(b.slot, 47, 1849);
  assert(q.oldest(edgeC) == edgeA && q.newer(edgeA));
  assert(q.overlap(edgeC).state == capture_slots::Admission::Full);
  unblockWriter.set_value();
  writer.get();
  assert(q.finish(edgeA) == b.slot);
  gate.release();
  assert(gate.request() && gate.select([&] { return edgeB; }, [](uint32_t&) { return false; }, selected));
  const int attached = q.attach(selected, true, fresh);
  assert(attached == b.slot && !fresh);
  drain(attached, 47, 0x9e25b26fU);
  q.finish(edgeB);
  gate.complete();
  gate.release();
  assert(q.handled(edgeC) && !q.mayAttach(edgeC));
  assert(q.attach(edgeC, true, fresh) < 0); // buffered Home replay
  assert(q.takeFull() && !q.takeFull());

  // On a low-memory boot the first ring is still usable. A distinct hold
  // requests visible overload and cannot later start midway through the hold.
  capture_slots::Registry single;
  single.capacity(1);
  const int only = single.attach(edgeA, true, fresh);
  single.micStopped(only);
  assert(single.overlap(edgeB).state == capture_slots::Admission::Full);
  single.drained(only);
  single.finish(edgeA);
  assert(single.attach(edgeB, true, fresh) < 0);
  assert(single.attach(edgeC, true, fresh) == only && fresh);
}


int main() {
  writerBlockedThroughTwoHolds();
  using capture_slots::Registry;
  using capture_slots::Admission;
  Registry q;
  using Ring = pcm_capture::Buffer<4, 2>;
  Ring::Storage sa{}, sb{};
  Ring a(sa), b(sb);
  Ring* rings[] = {&a, &b};
  const int16_t first[] = {-4, 99, -6, 99}, second[] = {81, 99, 82, 99};
  bool fresh;
  auto firstSlot = q.attach(10, true, fresh);
  assert(firstSlot >= 0 && fresh && !q.watch());
  rings[firstSlot]->pushStereo(first, 11, false);
  q.micStopped(firstSlot);
  assert(q.watch());  // boot spool writer still waiting for storage, no foreground window
  auto next = q.overlap(20);
  assert(next.state == Admission::Accepted && next.prior == firstSlot && next.slot != firstSlot);
  rings[next.slot]->pushStereo(second, 21, true);
  q.micStopped(next.slot);
  assert(q.oldest(999) == 10 && q.newer(10));
  assert(q.overlap(30).state == Admission::Full);
  assert(q.overlap(30).state == Admission::Ignore && q.takeFull() && !q.takeFull());
  Ring::Block block{};
  assert(rings[firstSlot]->pop(block) && block.samples[0] == -4 && block.samples[1] == -6 && !block.marker);
  assert(!rings[firstSlot]->pop(block));
  q.drained(firstSlot);
  assert(q.finish(10) == next.slot);
  assert(q.finish(10) < 0 && q.oldest(999) == 20);
  assert(q.overlap(30).state == Admission::Ignore);
  assert(q.attach(30, true, fresh) < 0);
  q.openWindow(20);
  assert(q.attach(20, true, fresh) == next.slot && !fresh); // already released
  assert(rings[next.slot]->pop(block) && block.samples[0] == 81 && block.samples[1] == 82 && block.marker);
  assert(!rings[next.slot]->pop(block));
  q.drained(next.slot);
  q.finish(20);
  assert(q.watch()); // foreground save feedback transition
  assert(q.overlap(40).state == Admission::Accepted);
  assert(!q.seal(20) && q.oldest(0) == 40);
  q.closeWindow(20);
  // No unsolicited capture outside an active save, including during questions.
  Registry ask;
  assert(ask.overlap(5).state == Admission::Ignore);
  auto s = ask.attach(5, false, fresh);
  ask.micStopped(s);
  assert(!ask.watch() && ask.overlap(6).state == Admission::Ignore);
  ask.drained(s);
  assert(ask.attach(7, true, fresh) == s && fresh);
  ask.openWindow(7);
  ask.micStopped(s);
  ask.drained(s);
  ask.finish(7);
  assert(ask.seal(7) && !ask.watch());
}
