#pragma once
#include <cstdint>

namespace capture_slots {
enum class Admission { Ignore, Accepted, Full };
struct Decision {
  Admission state = Admission::Ignore;
  int slot = -1, prior = -1;
};

// The caller serializes these small metadata operations. Each slot keeps its
// own PCM storage until the writer has drained it AND publication has finished.
class Registry {
 public:
  static constexpr int count = 2;
  // Set once before tasks start. A low-memory boot can retain one working
  // recorder and report a full queue on overlap.
  void capacity(int slots) { capacity_ = slots; }
  uint32_t edge(int i) const { return takes_[i].edge; }
  bool queued(int i) const { return takes_[i].used && !takes_[i].attached; }
  bool watch() const { return mic_ < 0 && (watchWindow_ || latestPending() >= 0); }

  int attach(uint32_t edge, bool note, bool& fresh) {
    fresh = false;
    for (int i = 0; i < count; ++i) {
      if (queued(i) && takes_[i].edge == edge && note) {
        takes_[i].attached = true;
        return i;
      }
    }
    if (mic_ >= 0 || (note && !mayAttach(edge))) return -1;
    const int i = available();
    if (i < 0) return -1;
    reserve(i, edge, note, true);
    fresh = true;
    return i;
  }

  Decision overlap(uint32_t edge) {
    if (!watch() || (seen_ && lastEdge_ == edge)) return {};
    const int i = available();
    if (i < 0) {
      // A sustained refused hold must never start midway through its audio.
      lastEdge_ = edge;
      seen_ = full_ = true;
      return {Admission::Full};
    }
    const int prior = latestPending();
    reserve(i, edge, true, false);
    takes_[i].prior = prior;
    return {Admission::Accepted, i, prior};
  }

  void micStopped(int i) { if (mic_ == i) mic_ = -1; }
  void drained(int i) {
    auto& take = takes_[i];
    take.drained = true;
    if (!take.pending) take = {};
  }

  // Return the dependent capture exactly once so its indicator and timestamps
  // are cleared at publication (or failure), independently of catalog replay.
  int finish(uint32_t edge) {
    int dependent = -1;
    for (int i = 0; i < count; ++i) {
      auto& take = takes_[i];
      if (!take.used || !take.pending || take.edge != edge) continue;
      take.pending = false;
      for (int j = 0; j < count; ++j) {
        if (takes_[j].used && takes_[j].prior == i) {
          dependent = j;
          takes_[j].prior = -1;
        }
      }
      if (take.drained) take = {};
      break;
    }
    return dependent;
  }

  void openWindow(uint32_t edge) {
    window_ = true;
    windowEdge_ = edge;
    watchWindow_ = latestPending() >= 0;
  }
  void closeWindow(uint32_t edge) {
    if (window_ && windowEdge_ == edge) window_ = watchWindow_ = false;
  }
  bool seal(uint32_t edge) {
    if (newer(edge)) return false;
    closeWindow(edge);
    return true;
  }
  bool newer(uint32_t edge) const {
    for (const auto& take : takes_)
      if (take.used && take.note && later(take.edge, edge)) return true;
    return false;
  }
  bool mayAttach(uint32_t value) const {
    for (int i = 0; i < count; ++i)
      if (takes_[i].used && edge(i) == value) return queued(i);
    return !seen_ || lastEdge_ != value;
  }
  bool anyQueued() const { return queued(0) || queued(1); }
  uint32_t oldest(uint32_t fallback) const {
    int old = -1;
    for (int i = 0; i < count; ++i) {
      if (!takes_[i].pending) continue;
      if (old < 0 || later(takes_[old].edge, edge(i))) old = i;
    }
    return old < 0 ? fallback : edge(old);
  }
  bool handled(uint32_t edge) const { return seen_ && lastEdge_ == edge; }
  bool takeFull() {
    const bool full = full_;
    full_ = false;
    return full;
  }

 private:
  struct Take {
    uint32_t edge = 0;
    bool used = false, attached = false, note = false, pending = false, drained = false;
    int prior = -1;
  } takes_[count];
  int mic_ = -1, capacity_ = count;
  uint32_t lastEdge_ = 0, windowEdge_ = 0;
  bool seen_ = false, full_ = false, window_ = false, watchWindow_ = false;

  static bool later(uint32_t a, uint32_t b) { return int32_t(a - b) > 0; }
  int available() const {
    for (int i = 0; i < capacity_; ++i) if (!takes_[i].used) return i;
    return -1;
  }
  int latestPending() const {
    int latest = -1;
    for (int i = 0; i < count; ++i)
      if (takes_[i].pending && (latest < 0 || later(edge(i), edge(latest)))) latest = i;
    return latest;
  }
  void reserve(int i, uint32_t edge, bool note, bool attached) {
    takes_[i] = {edge, true, attached, note, note, false, -1};
    mic_ = i;
    if (note) {
      lastEdge_ = edge;
      seen_ = true;
      if (window_ && windowEdge_ == edge) watchWindow_ = true;
    }
  }
};
}  // namespace capture_slots
