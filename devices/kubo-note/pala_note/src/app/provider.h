#pragma once
// Generated locally by mac-bridge/pair.py. Without pairing the stock provider is used.
#if __has_include("../../bridge_pairing.h")
#include "../../bridge_pairing.h"
#elif __has_include("bridge_pairing.h")
#include "bridge_pairing.h"
#else
#define PALA_MAC_BRIDGE 0
#define PALA_HOST "api.openai.com"
#define PALA_PORT 443
#define PALA_AUTHORITY PALA_HOST
#endif

#ifndef PALA_BRIDGE_DISCOVERY
#define PALA_BRIDGE_DISCOVERY 0
#endif
