#include "note_peers.h"

#include "provider.h"
#include <ArduinoJson.h>
#include <Preferences.h>
#include <esp_mac.h>
#include <mbedtls/md.h>
#include <mbedtls/platform_util.h>
#include <mbedtls/x509_crt.h>

namespace {
Preferences prefs;
NotePeer slots[kNoteMaxPeers];
int order[kNoteMaxPeers];
int count = 0;
bool attempted = false;
bool ready = false;
String advertiser;

char lowerHex(int n) { return "0123456789abcdef"[n & 15]; }

String hex(const uint8_t* data, size_t size) {
  String value;
  if (!value.reserve(size * 2)) return {};
  for (size_t i = 0; i < size; ++i) {
    value += lowerHex(data[i] >> 4);
    value += lowerHex(data[i]);
  }
  return value;
}

bool isHex(const String& value, size_t size) {
  if (value.length() != size) return false;
  for (char c : value) if (!((c >= 'a' && c <= 'f') || (c >= '0' && c <= '9'))) return false;
  return true;
}

bool alphaNumeric(char c) {
  return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
}

bool validCertificate(const String& cert) {
  if (cert.length() > kNoteMaxPeerCertificate || !cert.startsWith("-----BEGIN CERTIFICATE-----") ||
      cert.indexOf("-----END CERTIFICATE-----") < 0) return false;
  mbedtls_x509_crt certificate;
  mbedtls_x509_crt_init(&certificate);
  const bool valid = mbedtls_x509_crt_parse(&certificate, reinterpret_cast<const uint8_t*>(cert.c_str()),
      cert.length() + 1) == 0 && !certificate.next;
  mbedtls_x509_crt_free(&certificate);
  return valid;
}

bool validPeer(const NotePeer& peer) {
  if (!notePeerIdValid(peer.id) || !notePeerNameValid(peer.name) ||
      !peer.token.length() || peer.token.length() > 128) return false;
  if (peer.host.isEmpty()) return peer.cert.isEmpty() && !peer.port;
  return notePeerHostValid(peer.host) && peer.port && validCertificate(peer.cert);
}

String slotKey(int slot) { return String("peer") + slot; }

bool storeSlot(int slot, const NotePeer& peer) {
  JsonDocument doc;
  doc["id"] = peer.id;
  doc["name"] = peer.name;
  doc["token"] = peer.token;
  doc["host"] = peer.host;
  doc["port"] = peer.port;
  doc["cert"] = peer.cert;
  String payload;
  serializeJson(doc, payload);
  const String key = slotKey(slot);
  return payload.length() && prefs.putString(key.c_str(), payload) == payload.length() &&
         prefs.getString(key.c_str()) == payload;
}

bool storeOrder(const int* slotsToStore, int size) {
  JsonDocument doc;
  doc["advertiser"] = advertiser;
  JsonArray indices = doc["order"].to<JsonArray>();
  for (int i = 0; i < size; ++i) indices.add(slotsToStore[i]);
  String payload;
  serializeJson(doc, payload);
  if (!payload.length() || prefs.putString("registry", payload) != payload.length() ||
      prefs.getString("registry") != payload) return false;
  for (int i = 0; i < size; ++i) order[i] = slotsToStore[i];
  count = size;
  return true;
}

bool migrate() {
#if defined(PALA_BRIDGE_TOKEN) && PALA_BRIDGE_DISCOVERY
  const NotePeer legacy{"legacy", "Existing Bridge", PALA_BRIDGE_TOKEN, PALA_HOST, PALA_BRIDGE_CERT, PALA_PORT};
  const auto* algo = mbedtls_md_info_from_type(MBEDTLS_MD_SHA256);
  constexpr char message[] = "KUBO_BLE_V1 identity";
  uint8_t digest[32];
  if (!algo || mbedtls_md_hmac(algo, reinterpret_cast<const uint8_t*>(legacy.token.c_str()), legacy.token.length(),
      reinterpret_cast<const uint8_t*>(message), sizeof(message) - 1, digest) != 0) return false;
  advertiser = hex(digest, 6);
  mbedtls_platform_zeroize(digest, sizeof(digest));
  if (!isHex(advertiser, 12) || !validPeer(legacy) || !storeSlot(0, legacy)) return false;
  slots[0] = legacy;
  const int initial[] = {0};
  return storeOrder(initial, 1);
#else
  advertiser = noteDeviceSerial();
  return isHex(advertiser, 12) && storeOrder(nullptr, 0);
#endif
}

bool initialize() {
#if !PALA_BRIDGE_DISCOVERY
  return false;
#endif
  if (attempted) return ready;
  attempted = true;
  if (!prefs.begin("pala-peers", false)) return false;
  if (!prefs.isKey("registry")) return ready = migrate();

  JsonDocument registry;
  if (deserializeJson(registry, prefs.getString("registry"))) return false;
  advertiser = registry["advertiser"] | "";
  const JsonArrayConst indices = registry["order"].as<JsonArrayConst>();
  if (!isHex(advertiser, 12) || indices.isNull() || indices.size() > kNoteMaxPeers) return false;
  bool used[kNoteMaxPeers] = {};
  for (JsonVariantConst entry : indices) {
    if (!entry.is<int>()) return false;
    const int slot = entry.as<int>();
    if (slot < 0 || slot >= kNoteMaxPeers || used[slot]) return false;
    JsonDocument stored;
    if (deserializeJson(stored, prefs.getString(slotKey(slot).c_str()))) return false;
    NotePeer peer{stored["id"] | "", stored["name"] | "", stored["token"] | "",
                  stored["host"] | "", stored["cert"] | "", stored["port"].as<uint16_t>()};
    if (!validPeer(peer)) return false;
    for (int i = 0; i < count; ++i) if (slots[order[i]].id == peer.id) return false;
    slots[slot] = peer;
    order[count++] = slot;
    used[slot] = true;
  }
  return ready = true;
}
}  // namespace

String noteDeviceSerial() {
  uint8_t mac[6];
  return esp_read_mac(mac, ESP_MAC_BASE) == ESP_OK ? hex(mac, sizeof(mac)) : String();
}

bool notePeerIdValid(const String& id) {
  if (id.isEmpty() || id.length() > 32) return false;
  for (char c : id) if (!alphaNumeric(c) && c != '-' && c != '_') return false;
  return true;
}

bool notePeerNameValid(const String& name) {
  if (name.isEmpty() || name.length() > 32) return false;
  for (char c : name) if (static_cast<unsigned char>(c) < 32 || c == 127) return false;
  return true;
}

bool notePeerHostValid(const String& host) {
  if (host.length() < 7 || host.length() > 69 || !host.endsWith(".local")) return false;
  const size_t end = host.length() - 6;
  if (!alphaNumeric(host[0]) || !alphaNumeric(host[end - 1])) return false;
  for (size_t i = 0; i < end; ++i) if (!alphaNumeric(host[i]) && host[i] != '-') return false;
  return true;
}

int notePeerCount() { return initialize() ? count : 0; }

NotePeer notePeerAt(int index) {
  return initialize() && index >= 0 && index < count ? slots[order[index]] : NotePeer{};
}

NotePeer notePeerById(const String& id) {
  if (initialize()) for (int i = 0; i < count; ++i) {
    if (slots[order[i]].id == id) return slots[order[i]];
  }
  return {};
}

String notePeerAdvertiser() { return initialize() ? advertiser : String(); }

String noteLastBridge() {
  if (!initialize()) return {};
  const String id = prefs.getString("last");
  return !id.isEmpty() && notePeerById(id).id == id ? id : String();
}

bool noteSetLastBridge(const String& id) {
  if (id.isEmpty() || notePeerById(id).id != id) return false;
  return noteLastBridge() == id || prefs.putString("last", id) == id.length();
}

bool noteAddPeer(const NotePeer& peer) {
  if (!initialize() || count == kNoteMaxPeers || peer.id == "legacy" ||
      !validPeer(peer) || !notePeerById(peer.id).id.isEmpty()) return false;
  bool used[kNoteMaxPeers] = {};
  int next[kNoteMaxPeers];
  for (int i = 0; i < count; ++i) {
    next[i] = order[i];
    used[order[i]] = true;
  }
  int slot = 0;
  while (used[slot]) ++slot;
  if (!storeSlot(slot, peer)) return false;
  slots[slot] = peer;
  next[count] = slot;
  if (storeOrder(next, count + 1)) return true;
  slots[slot] = {};
  prefs.remove(slotKey(slot).c_str());
  return false;
}

bool noteRemovePeer(const String& id) {
  if (!initialize() || id.isEmpty()) return false;
  int next[kNoteMaxPeers];
  int size = 0;
  int removed = -1;
  for (int i = 0; i < count; ++i) {
    const int slot = order[i];
    if (slots[slot].id == id) removed = slot;
    else next[size++] = slot;
  }
  if (removed < 0 || !storeOrder(next, size)) return false;
  slots[removed] = {};
  prefs.remove(slotKey(removed).c_str());
  if (prefs.getString("last") == id) prefs.remove("last");
  return true;
}

bool noteOrderPeers(const String* ids, int size) {
  if (!initialize() || size != count) return false;
  int next[kNoteMaxPeers];
  bool used[kNoteMaxPeers] = {};
  bool changed = false;
  for (int i = 0; i < size; ++i) {
    int slot = -1;
    for (int j = 0; j < count; ++j) if (slots[order[j]].id == ids[i]) slot = order[j];
    if (slot < 0 || used[slot]) return false;
    used[slot] = true;
    next[i] = slot;
    changed = changed || slot != order[i];
  }
  return !changed || storeOrder(next, size);
}
