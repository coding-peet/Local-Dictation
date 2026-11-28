
#include "../pala_note/src/app/recording_lock.h"
#include "../pala_note/src/app/display_frames.h"
#include "../pala_note/src/app/recording_parts.h"
#include <cassert>
#include <filesystem>
#include <fstream>
#include "../pala_note/src/app/pcm_capture_buffer.h"
#include <memory>
using namespace recording_parts;

static uint32_t checksum(uint32_t crc, const uint8_t* pcm, size_t size) {
  static const auto table = [] {
    std::array<uint32_t, 256> lut{};
    for (unsigned i = 0; i < 256; i++) {
      uint32_t c = i;
      for (int j = 0; j < 8; j++) c = (c & 1 ? 0xedb88320u : 0) ^ (c >> 1);
      lut[i] = c;
    }
    return lut;
  }();
  crc = ~crc;
  while (size--) crc = table[(crc ^ *pcm++) & 255] ^ (crc >> 8);
  return ~crc;
}
static bool header(FILE* f, uint32_t bytes) {
  uint8_t h[44] = {'R', 'I', 'F', 'F', 0,  0, 0,   0,   'W', 'A',  'V',  'E', 'f', 'm', 't',
                   ' ', 16,  0,   0,   0,  1, 0,   1,   0,   0x80, 0x3e, 0,   0,   0,   0x7d,
                   0,   0,   2,   0,   16, 0, 'd', 'a', 't', 'a',  0,    0,   0,   0};
  capture_wav::putSize(h + 4, bytes + 36);
  capture_wav::putSize(h + 40, bytes);
  return std::fseek(f, 0, SEEK_SET) == 0 && std::fwrite(h, 1, 44, f) == 44 && sync(f) &&
         std::fseek(f, 0, SEEK_END) == 0;
}
static void hour(const std::string& wav) {
  using Ring = pcm_capture::RecorderBuffer;
  auto storage = std::make_unique<Ring::Storage>();
  Ring ring(*storage);
  uint32_t micCrc = 0, frame = 0;
  auto generate = [&] {
    int16_t stereo[512], mono[256];
    for (unsigned i = 0; i < 256; ++i, ++frame) {
      mono[i] = int16_t((frame * 2654435761u) >> 16);
      stereo[2 * i] = mono[i];
      stereo[2 * i + 1] = 17;
    }
    micCrc = checksum(micCrc, reinterpret_cast<uint8_t*>(mono), sizeof(mono));
    assert(ring.pushStereo(stereo, frame / 16, false));
  };
  auto open = [&](unsigned i) {
    if (i == 1)
      for (int b = 0; b < 407; b++) generate();  // 6.512 s while SD cannot progress
    FILE* f = std::fopen(path(wav, i).c_str(), "wb");
    assert(f && header(f, 0));
    return f;
  };
  FILE* f = open(0);
  Writer writer(f, wav, true, open, header, checksum);
  Ring::Block block;
  std::array<uint8_t, 8192> batched{};
  size_t buffered = 0;
  while (frame < 57600000 || ring.pop(block)) {
    if (frame < 57600000) {
      generate();
      if (!ring.pop(block)) continue;
    }
    std::memcpy(batched.data() + buffered, block.samples, sizeof(block.samples));
    buffered += sizeof(block.samples);
    if (buffered == batched.size()) {
      assert(writer.write(batched.data(), buffered));
      buffered = 0;
    }
  }
  if (buffered) assert(writer.write(batched.data(), buffered));
  assert(header(f, writer.partSize) && std::fclose(f) == 0 && writer.publish(false));
  assert(writer.total == 115200000 && ring.highWaterBlocks() >= 407);
  std::vector<Entry> rows;
  assert(read(wav, rows) == State::Complete && rows.size() == 12);
  uint32_t joined = 0;
  for (unsigned i = 0; i < rows.size(); ++i) {
    auto name = path(wav, i);
    FILE* in = std::fopen(name.c_str(), "rb");
    assert(in);
    uint8_t h[44];
    assert(std::fread(h, 1, 44, in) == 44 && note_maintenance::readLe32(h + 40) == partBytes);
    uint32_t n = 0, crc = 0;
    while (auto len = std::fread(batched.data(), 1, batched.size(), in)) {
      joined = checksum(joined, batched.data(), len);
      crc = checksum(crc, batched.data(), len);
      n += len;
    }
    assert(std::fclose(in) == 0 && n == partBytes && crc == rows[i].crc);
  }
  assert(joined == micCrc);
  std::printf("frames=%u parts=%zu mic_crc=%08x wav_crc=%08x high_water=%u blocks\n", frame, rows.size(),
              micCrc, joined, ring.highWaterBlocks());
  // Simulate a power cut in part 2 after its first PCM, before its header checkpoint.
  for (unsigned i = 0; i < rows.size(); ++i) assert(std::filesystem::remove(path(wav, i)));
  assert(std::filesystem::remove(wav + ".parts"));
  auto cut = wav + ".cut";
  assert(append(cut, 0, rows[0]));
  FILE* tail = std::fopen(path(cut, 1).c_str(), "wb");
  assert(tail && header(tail, 0) && std::fwrite(batched.data(), 1, 512, tail) == 512 &&
         std::fclose(tail) == 0);
  assert(recover(cut, checksum) && read(cut, rows) == State::Interrupted && rows.size() == 2 &&
         rows[1].bytes == 512 && rows[1].crc == checksum(0, batched.data(), 512));
}
#ifdef PALA_TEST_RECORDING_SCREEN
namespace screen {
#define PALA_STAGE3 1
constexpr int W=200,H=200,BLACK=0,WHITE=1,kInset=14,kRight=186,kContentWidth=172,kHeaderY=10;
constexpr int kHomeHeaderCenterY=17,kHeaderRuleY=32,kHomeBatteryX=153,kBatteryWidth=33;
constexpr int kBatteryHeight=12,kBatteryBodyWidth=30,kBatteryContactWidth=3,kFooterTextY=170;
std::vector<std::string> labels;
std::atomic<const char*> lockedCaption{"ZAMKNUTE"},stopCaption{"Either button stops"};
bool dot=false,lower=false;
int percent=55,shownBattery=-2;
uint32_t elapsed=0;
void fillRect(int,int,int,int,int) {}
void strokeRect(int,int,int,int,int,int) {}
void hline(int,int,int,int) {}
void fillCircle(int,int,int,int color) { dot=color==WHITE; }
void drawStr(int,int,const char* text,int,int) { labels.emplace_back(text); }
void drawStrC(int,int,const char* text,int,int) { labels.emplace_back(text); }
void labelRight(int y,const char* text,int,int color) { lower=y==kFooterTextY && color==WHITE; labels.emplace_back(text); }
void drawTimer(uint32_t ms,int) { elapsed=ms; }
void drawBattery(int value) { shownBattery=value; }
struct Battery { int percent; };
Battery readBattery() { return {percent}; }
#include "recording_screen_host.inc"
void test() {
  for (bool fallback:{false,true}) {
    labels.clear(); dot=false; shownBattery=-2;
    paintRecordingScreen(fallback);
    paintRecordingStatus(0,false,false);
    assert(dot && shownBattery==55 && labels==std::vector<std::string>{"REC"});
  }
  labels.clear();
  paintRecordingProgress(5008);
  paintRecordingStatus(5008,true,false);
  assert(lower && elapsed==5008 && labels.back()=="LOCK");
  labels.clear(); percent=-1;
  paintRecordingStatus(61000,false,true);
  assert((labels==std::vector<std::string>{"?","ZAMKNUTE","Either button stops"}) && elapsed==61000);
}
}
#endif

int main(int argc, char** argv) {
  assert(argc == 2);
#ifdef PALA_TEST_RECORDING_SCREEN
  screen::test();
#endif
  hour((std::filesystem::path(argv[1]) / "hour.wav").string());
  display_frames::Mailbox panel;
  panel.firstPcm(91);
  panel.publishCapture(91, display_frames::Image::Recording);
  const auto firstFrame = panel.claim();
  assert(panel.recordingStatus(91, 5008, true, false) && panel.claim().offer);
  assert(panel.recordingStatus(91, 5120, false, true));
  panel.publishCapture(91, display_frames::Image::Progress, true, 3900);
  const auto confirmed = panel.claim();
  assert(!firstFrame.locked && confirmed.locked && !confirmed.offer && confirmed.elapsed == 5120);
  panel.end(91);
  panel.firstPcm(93);
  assert(!panel.recordingStatus(91, 60000, false, true));
  panel.publishCapture(93, display_frames::Image::Recording);
  assert(!panel.claim().locked);
  RecordingLock lock;
  assert(!lock.update(4999, true, false).offer);
  assert(lock.update(5000, true, false).offer);
  assert(!lock.update(5010, true, true).locked);
  assert(lock.update(5050, true, true).locked);
  assert(!lock.update(5060, false, true).stop);
  assert(!lock.update(5100, false, false).stop);
  lock.update(5140, false, false);
  lock.update(5150, false, true);
  assert(lock.update(5180, false, true).stop);
  RecordingLock upper;
  upper.update(5000, true, false);
  upper.update(5010, true, true);
  upper.update(5050, true, true);
  upper.update(5080, false, false);
  upper.update(5120, false, false);
  upper.update(5150, true, false);
  assert(upper.update(5180, true, false).stop);
  RecordingLock early;
  early.update(4900, true, true);
  assert(!early.update(5100, true, true).locked);
  assert(early.update(5200, false, true).stop);
  const std::string wav = (std::filesystem::path(argv[1]) / "take.wav").string();
  std::vector<Entry> rows;
  assert(read(wav, rows) == State::Single);
  assert(remaining(9599800, 1024) == 200);
  assert(append(wav, 0, {partBytes, 123}));
  assert(read(wav, rows) == State::Open && rows.size() == 1);
  assert(append(wav, 1, {512, 444}));
  assert(seal(wav, 2, false));
  assert(read(wav, rows) == State::Complete && rows.size() == 2 && rows[1].crc == 444);
  auto source = path(wav, 1);
  std::ofstream(source) << "test";
  auto dest = wav + ".archive";
  assert(moveSidecars(wav, dest));
  assert(std::filesystem::exists(path(dest, 1)) && read(dest, rows) == State::Complete);
  assert(moveSidecars(wav, dest));
  TranscriptArchive text;
  text.start(wav, "usb");
  assert(!text.piece(1, "bad", 3) && text.piece(0, "one", 3) && text.piece(1, "", 0));
  assert(!text.finish(3) && text.piece(2, "two", 3) && text.finish(3));
  std::ifstream archived(wav + ".transcript");
  assert(std::string(std::istreambuf_iterator<char>(archived), {}) == "one two");
  archived.close();
  auto recovered = wav + ".replayed";
  TranscriptArchive interrupted;
  interrupted.start(recovered, "usb");
  assert(interrupted.piece(0, "complete dictation", 18));
  assert(interrupted.finish(1, "routed preview"));
  assert(recoverTranscript(recovered, recovered + ".txt") &&
         recoverTranscript(recovered, recovered + ".txt"));
  std::ifstream preview(recovered + ".txt");
  assert(std::string(std::istreambuf_iterator<char>(preview), {}) == "routed preview");
  text.start(wav, "wifi");
  assert(text.piece(0, "one two", 7) && text.finish(1));
}
