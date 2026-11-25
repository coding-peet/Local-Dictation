#include "../pala_note/src/app/storage_audit_policy.h"

#include <algorithm>
#include <array>
#include <cassert>
#include <cstdio>
#include <chrono>
#include <vector>

using namespace note_maintenance;

constexpr auto catalog = parseCatalogRow("50000,Inbox,1,17\r");
static_assert(catalog.valid && catalog.note == 50000 && catalog.hasTranscript && catalog.moments == 17);
static_assert(parseCatalogRow("90001,Inbox,0").valid);
static_assert(!parseCatalogRow("2147483648,Inbox,0").valid);
static_assert(!parseCatalogRow("7,Inbox,2,1").valid);
static_assert(!parseCatalogRow("7,Inbox,1,123456").valid);
static_assert(!parseCatalogRow("7,Inbox,1,1,trailing").valid);
static_assert(estimateAuditSeconds(100, 1000, 50'000) == 500);

template <size_t N>
void put32(std::array<uint8_t, N>& bytes, size_t offset, uint32_t value) {
  for (unsigned i = 0; i < 4; ++i) bytes[offset + i] = static_cast<uint8_t>(value >> (i * 8));
}

int main() {
  RestartConfirmation confirmation;
  assert(confirmation.request(NoteRestartMode::Audit, 300));
  assert(!confirmation.request(NoteRestartMode::Restart, 301));
  const auto first = confirmation.pending(301);
  assert(!confirmation.answer({NoteRestartMode::Restart, first.ticket}, 302));
  assert(confirmation.answer(first, 303));
  assert(!confirmation.answer(first, 304));
  assert(confirmation.request(NoteRestartMode::Restart, UINT32_MAX - 42));
  assert(confirmation.pending(10).mode == NoteRestartMode::Restart);
  const auto expired = confirmation.pending(10);
  assert(confirmation.pending(36'000).mode == NoteRestartMode::None);
  assert(confirmation.request(NoteRestartMode::Restart, 36'001));
  assert(!confirmation.answer(expired, 36'002));
  assert(confirmation.answer(confirmation.pending(36'002), 36'003));
  assert(!shouldRunConfirmedAudit(true, false, AuditState::Running));
  assert(!shouldRunConfirmedAudit(false, true, AuditState::Running));
  assert(!shouldRunConfirmedAudit(true, true, AuditState::Incomplete));
  assert(shouldRunConfirmedAudit(true, true, AuditState::Running));

  // PCM mono, 16 kHz. A 32 MB data chunk must never be read by the audit.
  constexpr uint32_t payload = 32'000'000;
  std::array<uint8_t, 44> wav{{
      'R','I','F','F',0,0,0,0,'W','A','V','E',
      'f','m','t',' ',16,0,0,0,1,0,1,0,0x80,0x3e,0,0,0x00,0x7d,0,0,2,0,16,0,
      'd','a','t','a',0,0,0,0
  }};
  put32(wav, 4, payload + 36);
  put32(wav, 40, payload);
  size_t bytesRead = 0;
  const auto read = [&](uint32_t offset, uint8_t* buffer, size_t size) {
    bytesRead += size;
    if (offset + size > wav.size()) return false;
    std::copy_n(wav.begin() + offset, size, buffer);
    return true;
  };
  assert(checkWav(payload + wav.size(), read));
  assert(bytesRead == wav.size());
  assert(!checkWav(payload + wav.size() - 1, read));
  wav[34] = 8;
  assert(!checkWav(payload + wav.size(), read));
  wav[34] = 16;
  put32(wav, 40, payload - 1);
  assert(!checkWav(payload + wav.size(), read));

  // A large catalog with six-digit IDs, checked with the real catalog and
  // WAV policies. This measures memory reads on the host, not SD I/O.
  put32(wav, 40, payload);
  bytesRead = 0;
  const auto startedAt = std::chrono::steady_clock::now();
  for (int note = 75'001; note <= 125'000; ++note) {
    char line[40];
    const int length = std::snprintf(line, sizeof(line), "%d,Inbox,%d,2", note, note % 2);
    const auto current = parseCatalogRow({line, static_cast<size_t>(length)});
    assert(current.valid && current.note == note && current.hasTranscript == (note % 2 != 0));
    assert(checkWav(payload + wav.size(), read));
  }
  const auto elapsed = std::chrono::steady_clock::now() - startedAt;
  assert(bytesRead == 50'000 * wav.size());
  std::printf("50,000 catalog rows and WAV headers in %lld ms, %zu header bytes read, no audio payload reads\n",
              static_cast<long long>(std::chrono::duration_cast<std::chrono::milliseconds>(elapsed).count()), bytesRead);
}
