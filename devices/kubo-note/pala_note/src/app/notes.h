#pragma once
#include <Arduino.h>
#include <vector>
#include "transcript_storage.h"

void   initializeNoteCatalog(bool recoverSources = true);
bool   maintainNoteCatalog();
bool   noteCatalogReady();
bool   noteCatalogBaseLoaded();
bool   beginNoteRecording(int num, const String& capturedUtc);
bool   saveIndex();
bool   addToIndex(int num, const char* tag, bool hasText);
bool   updateIndexHasText(int num);
bool   saveNoteTranscript(int num, const String& text);
bool   deleteNote(int num);
bool   deleteAllNotes();

struct TrashedNote {
  int num;
  String tag;
  int64_t trashedAt;
  String capturedUtc;
};

namespace fs { class File; }
enum class ParkedNoteFile { Transcript, Audio };

bool listTrashedNotes(std::vector<TrashedNote>& entries);
fs::File openParkedNoteFile(int num, ParkedNoteFile kind);
bool restoreTrashedNote(int num);
bool noteIsTrashed(int num);
int  trashRetentionDays();
bool setTrashRetentionDays(int days);
bool emptyTrash(bool& more);

void discoverNoteTrash();
void recoverNoteTrash();
void maintainNoteTrash();
int  highestTrashedNoteNumber();
bool noteHasLiveBundle(int num);
int    nextNoteNumber();
bool   saveTag(int num, const char* tag, const String& capturedUtc);

String noteMetaPath(int num);
String readNoteMetaValue(int num, const char* key);
bool   writeNoteMeta(int num, const char* tag, const String& capturedUtc = String());
struct NoteEntry;
note_transcript::Stored storedNoteTranscript(int num);
bool   reconcileNoteTranscript(int num);
bool   noteHasCommittedTranscript(int num);
bool   notePendingTranscription(const NoteEntry& note);
int    pendingTranscriptionCount();
String noteQueueState(int num);
String noteQueueError(int num);
int    noteQueueRetries(int num);
int    noteQueueFailureCount(int num);
String noteQueueUpdatedUtc(int num);
bool   noteQueueRetryDue(int num);
void   resumeBlockedCloudNotes();
bool   writeNoteQueueState(int num, const char* state, const char* error = nullptr,
                           bool incrementRetry = false, bool progressMade = false);

bool   saveNoteMoments(int num, const std::vector<uint32_t>& momentsMs);
std::vector<uint32_t> loadNoteMoments(int num);
int    noteMomentCount(int num);
void   updateIndexMomentCount(int num, int count);

String noteCreatedUtc(int num);
String utcToLocalDeviceLabel(const String& utcIso);
int    localUtcOffsetMinutes(const String& utcIso);
String noteCreatedDeviceLabel(int num);
String currentUtcIso();

String notePreviewText(int num, size_t maxLen = 90);
