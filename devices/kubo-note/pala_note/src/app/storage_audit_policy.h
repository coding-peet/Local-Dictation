#pragma once

#include <cstddef>
#include <cstdint>
#include <cstring>
#include <string_view>

enum class NoteRestartMode : uint8_t { None, Restart, Audit };

struct NoteRestartRequest {
  NoteRestartMode mode = NoteRestartMode::None;
  uint32_t ticket = 0;
};

namespace note_maintenance {

enum class AuditState : uint32_t { Never, Running, Incomplete, Complete };

class RestartConfirmation {
public:
  bool request(NoteRestartMode mode, uint32_t now) {
    if (mode == NoteRestartMode::None || pending(now).ticket != 0) return false;
    mode_ = mode;
    if (++ticket_ == 0) ++ticket_;
    requestedAt_ = now;
    return true;
  }

  NoteRestartRequest pending(uint32_t now) const {
    return mode_ != NoteRestartMode::None && now - requestedAt_ < 30'000
        ? NoteRestartRequest{mode_, ticket_} : NoteRestartRequest{};
  }

  bool answer(NoteRestartRequest request, uint32_t now) {
    const auto current = pending(now);
    if (request.ticket == 0 || request.ticket != current.ticket || request.mode != current.mode) return false;
    mode_ = NoteRestartMode::None;
    return true;
  }

private:
  NoteRestartMode mode_ = NoteRestartMode::None;
  uint32_t ticket_ = 0;
  uint32_t requestedAt_ = 0;
};

constexpr bool shouldRunConfirmedAudit(bool cookie, bool softwareReset, AuditState state) {
  return cookie && softwareReset && state == AuditState::Running;
}

constexpr uint32_t estimateAuditSeconds(uint32_t sampleNotes, uint32_t sampleMilliseconds, uint32_t notes) {
  if (sampleNotes == 0 || sampleMilliseconds == 0 || notes == 0) return 0;
  const uint64_t denominator = uint64_t{sampleNotes} * 1000;
  const uint64_t estimate = (uint64_t{sampleMilliseconds} * notes - 1) / denominator + 1;
  return estimate > UINT32_MAX ? UINT32_MAX : static_cast<uint32_t>(estimate);
}

constexpr bool parseUnsigned(std::string_view text, uint32_t limit, uint32_t& value) {
  value = 0;
  if (text.empty()) return false;
  for (const char digit : text) {
    if (digit < '0' || digit > '9') return false;
    const auto number = static_cast<uint32_t>(digit - '0');
    if (number > limit || value > (limit - number) / 10) return false;
    value = value * 10 + number;
  }
  return true;
}

struct CatalogRow {
  int note = 0;
  uint16_t moments = 0;
  bool hasTranscript = false;
  bool valid = false;
};

constexpr CatalogRow parseCatalogRow(std::string_view row) {
  if (!row.empty() && row.back() == '\r') row.remove_suffix(1);
  const auto first = row.find(',');
  if (first == row.npos) return {};
  const auto second = row.find(',', first + 1);
  if (second == row.npos || second - first - 1 > 15) return {};
  const auto third = row.find(',', second + 1);
  const auto synced = row.substr(second + 1, third == row.npos ? row.npos : third - second - 1);
  if (synced != "0" && synced != "1") return {};
  uint32_t note = 0, moments = 0;
  if (!parseUnsigned(row.substr(0, first), INT32_MAX, note) || note == 0) return {};
  if (third != row.npos && !parseUnsigned(row.substr(third + 1), UINT16_MAX, moments)) return {};
  return {static_cast<int>(note), static_cast<uint16_t>(moments), synced == "1", true};
}

constexpr uint16_t readLe16(const uint8_t* value) {
  return static_cast<uint16_t>(uint16_t{value[0]} | uint16_t{value[1]} << 8);
}

constexpr uint32_t readLe32(const uint8_t* value) {
  return uint32_t{value[0]} | uint32_t{value[1]} << 8 | uint32_t{value[2]} << 16 | uint32_t{value[3]} << 24;
}

// Inspect the RIFF container and PCM format. Skip every data payload via
// offsets, including when the file contains ancillary chunks.
template <typename ReadAt>
bool checkWav(uint64_t fileBytes, ReadAt&& readAt) {
  uint8_t header[12];
  if (fileBytes < sizeof(header) || fileBytes > UINT32_MAX || !readAt(0, header, sizeof(header))) return false;
  if (std::memcmp(header, "RIFF", 4) || std::memcmp(header + 8, "WAVE", 4)
      || uint64_t{readLe32(header + 4)} + 8 != fileBytes) return false;

  uint16_t blockAlign = 0, dataAlignment = 0;
  bool foundData = false;
  uint32_t dataBytes = 0;
  uint64_t offset = sizeof(header);
  unsigned chunks = 0;
  while (offset + 8 <= fileBytes && chunks++ < 64) {
    uint8_t chunk[8];
    if (!readAt(static_cast<uint32_t>(offset), chunk, sizeof(chunk))) return false;
    const uint32_t size = readLe32(chunk + 4);
    offset += sizeof(chunk);
    const uint64_t next = offset + size + (size & 1U);
    if (next > fileBytes) return false;
    if (std::memcmp(chunk, "fmt ", 4) == 0) {
      uint8_t format[16];
      if (blockAlign != 0 || size < sizeof(format) ||
          !readAt(static_cast<uint32_t>(offset), format, sizeof(format))) return false;
      const uint16_t channels = readLe16(format + 2);
      const uint32_t frequency = readLe32(format + 4);
      blockAlign = readLe16(format + 12);
      if (readLe16(format) != 1 || (channels != 1 && channels != 2) ||
          (frequency != 16'000 && frequency != 24'000) || readLe16(format + 14) != 16 ||
          blockAlign != channels * 2 || readLe32(format + 8) != frequency * blockAlign) return false;
      dataAlignment = blockAlign;
    } else if (std::memcmp(chunk, "data", 4) == 0) {
      if (foundData || size == 0) return false;
      foundData = true;
      dataBytes = size;
    }
    offset = next;
  }
  return offset == fileBytes && dataAlignment != 0 && foundData && dataBytes % dataAlignment == 0;
}

}  // namespace note_maintenance
