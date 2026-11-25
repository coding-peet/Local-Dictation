#include "../pala_note/src/app/recording_owner.h"
#include <cassert>
#include <chrono>
#include <filesystem>
#include <fstream>
#include <iostream>

int main(int argc, char** argv) {
  std::string owner;
  if (argc == 3) {
    if (!recording_owner::resolve(argv[1], argv[2], owner)) return 2;
    std::cout << owner;
    return owner == argv[2] ? 0 : 3;
  }
  namespace fs = std::filesystem;
  const auto dir = fs::temp_directory_path() /
      ("pala-owner-" + std::to_string(std::chrono::steady_clock::now().time_since_epoch().count()));
  assert(fs::create_directory(dir));
  const auto path = (dir / "note_042.bridge-owner").string();
  assert(!recording_owner::resolve(path, "", owner));
  assert(recording_owner::resolve(path, "bridge-A", owner) && owner == "bridge-A");
  assert(recording_owner::resolve(path, "bridge-B", owner) && owner == "bridge-A");
  fs::rename(path, path + ".bak");
  assert(recording_owner::resolve(path, "bridge-B", owner) && owner == "bridge-A");
  { std::ofstream corrupt(path); corrupt << "damaged receipt"; }
  assert(!recording_owner::resolve(path, "bridge-B", owner));
  fs::remove(path);
  fs::remove(path + ".bak");
  { std::ofstream partial(path + ".tmp"); partial << "bridge-A"; }
  assert(recording_owner::resolve(path, "bridge-B", owner) && owner == "bridge-B");
  fs::remove(path);
  fs::remove(dir);
  std::cout << "owner: two peers, backup, read error, interrupted claim passed" << std::endl;
}
