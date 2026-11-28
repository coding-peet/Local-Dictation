#pragma once
#include "owner_trace.h"
#include "esp_attr.h"

namespace owner_trace {

enum class Layer { Work, Vfs, Fat, Command, Event };
struct Token { int slot; uint32_t value; uint32_t pc; };

class Frame {
 public:
  Frame(Layer layer, uint32_t value, uint32_t pc = 0);
  ~Frame();
  Frame(const Frame&) = delete;
 private:
  Layer layer_;
  Token token_;
};

void writerBegin(Evidence& evidence);
void writerEnd();
void IRAM_ATTR blocked(uint32_t mutex, uint32_t holder, uint32_t waiterPc);
void IRAM_ATTR acquired();
void IRAM_ATTR eventBlock();
uint32_t IRAM_ATTR startGive(uint32_t queue, uint32_t& prior);
void IRAM_ATTR endGive(uint32_t generation, uint32_t prior);
void IRAM_ATTR giving(uint32_t holder);

}  // namespace owner_trace
