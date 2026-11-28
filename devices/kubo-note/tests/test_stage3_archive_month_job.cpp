#include "archive_month_job.h"

#include <algorithm>
#include <cassert>
#include <filesystem>
#include <set>
#include <vector>

using namespace archive_month;

namespace {
const Month december{202612, false};
const Month january{202701, false};

struct Card {
  std::vector<Member> decemberFiles{
      {.name = "note_1.wav", .size = 2048}, {.name = "month.log", .size = 14},
      {.name = "note_2.txt", .size = 12}, {.note = 1073742932, .pending = Pending::Audio}};
  std::set<std::string> archived{"note_1.wav", "month.log", "note_2.txt"};
  std::set<int> pending{1073742932};
  bool writeBusy = false;
  int removed = 0;
  int providerChanges = 0;

  bool scan(Month month, const Visitor& visit) {
    if (writeBusy) return false;
    if (month == january) return visit({.name = "january.wav", .size = 55});
    assert(month == december);
    return std::all_of(decemberFiles.begin(), decemberFiles.end(), visit);
  }
  bool remove(Month month, const Member& member) {
    assert(month == december);
    if (member.note) pending.erase(member.note);
    else archived.erase(member.name);
    ++removed;
    return true;
  }
};

void run(const std::string& journalPath) {
  monthly_recordings::firstNewSequence = 1;
  Card card;
  const Scan scan = [&](Month month, const Visitor& visit) { return card.scan(month, visit); };
  const auto remove = [&](Month month, const Member& member) { return card.remove(month, member); };
  const auto finish = [](Month) {};
  const auto sync = [](std::FILE*) { return true; };
  const auto snapshot = preview(december, scan);
  assert(snapshot && snapshot->files == 3 && snapshot->audio == 1 && snapshot->delivery == 0);
  assert(!preview({202613, false}, scan));
  assert(!validFilename("../other-month.wav") && !validFilename(".."));

  {
    Job job(journalPath);
    card.decemberFiles.back().pending = Pending::Delivery;
    assert(job.stage(december, *snapshot, scan, sync) == Stage::Changed);
    assert(!job.active() && card.removed == 0);
    card.decemberFiles.back().pending = Pending::Audio;
    card.writeBusy = true;
    assert(job.stage(december, *snapshot, scan, sync) == Stage::Unavailable);
    card.writeBusy = false;
    ++card.decemberFiles.front().modified;
    assert(job.stage(december, *snapshot, scan, sync) == Stage::Changed);
    --card.decemberFiles.front().modified;
    assert(job.stage(december, *snapshot, scan, [](std::FILE*) { return false; }) == Stage::Unavailable);
    assert(!job.active() && card.removed == 0);
    assert(job.stage(december, *snapshot, scan, sync) == Stage::Accepted);
    assert(job.active());
    assert(job.stage(december, *snapshot, scan, sync) == Stage::Unavailable);
    card.decemberFiles.push_back({.name = "new-rec.wav", .size = 44});
    card.archived.insert("new-rec.wav");
    for (int i = 0; card.removed == 0 && i < 10; ++i)
      job.advance(remove, finish, [] { return false; }, 1);
    assert(card.removed == 1);  // power cut after the first unlink
    const int before = card.removed;
    job.advance(remove, finish, [] { return true; }, 4);
    assert(card.removed == before); // REC preempts the batch
  }
  {
    Job job(journalPath);
    SpaceRefresh capacity;
    bool recorderBusy = true;
    int refreshes = 0;
    const auto busy = [&] { return recorderBusy; };
    const auto refresh = [&] { assert(!job.active()); ++refreshes; };
    capacity.service(busy, refresh);
    for (int i = 0; job.active() && i < 20; ++i) {
      capacity.observe(job.advance(remove, finish, [] { return false; }, 1));
      capacity.service(busy, refresh);
    }
    assert(!job.active() && card.archived == std::set<std::string>{"new-rec.wav"});
    assert(capacity.pending() && refreshes == 0);
    recorderBusy = false;
    capacity.service(busy, refresh);
    capacity.service(busy, refresh);
    assert(!capacity.pending() && refreshes == 1);
    assert(card.pending.empty() && card.providerChanges == 0);
  }
  {
    Job job(journalPath);
    const auto nextSnapshot = preview(december, scan);
    assert(nextSnapshot && job.stage(december, *nextSnapshot, scan, sync) == Stage::Accepted);
    std::FILE* bad = std::fopen(journalPath.c_str(), "r+b");
    assert(bad && std::fseek(bad, -1, SEEK_END) == 0);
    assert(std::fputc(1, bad) != EOF && std::fclose(bad) == 0);
    const int before = card.removed;
    for (int i = 0; i < 10; ++i) job.advance(remove, finish, [] { return false; }, 1);
    assert(job.active() && card.removed == before);
  }
  assert(std::remove(journalPath.c_str()) == 0);
}
}

int main(int argc, char** argv) {
  assert(argc == 2 && !std::filesystem::exists(argv[1]));
  run(argv[1]);
}
