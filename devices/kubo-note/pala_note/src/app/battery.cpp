#include "battery.h"

#include "../../config.h"
#include <Arduino.h>

namespace {

constexpr size_t kSampleCount = 10;
constexpr uint32_t kSampleIntervalMs = 50;
constexpr uint32_t kWindowMs = kSampleCount * kSampleIntervalMs;
constexpr uint32_t kDividerRatio = 2;  // The board uses two 200 kOhm resistors.
constexpr uint32_t kMinBatteryMv = 2800;
constexpr uint32_t kMaxBatteryMv = 4500;

struct CurvePoint { uint16_t millivolts; uint8_t percent; };

// Resting-voltage approximation for a single-cell Li-ion/LiPo battery.
constexpr CurvePoint kCurve[] = {
  {3400, 0}, {3500, 5}, {3600, 12}, {3680, 22}, {3740, 35}, {3790, 50},
  {3850, 62}, {3920, 74}, {4000, 84}, {4080, 92}, {4160, 98}, {4200, 100}
};

int percentFromVoltage(uint16_t millivolts) {
  if (millivolts <= kCurve[0].millivolts) return 0;
  const size_t last = sizeof(kCurve) / sizeof(kCurve[0]) - 1;
  if (millivolts >= kCurve[last].millivolts) return 100;
  for (size_t i = 1; i <= last; i++) {
    if (millivolts > kCurve[i].millivolts) continue;
    const CurvePoint& low = kCurve[i - 1];
    const CurvePoint& high = kCurve[i];
    return low.percent +
      ((millivolts - low.millivolts) * (high.percent - low.percent)) /
      (high.millivolts - low.millivolts);
  }
  return 0;
}

struct Sample { uint32_t atMs; uint16_t millivolts; };

class VoltageWindow {
 public:
  BatteryReading update(uint32_t atMs, uint32_t pinMillivolts) {
    while (count_ && atMs - samples_[first_].atMs >= kWindowMs) removeFirst();
    if (pinMillivolts >= kMinBatteryMv / kDividerRatio && pinMillivolts <= kMaxBatteryMv / kDividerRatio) {
      if (count_ == kSampleCount) removeFirst();
      const auto millivolts = static_cast<uint16_t>(pinMillivolts * kDividerRatio);
      samples_[(first_ + count_) % kSampleCount] = {atMs, millivolts};
      ++count_;
      total_ += millivolts;
      lastValidAt_ = atMs;
    }

    BatteryReading reading;
    reading.sampledAtMs = lastValidAt_;
    if (count_) {
      reading.millivolts = static_cast<uint16_t>((total_ + count_ / 2) / count_);
      reading.percent = percentFromVoltage(reading.millivolts);
      reading.valid = true;
    }
    return reading;
  }

 private:
  void removeFirst() {
    total_ -= samples_[first_].millivolts;
    first_ = (first_ + 1) % kSampleCount;
    --count_;
  }
  Sample samples_[kSampleCount]{};
  size_t first_ = 0, count_ = 0;
  uint32_t total_ = 0, lastValidAt_ = 0;
};

portMUX_TYPE snapshotLock = portMUX_INITIALIZER_UNLOCKED;
BatteryReading snapshot;

void sampleBattery(void*) {
  VoltageWindow window;
  analogReadResolution(12);
  analogSetPinAttenuation(BAT_ADC_PIN, ADC_11db);
  for (;;) {
    const auto pinMillivolts = analogReadMilliVolts(BAT_ADC_PIN);
    const auto result = window.update(millis(), pinMillivolts);
    portENTER_CRITICAL(&snapshotLock);
    snapshot = result;
    portEXIT_CRITICAL(&snapshotLock);
    // A delayed conversion resumes at normal cadence without replaying missed samples.
    vTaskDelay(pdMS_TO_TICKS(kSampleIntervalMs));
  }
}
}  // namespace

void batteryBegin() {
  // Deep sleep resets the sampler and its ordinary RAM on the next boot.
  constexpr uint32_t stackBytes = 3072;
  alignas(portBYTE_ALIGNMENT) static StackType_t stack[stackBytes / sizeof(StackType_t)];
  static StaticTask_t task;
  static TaskHandle_t sampler = nullptr;
  if (!sampler) {
    sampler = xTaskCreateStaticPinnedToCore(sampleBattery, "battery", stackBytes, nullptr,
                                            tskIDLE_PRIORITY + 1, stack, &task, 0);
  }
}

BatteryReading readBattery() {
  portENTER_CRITICAL(&snapshotLock);
  BatteryReading reading = snapshot;
  portEXIT_CRITICAL(&snapshotLock);
  if (reading.valid && millis() - reading.sampledAtMs >= kWindowMs) {
    reading.millivolts = 0;
    reading.percent = -1;
    reading.valid = false;
  }
  return reading;
}
