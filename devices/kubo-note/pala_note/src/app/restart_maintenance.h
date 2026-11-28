#pragma once

#include <ArduinoJson.h>
#include "audit_progress.h"

uint32_t requestNoteRestart(NoteRestartMode mode);
void noteRestartDisplayed(NoteRestartRequest request);
bool noteRestartExpired(uint32_t ticket);
void addNoteRestartConfirmation(JsonObject result);
NoteRestartRequest pendingNoteRestartRequest();
// A yes answer, after physical confirmation, persists the audit request if
// needed and resets. A no answer dismisses the one pending request.
bool answerNoteRestart(NoteRestartRequest request, bool accepted);
void addNoteStorageAuditSettings(JsonObject result, size_t knownNoteCount);
bool claimNoteStorageAuditForBoot();
bool runRequestedNoteStorageAudit(NoteStorageAuditProgressCallback notify);
