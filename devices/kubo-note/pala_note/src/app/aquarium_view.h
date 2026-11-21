#pragma once

#include "aquarium_state.h"

int aquariumFrame(AquariumScene scene, uint32_t elapsed);
bool showAquariumFrame(int frame, void (*serviceWhileWaiting)());
