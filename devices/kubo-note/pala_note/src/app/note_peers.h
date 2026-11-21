#pragma once

#include <Arduino.h>

constexpr int kNoteMaxPeers = 5;
constexpr size_t kNoteMaxPeerCertificate = 2048;
struct NotePeer {
  String id;
  String name;
  String token;
  String host;
  String cert;
  uint16_t port = 0;
};

int notePeerCount();
NotePeer notePeerAt(int index);
NotePeer notePeerById(const String& id);
String notePeerAdvertiser();
String noteDeviceSerial();
String noteLastBridge();
bool noteSetLastBridge(const String& id);
bool notePeerIdValid(const String& id);
bool notePeerNameValid(const String& name);
bool notePeerHostValid(const String& host);
bool noteAddPeer(const NotePeer& peer);
bool noteRemovePeer(const String& id);
bool noteOrderPeers(const String* ids, int count);
