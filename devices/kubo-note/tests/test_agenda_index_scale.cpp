#include "../pala_note/src/app/agenda_index_order.h"

#include <array>
#include <cassert>
#include <chrono>
#include <cstdio>
#include <fstream>
#include <iostream>
#include <string>

using namespace agenda_index;
namespace {
constexpr unsigned kRunWidth = 64;

bool readKey(std::ifstream& file, uint32_t index, Key& key) {
  file.seekg(static_cast<std::streamoff>(index) * sizeof(Key));
  return static_cast<bool>(file.read(reinterpret_cast<char*>(&key), sizeof(Key)));
}

void exercise(uint32_t count, const std::string& prefix) {
  const auto started = std::chrono::steady_clock::now();
  std::string input = prefix + "-a.bin", output = prefix + "-b.bin";
  {
    std::ofstream first(input, std::ios::binary | std::ios::trunc);
    std::array<Key, kRunWidth> run{};
    for (uint32_t start = 0; start < count; start += kRunWidth) {
      const uint32_t length = std::min(count - start, kRunWidth);
      for (uint32_t i = 0; i < length; ++i) {
        Key& key = run[i];
        key.note = start + i + 1;
        key.offset = key.note * 70;
        const uint32_t day = (key.note * 571) % 1095;
        std::snprintf(key.due, sizeof(key.due), "%04u-%02u-%02uT08:30:00Z",
                      2025 + day / 365, 1 + day % 12, 1 + day % 28);
      }
      std::sort(run.begin(), run.begin() + length, dueBefore);
      first.write(reinterpret_cast<const char*>(run.data()), length * sizeof(Key));
    }
    assert(first);
  }
  for (uint32_t width = kRunWidth; width < count; width *= 2) {
    std::ifstream left(input, std::ios::binary), right(input, std::ios::binary);
    std::ofstream out(output, std::ios::binary | std::ios::trunc);
    for (uint32_t start = 0; start < count; start += 2 * width) {
      MergeRun run;
      run.begin(start, width, count);
      while (!run.done()) {
        const uint32_t before = run.left + run.right;
        assert(run.step(64, [&](uint32_t n, Key& key) { return readKey(left, n, key); },
               [&](uint32_t n, Key& key) { return readKey(right, n, key); }, [&](const Key& key) {
                 return static_cast<bool>(out.write(reinterpret_cast<const char*>(&key), sizeof(Key)));
               }));
        assert(run.left + run.right - before <= 64);
      }
    }
    out.close();
    left.close();
    right.close();
    std::swap(input, output);
  }
  {
    std::ifstream sorted(input, std::ios::binary);
    Key previous{}, key{};
    uint64_t sum = 0;
    for (uint32_t i = 0; i < count; ++i) {
      assert(readKey(sorted, i, key));
      assert(!i || dueBefore(previous, key));
      assert(key.offset == key.note * 70);
      sum += key.note;
      previous = key;
    }
    assert(sum == static_cast<uint64_t>(count) * (count + 1) / 2);
    uint32_t lower = 0;
    uint32_t reads = 0;
    const char* due = "2026-03";
    assert(lowerBoundDue(count, due, [&](uint32_t i, Key& found) {
      ++reads;
      return readKey(sorted, i, found);
    }, lower));
    uint32_t maximumReads = 0;
    for (uint32_t range = count; range; range /= 2) ++maximumReads;
    assert(reads <= maximumReads);
    assert(lower > 0 && lower < count);
    assert(readKey(sorted, lower - 1, key) && std::strcmp(key.due, due) < 0);
    assert(readKey(sorted, lower, key) && std::strcmp(key.due, due) >= 0);
    std::cout << count << " notes, bounded build buffer " << kRunWidth * sizeof(Key)
              << " bytes, date seek " << reads << " key reads, "
              << std::chrono::duration_cast<std::chrono::milliseconds>(
                     std::chrono::steady_clock::now() - started).count() << " ms host disk\n";
  }
  std::remove(input.c_str());
  std::remove(output.c_str());
}
}  // namespace

int main(int argc, char** argv) {
  if (argc != 2) return 2;
  exercise(10000, std::string(argv[1]) + "-10k");
  exercise(50000, std::string(argv[1]) + "-50k");
  exercise(100000, std::string(argv[1]) + "-100k");
}
