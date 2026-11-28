#pragma once
#include <atomic>
#include <cstdint>

namespace rec_priority {
enum class Phase : uint8_t { Idle, Armed, Requested, Recording, Complete, Foreground };

// One owner for the codec. Leaving a blocking call can disarm a window only
// until the microphone task claims it. A completed press survives release.
class Gate {
  std::atomic<Phase> phase{Phase::Idle};
  bool change(Phase from, Phase to) { return phase.compare_exchange_strong(from, to); }
public:
  void arm() { change(Phase::Idle, Phase::Armed); }
  void disarm() { change(Phase::Armed, Phase::Idle); }
  bool request() { return change(Phase::Idle, Phase::Requested) || change(Phase::Armed, Phase::Requested); }
  bool takeRequest() { return change(Phase::Requested, Phase::Recording); }
  bool claimWorker() { return change(Phase::Armed, Phase::Recording); }
  bool claimForeground() {
    return change(Phase::Idle, Phase::Foreground) || change(Phase::Armed, Phase::Foreground);
  }
  template<class ReadEdge, class PhysicalHold>
  bool select(ReadEdge requestedEdge, PhysicalHold held, uint32_t& edge) {
    if (takeRequest()) { edge = requestedEdge(); return true; }
    return armed() && held(edge) && claimWorker();
  }
  template<class ReadEdge, class PhysicalHold, class Capture>
  void service(ReadEdge requestedEdge, PhysicalHold held, Capture capture) {
    uint32_t edge;
    if (select(requestedEdge, held, edge)) {
      capture(edge);
      complete();
    }
  }
  void complete() { phase.store(Phase::Complete); }
  bool armed() const { return phase.load() == Phase::Armed; }
  bool recording() const {
    const auto p = phase.load();
    return p == Phase::Recording || p == Phase::Requested;
  }
  bool pending() const {
    const auto p = phase.load();
    return p == Phase::Requested || p == Phase::Recording || p == Phase::Complete;
  }
  void release() { phase.store(Phase::Idle); }
};

inline uint32_t latency(uint32_t edgeMs, uint32_t firstSampleMs) {
  return firstSampleMs - edgeMs;
}
}
