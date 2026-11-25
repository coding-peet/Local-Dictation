#include "../pala_note/src/app/capture_wav_recovery.h"
#include "../pala_note/src/app/catalog_storage.h"
#include "../pala_note/src/app/rec_spool.h"
#include "../pala_note/src/app/pcm_resampler.h"
#include <cassert>
#include <chrono>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <iterator>

namespace fs = std::filesystem;
using State = capture_wav::State;
using Bytes = std::vector<uint8_t>;
const char* stamp = "2026-11-24T08:15:12Z";

struct Card {
  fs::path root = fs::temp_directory_path() / ("pala-wave-" +
      std::to_string(std::chrono::steady_clock::now().time_since_epoch().count()));
  std::string notes, inbox, journal, wav;
  Card() {
    assert(fs::create_directory(root));
    assert(fs::create_directory(root / "notes"));
    assert(fs::create_directory(root / "inbox"));
    notes = (root / "notes").string() + "/";
    inbox = (root / "inbox").string() + "/";
    journal = notes + "capture.pending"; wav = notes + "note_017.wav";
  }
  ~Card() { fs::remove_all(root); }
};

static void put(const std::string& file, const Bytes& bytes) {
  std::ofstream out(file, std::ios::binary);
  out.write(reinterpret_cast<const char*>(bytes.data()), bytes.size());
  assert(out.good());
}
static Bytes get(const std::string& file) {
  std::ifstream input(file, std::ios::binary); assert(input);
  return {std::istreambuf_iterator<char>(input), {}};
}
static Bytes recording(const std::vector<int16_t>& pcm, uint32_t checkpoint, bool tornByte = false) {
  Bytes h(44);
  std::memcpy(h.data(), "RIFF", 4);
  capture_wav::putSize(h.data()+4, checkpoint+36);
  std::memcpy(h.data()+8, "WAVEfmt ", 8);
  capture_wav::putSize(h.data()+16,16);
  h[20] = 1; h[22] = 1;
  capture_wav::putSize(h.data()+24, 16000);
  capture_wav::putSize(h.data()+28, 32000);
  h[32] = 2; h[34] = 16;
  std::memcpy(h.data()+36, "data",4);
  capture_wav::putSize(h.data()+40, checkpoint);
  for (int16_t sample : pcm) {
    auto value = static_cast<uint16_t>(sample);
    h.push_back(static_cast<uint8_t>(value)); h.push_back(static_cast<uint8_t>(value>>8));
  }
  if (tornByte) h.push_back(0x9a);
  return h;
}

static void play(const std::string& path, const std::vector<int16_t>& pcm) {
  const auto raw = get(path);
  assert(note_maintenance::checkWav(raw.size(), [&](uint32_t o, uint8_t* dst, size_t n) {
    if (o+n > raw.size()) return false;
    std::memcpy(dst,raw.data()+o,n); return true;
  }));
  assert(note_maintenance::readLe32(raw.data()+4) == raw.size()-8);
  const auto data = note_maintenance::readLe32(raw.data()+40);
  assert(data == 2*pcm.size());
  std::vector<int16_t> decoded;
  for (size_t i=44; i<44+data; i+=2)
    decoded.push_back(static_cast<int16_t>(note_maintenance::readLe16(raw.data()+i)));
  assert(decoded == pcm);
  std::vector<int16_t> stereo(2*pcm.size());
  PalaPcmResampler dac;
  assert(dac.convert(decoded.data(), static_cast<int>(decoded.size()), 1, false, stereo.data()) ==
         static_cast<int>(pcm.size()));
  for (size_t i=0; i<pcm.size(); ++i) assert(stereo[2*i] == pcm[i] && stereo[2*i+1] == pcm[i]);
}

// Simulate loss between two FAT directory updates and an SD short write.
static int failPromotion(const char* from, const char* to) {
  if (std::string(from).find(".recovery-tmp") != std::string::npos) { errno=EIO; return -1; }
  return std::rename(from,to);
}
static size_t cutPayload(const void* data, size_t element, size_t count, std::FILE* f) {
  return std::fwrite(data, element, count == 44 ? count : count/2, f);
}
static std::FILE* transient(const char*, const char*) { errno=EIO; return nullptr; }

static void direct(uint32_t checkpoint, const std::vector<int16_t>& pcm, bool odd) {
  Card c;
  assert(note_catalog::reserveRecording(c.journal, 17, stamp));
  assert(!note_catalog::reserveRecording(c.journal, 18, stamp));
  auto source = recording(pcm, checkpoint, odd);
  put(c.wav, source);
  std::vector<NoteEntry> catalog;
  std::string date;
  bool catalogWritable=false;
  auto resume = [&](int id, const char* utc) {
    assert(id == 17); date=utc;
    if (capture_wav::recover(c.wav) != State::Ready ||
        !capture_wav::restoreTimestamp(c.notes+"note_017.meta",utc,[&](const char* captured) {
          std::ofstream file(c.notes+"note_017.meta"); file << "created_utc=" << captured << "\n";
          return file.good();
        }) || !catalogWritable) return false;
    return note_catalog::recoverRecording(catalog,id,c.wav.c_str(),
        [&](auto where) { catalog.insert(where,{id,"Inbox",false,0}); return true; }, std::fopen,2);
  };
  const auto marker = get(c.journal);
  assert(capture_wav::recover(c.wav,transient) == State::Error);
  assert(capture_wav::recover(c.wav,std::fopen,std::rename,cutPayload) == State::Error);
  assert(get(c.wav)==source && get(c.journal)==marker);
  assert(capture_wav::recover(c.wav,std::fopen,failPromotion) == State::Error);
  assert(!fs::exists(c.wav) && get(c.wav+".recovery-original")==source);
  assert(!note_catalog::recoverReservation(c.journal.c_str(),resume));
  assert(get(c.journal)==marker && get(c.wav+".recovery-original")==source);
  play(c.wav,pcm);
  const auto published = get(c.wav);
  catalogWritable=true;
  assert(note_catalog::recoverReservation(c.journal.c_str(),resume));
  std::string onCard;
  assert(note_transcript::readField((c.notes+"note_017.meta").c_str(),"created_utc=",onCard) && onCard==stamp);
  assert(date==stamp && catalog.size()==1 && catalog.front().num==17);
  assert(get(c.wav)==published && get(c.wav+".recovery-original")==source);
  assert(!fs::exists(c.journal));
}

static void priority(bool afterMove, uint32_t checkpoint) {
  Card c;
  const std::string stem = "s1700000000_abc00003";
  std::vector<int16_t> pcm{-32768,256,0,32767,3};
  const auto input = recording(pcm,checkpoint);
  put(c.inbox+stem+".partial", input);
  std::string promoted;
  assert(rec_spool::promotePartial(c.inbox,stem+".partial",promoted)==State::Ready);
  assert(!fs::exists(c.inbox+stem+".partial") && get(c.inbox+promoted)==input);
  int assigned=0, allocations=0;
  std::string timestamp;
  bool cut=true;
  auto adopt = [&](const std::string& name) {
    return rec_spool::adopt(c.inbox,c.notes,name,assigned,[&] { ++allocations; return 17; },
        [&](int id,uint32_t epoch) {
          // Boot replays the previous journal before each new reservation.
          return note_catalog::recoverReservation(c.journal.c_str(),[&](int,const char*) { return !fs::exists(c.wav); }) &&
              note_catalog::reserveRecording(c.journal,id,rec_spool::captureUtc(epoch).c_str());
        },[&](int,uint32_t epoch) {
          timestamp=rec_spool::captureUtc(epoch);
          if (!afterMove && cut) return false;
          std::ofstream file(c.notes+"note_017.meta");
          file << "created_utc=" << timestamp << "\n";
          return file.good();
        },
        [&](int) { return (!afterMove || !cut) && capture_wav::recover(c.wav)==State::Ready; });
  };
  assert(!adopt(promoted));
  assert(timestamp=="2023-11-14T22:13:20Z" && allocations==1);
  cut=false;
  if (!afterMove) assert(adopt("c17-"+promoted));
  assert(note_catalog::recoverReservation(c.journal.c_str(),[&](int id,const char* utc) {
    return id==17 && timestamp==utc && capture_wav::recover(c.wav)==State::Ready;
  }));
  play(c.wav,pcm);
  std::string onCard;
  assert(note_transcript::readField((c.notes+"note_017.meta").c_str(),"created_utc=",onCard));
  assert(onCard==timestamp);
  assert(get(c.wav+".recovery-original")==input && allocations==1);
}

static void boundaries() {
  Card c;
  std::vector<int16_t> pcm{-27,42};
  const auto closed=recording(pcm,4);
  put(c.wav,closed);
  assert(capture_wav::recover(c.wav)==State::Ready && get(c.wav)==closed);
  assert(!fs::exists(c.wav+".recovery-original"));
  auto other=closed; other[20]=3;
  put(c.wav,other);
  assert(capture_wav::recover(c.wav)==State::Unrecognized && get(c.wav)==other);
  for (size_t size : {size_t(0),size_t(43),size_t(44),size_t(45)}) {
    Bytes empty(closed.begin(),closed.begin()+size); put(c.wav,empty);
    assert(capture_wav::recover(c.wav)==State::Empty && get(c.wav)==empty);
  }
  const int32_t legacy=17;
  Bytes bytes(sizeof(legacy)); std::memcpy(bytes.data(),&legacy,sizeof(legacy));
  put(c.journal,bytes); put(c.wav,recording(pcm,0));
  const auto meta = c.notes + "note_017.meta";
  std::ofstream(meta, std::ios::binary) << "tag=original\r\ncreated_utc=2021-01-01T01:02:03Z\r\ncustom=keep";
  const auto preserved = get(meta);
  assert(note_catalog::recoverReservation(c.journal.c_str(),[&](int id,const char* utc) {
    assert(id==legacy && !*utc); // no timestamp can be inferred from a legacy ID
    assert(capture_wav::restoreTimestamp(meta,utc,[](const char*) { return false; }));
    return capture_wav::recover(c.wav)==State::Ready;
  }));
  play(c.wav,pcm);
  assert(capture_wav::restoreTimestamp(meta,stamp,[](const char*) { return false; }));
  assert(get(meta)==preserved);
  put(c.journal, {'b','a','d','!','!'});
  assert(!note_catalog::recoverReservation(c.journal.c_str(),[](int,const char*) { return true; }));
  assert(fs::exists(c.journal));
}

int main() {
  direct(0,{-32768},false);
  std::vector<int16_t> pcm;
  for (int i=0;i<6203;++i) pcm.push_back(static_cast<int16_t>(i*19));
  direct(4096,pcm,true);
  priority(false,0); priority(true,4); boundaries();
  std::cout<<"normal + inbox cuts, original bytes, WAV headers, sample playback, UTC, legacy and IO faults passed\n";
}
