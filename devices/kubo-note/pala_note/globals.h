#pragma once
#include <vector>
#include <atomic>
#include <WebServer.h>
#include "types.h"
#include "src/power/board_power_bsp.h"
#include "src/display/epaper_driver_bsp.h"

extern std::vector<NoteEntry> noteIndex;

extern AppState  state;
extern int       lastRecNum;

extern uint32_t  lastActivityMs;
// Zero if this REC press is still held or another press has superseded it.
uint32_t recordingButtonRelease(uint32_t edge);

extern WebServer transferServer;
extern bool      transferServerActive;
extern String    transferUrl;
extern String    transferHostname;

extern std::atomic<bool> timeReady;

extern bool      audioPlaying;
extern bool      stopPlayback;

extern board_power_bsp_t      board;
extern epaper_driver_display* display;
