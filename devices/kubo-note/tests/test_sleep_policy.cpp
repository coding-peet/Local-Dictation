#include "../pala_note/src/app/sleep_policy.h"

static_assert(validIdleSleepMinutes(0) && validIdleSleepMinutes(1) && validIdleSleepMinutes(99));
static_assert(!validIdleSleepMinutes(-1) && !validIdleSleepMinutes(100));

constexpr auto defaults = idleSleepPolicy(0);
static_assert(defaults.homeMs == 60000 && defaults.queuedMs == 180000 && defaults.companionMs == 300000);
constexpr auto oneMinute = idleSleepPolicy(1);
static_assert(oneMinute.homeMs == 60000 && oneMinute.queuedMs == 60000 && oneMinute.companionMs == 60000);
constexpr auto maxMinutes = idleSleepPolicy(99);
static_assert(maxMinutes.homeMs == 5940000 && maxMinutes.queuedMs == 5940000 && maxMinutes.companionMs == 5940000);
