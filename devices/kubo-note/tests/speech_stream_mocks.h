#pragma once

#include <cstddef>
#include <cstdint>

constexpr int BTN_REC = 1;
constexpr int HIGH = 1;
constexpr int MALLOC_CAP_SPIRAM = 1;
constexpr int MALLOC_CAP_8BIT = 2;
constexpr int pdPASS = 1;
constexpr unsigned pdMS_TO_TICKS(unsigned milliseconds) { return milliseconds; }

int digitalRead(int pin);
uint32_t millis();
void delay(unsigned milliseconds);
void vTaskDelay(unsigned milliseconds);
void vTaskDelete(void* task);
int xTaskCreatePinnedToCore(void (*worker)(void*), const char*, unsigned, void* context,
                            int, void*, int);
void* heap_caps_malloc(size_t size, int caps);
void heap_caps_free(void* pointer);
int palaPlaybackVolume();
void audio_playback_set_vol(int volume);
int audio_playback_write(void* buffer, size_t bytes);
void audio_idle_suspend();

struct MockSerial {
  void printf(const char*, ...) {}
};
inline MockSerial Serial;
