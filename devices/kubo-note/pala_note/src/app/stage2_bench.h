#pragma once
#include "../../config.h"
#include "stage2_cut.h"
namespace microphone { struct Stats; }
#include <cstdint>
#if PALA_STAGE2_BENCH
#include <Arduino.h>
#endif

namespace stage2 {
#if PALA_STAGE2_BENCH
// Bench-only monotonic timestamps. The USB reader copies a consistent revision.
void captureStart(uint32_t edge, bool inbox, uint32_t acceptedMs);
uint32_t bleBlockRequested(uint32_t offset, uint32_t bytes);
void bleBlockNotifications(uint32_t sequence, uint32_t firstQueuedAt, uint32_t lastQueuedAt, bool completed);
void firstPcm(uint32_t edge, uint32_t sampledAt);
void displayEvent(uint32_t edge, const char* phase, uint32_t at);
void homeEvent(const char* phase, uint32_t at);
void captureMark(uint32_t edge, const char* name, uint32_t at);
void captureResult(uint32_t edge, uint32_t framesIn, uint32_t framesOut,
                   uint32_t crc, bool overflow, bool saved);
void captureAudio(uint32_t edge, const microphone::Stats& stats);
void previousSaveStarted(uint32_t edge, uint32_t priorEdge, uint32_t at);
void previousSaveFinished(uint32_t edge, uint32_t at, bool success);
void previousSaveVisible(uint32_t edge, bool pending, uint32_t at);
void releaseEvent(uint32_t edge, const char* phase, uint32_t at);
void captureWavCollision(uint32_t edge, int note);
void captureArchived(uint32_t edge, int note);
void bindInboxFile(uint32_t edge, const char* basename);
void captureInboxArchived(const char* name, int note);
String metricsPage(const String& page);

void displayRequest(uint32_t edge);
void displayDraw();
void displayRefresh();
void displayWaveform(bool full);
void displayBusy();
void displayWaitEnd(bool timedOut);
void displayReturned();
void reportDisplay();

bool configure(const String& name, int note);
void cutWindow(Point point, int note, uint32_t edge = 0);
bool loseReceipt(int note);
bool unknownClock();
bool bootRtcAvailable();
void transport(const char* link, const char* phase, int note);
#else
inline void captureStart(uint32_t, bool, uint32_t) {}
inline void firstPcm(uint32_t, uint32_t) {}
inline void displayEvent(uint32_t, const char*, uint32_t) {}
inline void homeEvent(const char*, uint32_t) {}
inline void captureMark(uint32_t, const char*, uint32_t) {}
inline void captureResult(uint32_t, uint32_t, uint32_t, uint32_t, bool, bool) {}
inline void captureAudio(uint32_t, const microphone::Stats&) {}
inline void previousSaveStarted(uint32_t, uint32_t, uint32_t) {}
inline void previousSaveFinished(uint32_t, uint32_t, bool) {}
inline void previousSaveVisible(uint32_t, bool, uint32_t) {}
inline void releaseEvent(uint32_t, const char*, uint32_t) {}
inline void captureWavCollision(uint32_t, int) {}
inline void captureArchived(uint32_t, int) {}
inline void displayRequest(uint32_t) {}
inline void displayDraw() {}
inline void displayRefresh() {}
inline void displayWaveform(bool) {}
inline void displayBusy() {}
inline void displayWaitEnd(bool) {}
inline void displayReturned() {}
inline void reportDisplay() {}
inline void cutWindow(Point, int, uint32_t = 0) {}
inline bool loseReceipt(int) { return false; }
inline bool unknownClock() { return false; }
inline bool bootRtcAvailable() { return true; }
inline void transport(const char*, const char*, int) {}
#endif
}
