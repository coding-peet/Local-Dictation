#pragma once
#include <cstddef>
#include <cstdint>
#include <mutex>
using TickType_t = uint32_t;
using StackType_t = uint8_t;
struct StaticTask_t {};
using TaskHandle_t = StaticTask_t*;
using portMUX_TYPE = std::mutex;
#define portMUX_INITIALIZER_UNLOCKED {}
#define portBYTE_ALIGNMENT 16
#define tskIDLE_PRIORITY 0
#define pdMS_TO_TICKS(ms) (ms)
constexpr uint8_t ADC_11db = 11;
uint32_t millis();
void delay(uint32_t ms);
void analogReadResolution(uint8_t bits);
void analogSetPinAttenuation(uint8_t pin, uint8_t attenuation);
uint32_t analogReadMilliVolts(uint8_t pin);
void vTaskDelay(TickType_t ticks);
TaskHandle_t xTaskCreateStaticPinnedToCore(void (*entry)(void*), const char* name,
    uint32_t stackBytes, void* data, unsigned priority, StackType_t* stack,
    StaticTask_t* task, int core);
inline void portENTER_CRITICAL(portMUX_TYPE* lock) { lock->lock(); }
inline void portEXIT_CRITICAL(portMUX_TYPE* lock) { lock->unlock(); }
