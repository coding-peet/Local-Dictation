#include "local_transcript_policy.h"

#include <string_view>

namespace {
using namespace note_transcript;

constexpr bool pendingFromInterruptedCommit() {
  const bool committed = hasCommittedTranscript([](std::string_view extension) {
    return extension == ".txt.tmp";
  });
  const std::string_view state = localRouteState(committed);
  return !committed && state == "queued" && pendingRecording(false, state);
}

constexpr bool durableTranscriptBeatsStaleCatalog() {
  for (const std::string_view durable : {std::string_view(".txt"), std::string_view(".txt.bak")}) {
    const bool committed = hasCommittedTranscript([durable](std::string_view extension) { return durable == extension; });
    const std::string_view state = localRouteState(committed);
    if (!committed || state != "usb-local" || pendingRecording(false, state)) return false;
  }
  return true;
}

static_assert(pendingFromInterruptedCommit());
static_assert(durableTranscriptBeatsStaleCatalog());
static_assert(!pendingRecording(false, "blocked"));
static_assert(!pendingRecording(true, "queued"));
static_assert(pendingRecording(false, "retry"));
}
