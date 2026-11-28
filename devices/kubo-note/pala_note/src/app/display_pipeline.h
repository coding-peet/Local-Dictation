#pragma once
#include "display_frames.h"
#include <cstdint>

namespace note_display {
using display_frames::Refresh;
void initialize();
uint8_t* pixels();
void startHome();
bool abandonSupersededUi();
bool bootHomeOutstanding();
bool inBootHome(uint32_t edge);
bool present(Refresh mode, void (*service)() = nullptr, bool asynchronousHome = false);
bool lastPresentationOk();
// Physical BUSY-low of the last acknowledged synchronous frame on this task.
uint32_t lastPresentationVisibleAt();
// True from this edge's first PCM until a newer note or question has first PCM.
bool latestCapture(uint32_t edge);
// Claim a foreground paint epoch only for a completed, still-current capture.
bool beginSaved(uint32_t edge);
// Can be called before firstPcm. Repeated updates for the same edge coalesce.
void setPriorSavePending(uint32_t activeEdge, bool pending);
void firstPcm(uint32_t edge, bool question);
void recordingStatus(uint32_t edge,uint32_t elapsed,bool offer,bool locked);
void progress(uint32_t edge, uint32_t elapsed, int moments);
void endCapture(uint32_t edge, bool saving = true);
void invalidateRecordingCache();
}
