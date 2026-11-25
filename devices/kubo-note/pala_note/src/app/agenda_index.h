#pragma once

#include "tasks.h"
#include <cstdint>

struct AgendaIndexCursor {
  uint32_t next = 0;
  uint32_t end = 0;
  uint32_t generation = 0;
  bool recent = false;
  uint8_t mask = 0;
  uint8_t match = 0;
};
using AgendaDateVisitor = bool (*)(const char* dueUtc, bool event, void*);
using AgendaVisitor = bool (*)(const BrainTaskRecord&, void*);

bool openAgendaIndex(AgendaIndexCursor& cursor, bool recent, const char* fromUtc = nullptr);
bool visitAgendaIndex(AgendaIndexCursor& cursor, AgendaVisitor visit, void* context,
                      uint32_t keyBudget = UINT32_MAX, bool* moreKeys = nullptr);
bool visitAgendaDates(AgendaIndexCursor& cursor, AgendaDateVisitor visit, void* context,
                      uint32_t budget, bool& finished);
void agendaTaskCounts(uint32_t& total, uint32_t& open);
uint32_t agendaIndexGeneration();
