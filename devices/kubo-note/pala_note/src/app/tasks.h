#pragma once

#include <Arduino.h>
#include <vector>

struct BrainTaskRecord {
  String id;
  int noteNum = -1;
  String text;
  String createdUtc;
  String dueUtc;
  String type = "todo";
  String datePrecision = "none";
  String updatedUtc;
  bool done = false;
};

std::vector<BrainTaskRecord> loadBrainTasks();
bool loadAgendaTasksForNote(int noteNum, std::vector<BrainTaskRecord>& tasks);
using AgendaStateVisitor = bool (*)(const String& id, bool done, const String& updated, void*);
bool visitAgendaStateBatch(uint32_t& offset, uint32_t limit, bool& end, AgendaStateVisitor visit, void* context);
bool verifyAgendaIndexBatch(uint32_t& position, bool finalPass = false);
bool beginAgendaIndexChange(int noteNum);
void finishAgendaIndexChange(int noteNum);
bool verifyAgendaSources();
bool requestAgendaIndexRebuild(bool verifySources = false);
bool maintainAgendaIndex();
int agendaIndexRebuildProgress();
bool agendaIndexReady();
std::vector<BrainTaskRecord> priorityBrainTasks(size_t limit = 3, int* openCount = nullptr);
enum class TaskEdit { Saved, Invalid, Missing, StorageError };
TaskEdit editBrainTask(const String& id, bool done);
bool setBrainTaskDone(const String& id, bool done);
int openBrainTaskCount();
void clearBrainTaskState();
bool applyBrainTaskCommand(const String& speech, String& response, int& sourceNote);
