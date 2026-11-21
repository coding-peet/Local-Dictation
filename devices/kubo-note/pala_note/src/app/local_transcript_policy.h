#pragma once

#include <string_view>

namespace note_transcript {
template<class ReadCommitted>
constexpr bool hasCommittedTranscript(ReadCommitted read) {
  return read(".txt") || read(".txt.bak");
}

constexpr const char* localRouteState(bool committed) {
  return committed ? "usb-local" : "queued";
}

constexpr bool pendingRecording(bool catalogHasText, std::string_view queueState) {
  return !catalogHasText && queueState != "blocked" && queueState != "usb-local";
}
}  // namespace note_transcript
