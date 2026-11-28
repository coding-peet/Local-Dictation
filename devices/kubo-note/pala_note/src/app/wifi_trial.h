#pragma once
#include "../../config.h"
#if PALA_WIFI_TRIAL
#include <Arduino.h>

namespace wifi_trial {
// These hooks run on the recorder loop, including the authenticated USB reader.
struct Trace {
  uint32_t note = 0, phase = 0, passes = 0, association = 0, localIp = 0;
  uint32_t queries = 0, answer = 0, rejected = 0, tlsAttempts = 0;
  uint32_t verified = 0, lastError = 0, httpStatus = 0;
  uint32_t socketIp = 0, socketPort = 0, tcpReached = 0;
  bool sampling = false;
};
inline Trace& trace() { static Trace state; return state; }

inline bool arm(int note) {
  if (note <= 0) return false;
  trace() = {};
  trace().note = note;
  trace().phase = 1;
  return true;
}
// A cable-powered preflight only requests /pala/ready. It never starts the queue.
inline bool queueLinkProbe() {
  if (trace().phase == 1 || trace().phase == 2 || trace().phase == 4) return false;
  trace() = {};
  trace().phase = 4;
  return true;
}
inline void detached() {
  if (trace().phase == 1) trace().phase = 2;
}
inline bool takeStart() {
  if (trace().phase != 2) return false;
  trace().phase = 3;
  return true;
}
class Probe {
 public:
  explicit Probe(int note) {
    auto& t = trace();
    t.sampling = t.phase == 3 && note > 0 && static_cast<uint32_t>(note) == t.note;
    if (t.sampling) ++t.passes;
  }
  ~Probe() { trace().sampling = false; }
};
inline void association(bool connected, uint32_t address) {
  auto& t = trace();
  if (!t.sampling) return;
  if (connected) { t.association = 2; t.localIp = address; }
  else if (!t.association) t.association = 1;
}
inline void resolved(uint32_t address) {
  auto& t = trace();
  if (t.sampling) { ++t.queries; if (address) t.answer = address; }
}
inline void disallowed() { if (trace().sampling) ++trace().rejected; }
inline void tls(bool verified, int error) {
  auto& t = trace();
  if (!t.sampling) return;
  ++t.tlsAttempts;
  if (verified) ++t.verified;
  else t.lastError = static_cast<uint32_t>(error);
}
inline String values() {
  const auto t = trace();
  String result;
  for (const auto value : {t.note,t.phase,t.passes,t.association,t.localIp,t.queries,t.answer,
                           t.rejected,t.tlsAttempts,t.verified,t.lastError,t.httpStatus}) {
    result += ' '; result += String(value);
  }
  return result;
}
}
#endif
