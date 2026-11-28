#define PALA_RECORDER_FIRST 1
#define PALA_STAGE3 1

#include "stage3_archive_host.h"
#include "archive_month.h"
#include <cassert>

unsigned confirmations = 0;
archive_month::Stage confirmArchiveMonthDeletion(archive_month::Month month, std::string_view token) {
  ++confirmations;
  return month.key == 202612 && token == "approval-token" ? archive_month::Stage::Accepted
                                                           : archive_month::Stage::Changed;
}
bool archiveMonthDeletionFailed() { return false; }
std::optional<ArchiveMonthConfirmation> prepareArchiveMonthDeletion(archive_month::Month month) {
  return ArchiveMonthConfirmation{month, {8, 2, 1, 11, 125}, "approval-token"};
}

#include "stage3_archive_handlers.inc"

namespace {
std::string filename(int id, const char* extension) {
  return "note_" + std::to_string(id) + "." + extension;
}

int id(uint32_t sequence, const char* date = "2026-12-04T08:00:00Z") {
  return monthly_recordings::makeId(sequence, date);
}

void reset() {
  fixture::rec = false;
  fixture::maxRead = 0;
  fixture::closedFiles = 0;
  fixture::closedDirectories = 0;
  fixture::directories.clear();
  fixture::files.clear();
  fixture::openedDirectories.clear();
  fixture::openedFiles.clear();
  transferServer = {};
  monthly_recordings::firstNewSequence = 1;
}

size_t occurrences(std::string_view text, std::string_view word) {
  size_t count = 0;
  for (size_t cursor = 0; (cursor = text.find(word, cursor)) != text.npos; cursor += word.size())
    ++count;
  return count;
}

void testValidateMonthBeforeOpeningCardAndLoadOnlyTheSelectedDirectory() {
  reset();
  transferServer.args["month"] = "../";
  handleRecorderArchive();
  assert(transferServer.status == 400 && fixture::openedDirectories.empty());

  transferServer = {};
  fixture::directories["/sdcard/recordings"] = {"202612", "202613", "000000", "temp"};
  handleRecorderArchive();
  assert(transferServer.status == 200 && transferServer.complete);
  assert(transferServer.body.find("month=202612") != std::string::npos);
  assert(transferServer.body.find("Time not set") != std::string::npos);
  assert(transferServer.body.find("month=202613") == std::string::npos);
  assert(fixture::openedDirectories == std::vector<std::string>{"/sdcard/recordings"});
  assert(fixture::openedFiles.empty());
}

void testMonthAgreementAndPaginationWithoutOpeningWavFiles() {
  reset();
  monthly_recordings::firstNewSequence = 10;
  auto& entries = fixture::directories["/sdcard/recordings/202612"];
  entries.push_back(filename(id(9), "wav"));  // legacy sequence in the wrong root
  entries.push_back(filename(id(60, "2027-01-01T00:00:00Z"), "wav"));
  entries.push_back(filename(id(61), "wav.tmp"));
  for (uint32_t i = 10; i < 55; ++i) entries.push_back(filename(id(i), "wav"));
  transferServer.args["month"] = "202612";
  handleRecorderArchive();
  assert(transferServer.status == 200 && transferServer.complete);
  assert(occurrences(transferServer.body, "<audio") == 40);
  assert(transferServer.body.find("&amp;offset=40") != std::string::npos);
  assert(transferServer.body.find("num=" + std::to_string(id(9))) == std::string::npos);

  transferServer = {};
  transferServer.args = {{"month", "202612"}, {"offset", "40"}};
  handleRecorderArchive();
  assert(occurrences(transferServer.body, "<audio") == 5);
  assert(transferServer.body.find("Next recordings") == std::string::npos);
  assert(fixture::openedFiles.empty());
}

void testMultichunkPlaybackAndDownloadUseExactBytes() {
  reset();
  const std::string wav = std::string("RIFF") + std::string(2500, '\0');
  fixture::files["/recordings/202612/" + filename(id(1), "wav")] = wav;
  for (bool attachment : {false, true}) {
    transferServer = {};
    transferServer.args["num"] = std::to_string(id(1));
    sendFileByNum("wav", "audio/wav", attachment);
    assert(transferServer.status == 200 && transferServer.mime == "audio/wav");
    assert(transferServer.length == wav.size() && transferServer.client().bytes == wav);
    assert(transferServer.headers.count("Content-Disposition") == static_cast<unsigned>(attachment));
  }
  assert(fixture::maxRead == 1024 && fixture::closedFiles == 2);
}

void testRecInterruptsWavAndClosesTheIncompleteResponse() {
  reset();
  fixture::files["/recordings/202612/" + filename(id(1), "wav")] = std::string(4096, 'a');
  transferServer.args["num"] = std::to_string(id(1));
  transferServer.socket.recAfterWrite = true;
  sendFileByNum("wav", "audio/wav", false);
  assert(transferServer.client().bytes.size() == 1024 && !transferServer.client().connected());
  assert(fixture::closedFiles == 1);
}

void setExportFixture() {
  const auto file = filename(id(1), "txt");
  const auto otherMonth = filename(id(2, "2027-01-01T00:00:00Z"), "txt");
  fixture::directories["/sdcard/recordings/202612"] = {file, otherMonth};
  const std::string content = std::string(510, 'a') + "\xc5\xa1\xc5\xa5" + std::string(700, 'b');
  fixture::files["/recordings/202612/" + file] = content;
  transferServer.args = {{"month", "202612"}, {"export", "1"}};
}

void testUtf8ExportStreamsUnchangedAcrossChunkBoundaries() {
  reset();
  setExportFixture();
  handleRecorderArchive();
  assert(transferServer.status == 200 && transferServer.mime == "text/plain; charset=utf-8");
  assert(transferServer.headers.at("Content-Disposition") == "attachment; filename=\"pala-202612.txt\"");
  assert(transferServer.body.find(fixture::files.begin()->second) != std::string::npos);
  assert(transferServer.complete && fixture::maxRead == 512);
  assert(fixture::openedFiles.size() == 1 && fixture::closedDirectories == 1);
}

void testRecAbortsExportBeforeAnotherBlockOrTerminalChunk() {
  reset();
  setExportFixture();
  transferServer.recAfterChunk = true;
  handleRecorderArchive();
  assert(!transferServer.complete && !transferServer.client().connected());
  assert(transferServer.body.find(std::string(512, 'b')) == std::string::npos);
  assert(fixture::maxRead == 512 && fixture::closedDirectories == 1);
}
void testMonthLogsReturnTextOnlyAndAbortWhenRecNeedsTheCard() {
  reset();
  fixture::directories["/sdcard/recordings/202612"] = {"month.log", filename(id(1), "wav"), "../fake.log"};
  const std::string log = "<script>literal</script> \xc5\xa1" + std::string(900, 'z');
  fixture::files["/recordings/202612/month.log"] = log;
  transferServer.args = {{"month", "202612"}, {"logs", "1"}};
  handleRecorderArchive();
  assert(transferServer.status == 200 && transferServer.mime == "text/plain; charset=utf-8");
  assert(transferServer.headers.at("X-Content-Type-Options") == "nosniff");
  assert(transferServer.body.find(log) != std::string::npos && transferServer.complete);
  assert(transferServer.body.find("Transcript saved") != std::string::npos);
  assert(fixture::openedFiles == std::vector<std::string>{"/recordings/202612/month.log"});
  assert(transferServer.body.find("<html>") == std::string::npos);

  transferServer = {};
  transferServer.args = {{"month", "202612"}, {"logs", "1"}};
  transferServer.recAfterChunk = true;
  handleRecorderArchive();
  assert(!transferServer.complete && !transferServer.client().connected());
  assert(transferServer.body.find(std::string(600, 'z')) == std::string::npos);
}
void testMonthConfirmationNamesTheMonthAndBothCounts() {
  reset();
  confirmations = 0;
  transferServer.args["month"] = "202612";
  handleArchiveMonthDeletion(false);
  assert(transferServer.status == 200 && confirmations == 0);
  assert(transferServer.body.find("Delete 2026-12?") != std::string::npos);
  assert(transferServer.body.find("Pending audio: 2") != std::string::npos);
  assert(transferServer.body.find("Pending delivery: 1") != std::string::npos);
  assert(transferServer.body.find("method='post'") != std::string::npos);
  assert(transferServer.body.find("approval-token") != std::string::npos);

  transferServer.args["month"] = "202613";
  handleArchiveMonthDeletion(true);
  assert(transferServer.status == 400 && confirmations == 0);
  transferServer.args["month"] = "202612";
  handleArchiveMonthDeletion(true);
  assert(transferServer.status == 409 && confirmations == 1);
  transferServer.args["confirmation"] = "approval-token";
  handleArchiveMonthDeletion(true);
  assert(transferServer.status == 202 && confirmations == 2);
}
}

void testLongRecordingDownloadAndTranscript() {
  reset();
  const int note=id(1);
  const std::string first="/recordings/202612/"+filename(note,"wav");
  const uint32_t n=recording_parts::partBytes;
  std::string index;
  auto row=[&](unsigned i,recording_parts::Entry e) {
    const uint32_t r[4]={i,e.bytes,e.crc,recording_parts::guard(i,e)};
    index.append(reinterpret_cast<const char*>(r),sizeof(r));
  };
  row(0,{n,0}); row(1,{4,0}); row(2,{0,0});
  fixture::files[first+".parts"]=index;
  fixture::files[first]=std::string(44,0)+std::string(n,'a');
  fixture::files[first+".p1"]=std::string(44,0)+"tail";
  transferServer.args={{"num",String(note)}};
  sendFileByNum("wav","audio/wav",true);
  assert(transferServer.status==200 && transferServer.length==n+48);
  const auto& all=transferServer.socket.bytes;
  assert(all.size()==n+48 && all.substr(all.size()-4)=="tail");
  assert(note_maintenance::readLe32(reinterpret_cast<const uint8_t*>(all.data()+40))==n+4);
  reset();
  fixture::files[first+".transcript"]="all words";
  transferServer.args={{"num",String(note)}};
  sendFileByNum("txt","text/plain",true);
  assert(transferServer.socket.bytes=="all words");
}
int main() {
  testLongRecordingDownloadAndTranscript();
  testValidateMonthBeforeOpeningCardAndLoadOnlyTheSelectedDirectory();
  testMonthAgreementAndPaginationWithoutOpeningWavFiles();
  testMultichunkPlaybackAndDownloadUseExactBytes();
  testRecInterruptsWavAndClosesTheIncompleteResponse();
  testUtf8ExportStreamsUnchangedAcrossChunkBoundaries();
  testRecAbortsExportBeforeAnotherBlockOrTerminalChunk();
  testMonthLogsReturnTextOnlyAndAbortWhenRecNeedsTheCard();
  testMonthConfirmationNamesTheMonthAndBothCounts();
}
