#pragma once

#include <Arduino.h>
#include "backlog_transport.h"

// The device UI intentionally exposes only Record, Ask, processing, answer,
// Companion, error, and sleep. Organization belongs to the Web Companion.
void showIdle(int pendingCount = 0, int batteryPercent = -1);
void showTodayAgenda(bool events, int page = 0);
int todayAgendaPageCount(bool events);
void showRecording();
void showRecordingProgress(uint32_t elapsedMs, int moments);
void showSaved(int noteNumber);
void showMemoryProcessing(int noteNumber, const char* phase);
void showBacklogProcessing(int completed, int total, const char* phase,
                           BacklogLink link = BacklogLink::Deferred,
                           void (*serviceConnection)() = nullptr);
void showAskListening();
void showAskThinking();
void showAskProgress(const char* action, const char* object = nullptr);
void showAskSpeaking(const String& answer, int sourceCount);
int askAnswerPageCount(const String& answer);
void showAskSpeakingPage(const String& answer, int sourceCount, int page);
void showTransferConnecting();
void showNotePairingWait(bool bluetooth);
void showNoteUsbPairingPrompt(const String& peer);
void showNoteBluetoothPairingNumber(uint32_t code);
enum class CompanionLink { Offline, Wifi, Hotspot };
void showTransferMode(CompanionLink link, const String& network, const String& ip,
                      const String& apPassword, int volume);
void showCompanionVolume(int percent);
void showSetupRequired();
void showError(const char* message);
void showSleepLocked();
void showUltraSleepScreen();

int confirmCalendarEvent(const String& title, const String& start, const String& end, int minutes);
