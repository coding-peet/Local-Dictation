#pragma once
#include <Arduino.h>

struct PalaPerformanceStats {
  uint32_t transcriptionMs = 0;
  uint32_t embeddingMs = 0;
  uint32_t answerMs = 0;
  uint32_t speechMs = 0;
};

PalaPerformanceStats palaPerformanceStats();
void palaSetBackgroundNetworkMode(bool active);
bool palaBackgroundNetworkMode();

bool transcribe(const String& wavPath, int noteNum);
bool transcribeQuestion(const String& wavPath, String& question);
bool understandNote(int noteNum);
using OrganizationPost = bool (*)(const char*, const String&, String&, uint16_t, String*, const String&);
String recordingBridge(int note, const String& candidate);
void confirmWifiTranscriptReceipt(int note, const String& peer);
bool understandNoteOverBluetooth(int noteNum, OrganizationPost post, bool (*serviceConnection)(), const String& peer);
bool answerFromBrain(const String& question, String& answer, std::vector<int>& sourceNotes);
bool answerAgendaQuestion(const String& question, String& answer, std::vector<int>& sourceNotes);
bool buildDailyBriefing(String& answer, int& taskCount, int& eventCount);
bool buildWeeklyBriefing(String& answer, int& taskCount, int& eventCount);
bool synthesizeBrainSpeech(const String& text, const char* outputPath, bool fast = true);
bool playBrainSpeech(const String& text, const char* fallbackPath, int sourceCount, bool fast = true);
int processPendingNotes(int preferredNote = -1, int maxNotes = 1);
bool recordingRequestedDuringSync();

void setupTransferServer();
void maintainCompanionAgenda();
void stopTransferMode();
