#pragma once
#include <stdbool.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

// The recording caller may retry. The codec has produced no samples.
enum { AUDIO_RX_RECONFIGURING = -2 };

typedef struct {
    bool monitored;
    uint32_t overruns;
    uint32_t bytes;
} AudioDmaSnapshot;

// Counts IDF RX queue drops on the Note's microphone channel across its lifetime.
// Size is the raw DMA descriptor in bytes (both input slots).
AudioDmaSnapshot audio_rx_dma_snapshot(void);

#ifdef __cplusplus
}
#endif
