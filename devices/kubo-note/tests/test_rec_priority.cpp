#include "../pala_note/src/app/button_gesture.h"
#include "../pala_note/src/app/calendar_button.h"
#include "../pala_note/src/app/rec_spool.h"
#include "../pala_note/src/app/record_priority.h"
#include <cassert>
#include <chrono>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <iterator>
#include <thread>
#include <vector>

namespace fs = std::filesystem;
using rec_priority::Gate;

static int prompt(int key, unsigned held, bool link = true) {
  uint32_t now = 0;
  return calendar_button::choose([&] { return now; }, [&] {
    return now >= 40 && now < 40 + held ? key : 0;
  }, [&](int delta) { now += delta; }, [&] {
    return link && !(key == 1 && now >= 40 + ButtonGesture::kHoldMs);
  });
}

static void calendar() {
  assert(prompt(1, 90) == 0);
  assert(prompt(2, 90) == 1);
  assert(prompt(1, 450) == -1);
  assert(prompt(1, 5000) == -1);
  assert(prompt(2, 90, false) == -1);
  assert(prompt(0, 0) == -1);
  std::cout << "calendar: short choices, REC hold, lost link and timeout passed\n";
}

// Exercise the same ownership transitions as a blocking foreground job and the
// independent codec task. Virtual times are deterministic, not physical latency.
static void blockedJob() {
  Gate gate;
  gate.arm();
  const uint32_t edge = 900;
  uint32_t first = 0;
  std::vector<int> pcm;
  std::thread microphone([&] {
    // The foreground is blocked until after REC has been released. Run the same
    // worker poll as the firmware against GPIO and I2S with a virtual clock.
    for (uint32_t now = 800; now < 2200; now += 5) {
      gate.service([] { return 0U; }, [&](uint32_t& pressed) {
        pressed = edge;
        return now >= edge + ButtonGesture::kHoldMs && now < 1600;
      }, [&](uint32_t pressed) {
        assert(pressed == edge);
        first = now + 6; // modelled codec read completion
        for (uint32_t sample = first; sample < 1600; sample += 10)
          pcm.push_back(static_cast<int>(sample));
      });
    }
  });
  microphone.join();
  gate.disarm();
  assert(gate.pending() && !gate.claimForeground());
  assert(rec_priority::latency(edge, first) == 326);
  assert(pcm.size() == 38 && pcm.front() == 1226 && pcm.back() == 1596);
  assert(rec_priority::latency(UINT32_MAX - 20, 5) == 26);
  gate.release();
  gate.arm();
  assert(gate.request()); // foreground notices hold a tick before the worker
  gate.disarm();          // prompt can exit before the worker runs
  assert(gate.pending() && gate.takeRequest());
  assert(!gate.claimWorker() && !gate.claimForeground());
  gate.complete();
  gate.release();
  gate.arm();
  gate.disarm();          // ordinary tap ends the window; mic stays closed
  assert(!gate.claimWorker() && gate.claimForeground());
  std::cout << "REC: blocked caller, request/disarm race, exclusive codec, wrapping clock passed\n";
}

static std::string read(const fs::path& file) {
  std::ifstream f(file, std::ios::binary);
  return {std::istreambuf_iterator<char>(f), {}};
}

static void spoolRecovery() {
  const auto tmp = fs::temp_directory_path() / ("pala-rec-" + std::to_string(
      std::chrono::steady_clock::now().time_since_epoch().count()));
  assert(fs::create_directory(tmp));
  const auto inbox = tmp / "inbox", notes = tmp / "notes";
  fs::create_directory(inbox);
  fs::create_directory(notes);
  const std::string src = "s1780000000_0000a001.wav";
  const auto claimed = std::string("c42-") + src;
  const std::string samples("RIFF\0original\xff", 14);
  std::ofstream(inbox / src, std::ios::binary) << samples;
  std::ofstream(inbox / "s1780000000_0000a001.mom") << "125";
  bool reserved = false, metadata = false, finished = false;
  int id = 0, allocations = 0;
  auto adopt = [&](const std::string& name) {
    return rec_spool::adopt(inbox.string()+"/", notes.string()+"/", name, id,
        [&] { ++allocations; return 42; }, [&](int n, uint32_t epoch) { return n == 42 && epoch == 1780000000U && reserved; },
        [&](int n, uint32_t seconds) { assert(n == 42 && seconds == 1780000000U); return metadata; },
        [&](int n) { assert(n == 42); return finished; });
  };
  assert(!adopt(src));                         // cut before capture.pending
  assert(!fs::exists(inbox / src) && read(inbox / claimed) == samples);
  reserved = true;
  assert(!adopt(claimed));                     // cut after moving moments
  assert(read(notes / "note_042.moments") == "125" && read(inbox / claimed) == samples);
  metadata = true;
  std::ofstream(notes / "note_042.wav") << "original";
  assert(!adopt(claimed));                     // never overwrite an existing recording
  assert(read(notes / "note_042.wav") == "original");
  fs::remove(notes / "note_042.wav");
  assert(!adopt(claimed));                     // WAV committed; catalog failed
  assert(!fs::exists(inbox / claimed) && read(notes / "note_042.wav") == samples);
  assert(allocations == 1 && reserved);        // capture.pending now owns repair
  const std::string fresh = "s1780000001_0000a002.wav";
  std::ofstream(inbox / fresh, std::ios::binary) << samples;
  assert(rec_spool::adopt(inbox.string()+"/", notes.string()+"/", fresh, id, [] { return 43; },
      [](int n, uint32_t ts) { return n == 43 && ts == 1780000001U; }, [](int, uint32_t ts) { return ts == 1780000001U; },
      [](int n) { return n == 43; }));
  assert(read(notes / "note_043.wav") == samples && read(notes / "note_042.wav") == samples);
  int parsedId = 0;
  std::string base;
  uint32_t epoch = 0;
  assert(!rec_spool::parse("c42-nonrecording.wav", parsedId, base, epoch));
  fs::remove_all(tmp);
  std::cout << "spool: repeated ID, timestamp, markers, original WAV bytes, destination collision passed\n";
}

int main() { calendar(); blockedJob(); spoolRecovery(); }
