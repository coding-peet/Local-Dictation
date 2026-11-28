// Link this host test with pala_note/src/app/battery.cpp and add tests/battery_hal to the include path.
#include "battery_hal/Arduino.h"
#include "../pala_note/src/app/battery.h"
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdlib>
#include <future>
#include <iostream>
#include <thread>
#include <vector>

namespace {
struct Stop {};
std::mutex control;
std::condition_variable changed;
std::thread worker;
std::atomic<uint32_t> now{0};
uint32_t pinMillivolts = 1700;
unsigned parked = 0, lastDelay = 0, conversions = 0, foregroundConversions = 0;
unsigned created = 0, foregroundDelays = 0;
bool resume = false, stop = false, adcConfigured = false;
bool pauseInAdc = false, adcWaiting = false;
std::thread::id owner;

void require(bool ok, const char* message) {
  if (!ok) { std::cerr << message << std::endl; std::_Exit(1); }
}

void awaitSample(std::unique_lock<std::mutex>& lock, unsigned previous) {
  require(changed.wait_for(lock, std::chrono::seconds(2), [&] { return parked > previous; }),
          "sampler did not produce a sample independently of foreground reads");
  require(lastDelay == 50, "sampler did not wait 50 ms before the next conversion");
}

void step(uint32_t at, uint32_t calibratedPinMillivolts) {
  std::unique_lock<std::mutex> lock(control);
  const unsigned previous = parked;
  now = at;
  pinMillivolts = calibratedPinMillivolts;
  resume = true;
  changed.notify_all();
  awaitSample(lock, previous);
}

void checkReading(uint16_t millivolts, int percent) {
  BatteryReading r = readBattery();
  require(r.valid && r.millivolts == millivolts && r.percent == percent,
          "incorrect battery mean or voltage-to-percent mapping");
}
}

uint32_t millis() { return now; }
void delay(uint32_t) { ++foregroundDelays; }
void analogReadResolution(uint8_t bits) { require(bits == 12, "ADC resolution"); }
void analogSetPinAttenuation(uint8_t pin, uint8_t attenuation) {
  require(pin == 4 && attenuation == ADC_11db, "ADC1 GPIO4 attenuation");
  adcConfigured = true;
}
uint32_t analogReadMilliVolts(uint8_t pin) {
  std::unique_lock<std::mutex> lock(control);
  require(pin == 4, "unexpected ADC pin");
  if (pauseInAdc) {
    adcWaiting = true;
    changed.notify_all();
    changed.wait(lock, [] { return !pauseInAdc; });
    adcWaiting = false;
  }
  if (std::this_thread::get_id() != owner) ++foregroundConversions;
  ++conversions;
  return pinMillivolts;
}
void vTaskDelay(TickType_t ticks) {
  std::unique_lock<std::mutex> lock(control);
  ++parked;
  lastDelay = ticks;
  changed.notify_all();
  changed.wait(lock, [] { return resume || stop; });
  if (stop) throw Stop{};
  resume = false;
}
TaskHandle_t xTaskCreateStaticPinnedToCore(void (*entry)(void*), const char*,
    uint32_t, void* data, unsigned priority, StackType_t*, StaticTask_t* task, int) {
  require(++created == 1 && priority == 1, "expected one low-priority ADC worker");
  worker = std::thread([=] {
    owner = std::this_thread::get_id();
    try { entry(data); } catch (const Stop&) {}
  });
  return task;
}

int main() {
  const auto cold = readBattery();
  require(foregroundConversions == 0 && foregroundDelays == 0, "readBattery performed a synchronous conversion or delay");
  require(!cold.valid && cold.percent == -1 && cold.millivolts == 0, "cold reading must be unknown");
  batteryBegin();
  batteryBegin();
  {
    std::unique_lock<std::mutex> lock(control);
    awaitSample(lock, 0);
    require(adcConfigured && conversions == 1, "startup should configure and publish one sample");
  }
  checkReading(3400, 0);
  require(readBattery().sampledAtMs == 0, "valid timestamp at boot");
  for (unsigned i = 0; i < 30; ++i) checkReading(3400, 0);
  require(conversions == 1 && foregroundConversions == 0 && foregroundDelays == 0,
          "extra readers caused ADC activity or a foreground delay");

  for (unsigned i = 1; i <= 9; ++i) step(50 * i, 1900);
  checkReading(3760, 41);  // One 3400 and nine 3800 mV samples.
  step(500, 2100);
  checkReading(3840, 60);  // Oldest sample expired. Nine 3800 and one 4200 remain.
  step(550, 0);
  checkReading(3844, 60);  // Eight 3800 and one 4200. A failed ADC conversion has no weight.
  require(readBattery().sampledAtMs == 500, "invalid ADC conversion advanced timestamp");
  step(1050, 2300);
  auto stale = readBattery();
  require(!stale.valid && stale.percent == -1 && stale.millivolts == 0, "stale or out-of-range battery shown as valid");
  require(stale.sampledAtMs == 500, "stale snapshot lost the time of the last valid sample");
  step(1110, 2100);
  checkReading(4200, 100);
  now = 1610;
  require(!readBattery().valid, "expired sample remained valid while sampler was unscheduled");
  const unsigned beforeGap = conversions;
  step(10000, 1895);
  checkReading(3790, 50);
  require(conversions == beforeGap + 1, "sampler replayed missed conversions after a stall");

  step(UINT32_MAX - 25, 1800);
  step(24, 1900);
  checkReading(3700, 26);
  require(readBattery().sampledAtMs == 24, "timestamp failed across wrap");

  // Stall a real conversion. Another caller must return the previous coherent snapshot.
  std::unique_lock<std::mutex> lock(control);
  const unsigned previous = parked;
  pauseInAdc = true;
  now = 74;
  pinMillivolts = 1900;
  resume = true;
  changed.notify_all();
  require(changed.wait_for(lock, std::chrono::seconds(2), [] { return adcWaiting; }), "ADC did not enter");
  lock.unlock();
  auto reader = std::async(std::launch::async, readBattery);
  require(reader.wait_for(std::chrono::seconds(1)) == std::future_status::ready,
          "reader blocked behind a conversion");
  const auto previousReading = reader.get();
  require(previousReading.valid && previousReading.sampledAtMs == 24 && previousReading.millivolts == 3700,
          "in-progress conversion changed the published reading");
  lock.lock();
  pauseInAdc = false;
  changed.notify_all();
  awaitSample(lock, previous);
  lock.unlock();

  step(50000, 1400);
  checkReading(2800, 0);
  std::atomic<bool> run{true};
  std::atomic<unsigned> firstSeen{0}, lastSeen{0};
  constexpr unsigned last = 201;
  auto awaitReaders = [](std::atomic<unsigned>& mask) {
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
    while (mask.load() != 7 && std::chrono::steady_clock::now() < deadline) std::this_thread::yield();
    require(mask.load() == 7, "three readers did not observe the published snapshot");
  };
  std::vector<std::thread> readers;
  for (unsigned n = 0; n < 3; ++n) {
    readers.emplace_back([&, n] {
      while (run) {
        const auto value = readBattery();
        if (value.valid) {
          const bool high = ((value.sampledAtMs - 50000) / 501) % 2 == 1;
          require(value.millivolts == (high ? 4500 : 2800) && value.percent == (high ? 100 : 0),
                  "reader observed fields from different samples");
          if (value.sampledAtMs == 50000) firstSeen.fetch_or(1U << n);
          if (value.sampledAtMs == 50000 + last * 501) lastSeen.fetch_or(1U << n);
        } else {
          require(value.millivolts == 0 && value.percent == -1, "invalid snapshot leaked placeholder values");
        }
      }
    });
  }
  awaitReaders(firstSeen);
  for (unsigned i = 1; i <= last; ++i) step(50000 + i * 501, i % 2 ? 2250 : 1400);
  awaitReaders(lastSeen);
  run = false;
  for (auto& r : readers) r.join();
  {
    std::lock_guard<std::mutex> shutdownLock(control);
    stop = true;
    changed.notify_all();
  }
  worker.join();
  std::cout << "PASS cached startup, independent sampler, 10 calibrated values, bounds, stale expiry, timer wrap, stalled ADC and three concurrent readers\n";
}
