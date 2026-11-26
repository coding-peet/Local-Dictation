#pragma once

#include "note_config.h"

bool noteAccessConfigured();
bool noteAccessPasswordRequired();
const char* noteAccessSetPasswordRequired(bool required);
bool noteAccessSessionValid(NoteConfigTransport transport, const String& token);
const char* noteAccessLogin(NoteConfigTransport transport, const String& password, String& token);
void noteAccessLogout(NoteConfigTransport transport);
const char* noteAccessSave(bool localUsb, const String& oldPassword, const String& password, const String& repeat);
bool noteAccessGuardHttp(WebServer& server);
void noteAccessHttp(WebServer& server);
