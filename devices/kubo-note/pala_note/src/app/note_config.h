#pragma once

#include <Arduino.h>

class WebServer;
enum class NoteConfigTransport { Usb, Bluetooth, Web };

constexpr char kNoteConfigPrefix[] = "PALA_CONFIG_V1 ";
String noteConfigRequest(const String& json, NoteConfigTransport transport, bool authenticated,
                         uint32_t attachment, bool enrollment = false);
void noteConfigSeedEntropy();
bool noteConfigRandom(uint8_t* output, size_t length);
void noteSettingsBegin();
void noteSettingsHttp(WebServer& server);
void noteUsbLinkChanged(bool active);
uint32_t noteUsbAttachment();
bool noteUsbPairingOpen();
bool noteUsbPairingPrompt(String& name, String* id = nullptr);
void noteUsbPairingConfirm(bool accept);
void notePairingDetach(NoteConfigTransport transport, uint32_t attachment);
bool takeNoteBleEnableRequest();
