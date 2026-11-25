#include "../pala_note/src/app/memory_telemetry.h"

constexpr auto reading = heapUsageFromFreeBytes(1000, 725, 590);
static_assert(reading.usedBytes == 275 && reading.peakBytes == 410 && reading.totalBytes == 1000);
static_assert(reading.usedPercent() == 28 && reading.peakPercent() == 41);

constexpr auto busyDuringRead = heapUsageFromFreeBytes(1000, 0, 50);
static_assert(busyDuringRead.usedPercent() == 100 && busyDuringRead.peakPercent() == 100);
static_assert(heapUsageFromFreeBytes(1000, 1250, 1200).usedPercent() == 0);
static_assert(heapUsageFromFreeBytes(0, 0, 0).usedPercent() == -1);
static_assert(heapUsageFromFreeBytes(0, 0, 0).peakPercent() == -1);
static_assert(heapUsageFromFreeBytes(1000, 725, 590, 880).peakPercent() == 88);
static_assert(heapUsageFromFreeBytes(1000, 725, 590, 340).peakPercent() == 41);
static_assert(heapUsageFromFreeBytes(1000, 725, 590, 1500).peakPercent() == 100);
