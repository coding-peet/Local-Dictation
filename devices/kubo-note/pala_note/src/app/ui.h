#pragma once

#include <Arduino.h>
#include "backlog_transport.h"

// Page visibility and order are managed in the shared Note settings.
void showIdle(int pendingCount = 0, int batteryPercent = -1);
enum class AgendaNavigation { EventsShortcut, LastDisplayPage, MoreDisplayPages };
void showTodayAgenda(bool events, int page, AgendaNavigation navigation);
void showCalendarMonth(int monthOffset, bool moreDisplayPages);
void resetTodayAgenda();
int todayAgendaPageCount(bool events);
void cacheRecordingCaptions();
void paintPriorSaveIndicator();
void paintRecordingScreen(bool fallback);
void paintRecordingStatus(uint32_t elapsed,bool offer,bool locked);
void paintRecordingProgress(uint32_t elapsedMs);
void paintSavingScreen();
void showSaved(int noteNumber, uint32_t edge);
void showMemoryProcessing(int noteNumber, const char* phase);
void showBacklogProcessing(int completed, int total, const char* phase,
                           BacklogLink link = BacklogLink::Deferred,
                           void (*serviceConnection)() = nullptr);
void paintQuestionScreen();
void showAskThinking();
void showAskProgress(const char* action, const char* object = nullptr);
void showAskSpeaking(const String& answer, int sourceCount);
int askAnswerPageCount(const String& answer);
void showAskSpeakingPage(const String& answer, int sourceCount, int page);
void showTransferConnecting(int batteryPercent);
void showNotePairingWait(bool bluetooth);
void showNoteUsbPairingPrompt(const String& peer, const String& id);
void showNoteRestartPrompt(bool audit);
void showNoteRestartExpired();
struct NoteStorageAuditProgress;
void showNoteStorageAuditProgress(const NoteStorageAuditProgress& progress);
void showNoteBluetoothPairingNumber(uint32_t code);
enum class CompanionLink { Offline, Wifi, Hotspot };
void showTransferMode(CompanionLink link, const String& network, const String& ip, const String& hostname,
                      const String& apPassword, int batteryPercent, int volumeOverlay);
void showSetupRequired();
void showError(const char* message);
void showBootError(const char* message);
#if PALA_USB_CARD_SERVICE
void showUsbCardService();
#endif
void showSleepLocked();
void showUltraSleepScreen();

struct CalendarProposalTime {
  String start;
  String end;
};
int chooseCalendarEventDate(const String& title, const CalendarProposalTime& first, const CalendarProposalTime& second,
                            bool (*serviceConnection)() = nullptr);
int confirmCalendarEvent(const String& title, const CalendarProposalTime& time, int minutes,
                         bool (*serviceConnection)() = nullptr);
