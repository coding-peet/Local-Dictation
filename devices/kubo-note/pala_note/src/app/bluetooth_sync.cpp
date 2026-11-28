#include "bluetooth_sync.h"
#include "provider.h"

#if PALA_BRIDGE_DISCOVERY
#include "bluetooth_audio.h"
#include "bluetooth_handshake.h"
#include "note_config.h"
#include "note_peers.h"
#include "usb_sync.h"
#include "settings.h"
#include "stage2_bench.h"
#include <Arduino.h>
#include <BLE2902.h>
#include <BLEDevice.h>
#include <BLEServer.h>
#include <atomic>
#include <cstring>
#include <esp_gatt_common_api.h>
#include <esp_random.h>
#include <freertos/queue.h>
#include <mbedtls/gcm.h>
#include <mbedtls/hkdf.h>
#include <mbedtls/md.h>
#include <mbedtls/platform_util.h>
#include <mutex>

extern "C" {
#include <mbedtls/constant_time.h>
}

namespace {
using namespace pala_ble;
constexpr char kService[] = "a7727a10-7a9d-4bdd-aaf5-d3e331eddf71";
constexpr char kCapability[] = "a7727a11-7a9d-4bdd-aaf5-d3e331eddf71";
constexpr char kRequest[] = "a7727a12-7a9d-4bdd-aaf5-d3e331eddf71";
constexpr char kResponse[] = "a7727a13-7a9d-4bdd-aaf5-d3e331eddf71";
constexpr char kSignal[] = "a7727a14-7a9d-4bdd-aaf5-d3e331eddf71";
enum class BluetoothWindow { Recordings, Settings, Delivery };
constexpr char kEnrollmentMarker = 'P';
constexpr char kDeliveryMarker = 'D';
constexpr size_t kIdentitySize = 6;
constexpr size_t kTagSize = 16;
constexpr size_t kMaxPacket = 512;
constexpr uint16_t kDefaultMtu = 23;
constexpr uint16_t kPreferredMtu = 247;
constexpr size_t kNotificationOverhead = 3;
constexpr unsigned kNotificationsPerPoll = 8;
constexpr int kDisconnected = -1;
constexpr uint32_t kAdvertiseMs = 16000;
constexpr uint32_t kEnrollmentTimeout = 90000;
constexpr uint32_t kUnauthenticatedMs = 7000;

struct Incoming {
  uint32_t generation;
  uint16_t length;
  uint8_t bytes[kMaxPacket];
};

QueueHandle_t input = nullptr;
BLEServer* server = nullptr;
BLECharacteristic* capability = nullptr;
BLECharacteristic* response = nullptr;
BLECharacteristic* signal = nullptr;
BLE2902* signalConfiguration = nullptr;
std::atomic<esp_gatt_if_t> gattInterface{ESP_GATT_IF_NONE};
std::atomic<int> connection{kDisconnected};
std::atomic<uint32_t> connectionGeneration{0};
std::atomic<bool> running{false};
std::atomic<bool> overflow{false};
std::atomic<bool> restartAdvertising{false};
std::atomic<bool> enrollmentMode{false};
bool deliveryMode = false;
std::atomic<bool> nativeEnrollmentSecure{false};
std::atomic<bool> nativeEnrollmentDenied{false};
std::atomic<bool> nativeEnrollmentCompleted{false};

// Only the BLE task updates characteristic values. The loop publishes a
// response for the current generation, which the BLE task snapshots on read.
std::mutex peerMutex;
Capability advertisedCapability{};
uint8_t peerResponse[kMaxPacket]{};
size_t peerResponseSize = 0;
bool closingPeer = false;
bool signalSubscribed = false;
uint16_t peerMtu = kDefaultMtu;
bool transmitCongested = false;
esp_bd_addr_t remoteAddress = {};
bool numericWaiting = false;
bool numericApproved = false;
bool bondCommitted = false;
uint32_t numericCode = 0;

uint32_t preparedGeneration = 0;
String linkPeer;
String linkToken;
Capability linkCapability{};
uint32_t connectedAt = 0;
uint32_t advertisedAt = 0;
uint32_t radioOffAt = 0;
bool advertising = false;
bool keyReady = false;
mbedtls_gcm_context cipher;
uint64_t sent = 0;
uint64_t received = 0;
uint8_t frame[kMaxPacket + 2];
size_t frameSize = 0;
uint8_t audioPacket[kAudioOffsetBytes + kAudioBlockBytes + kTagSize];
size_t audioPacketSize = 0;
size_t audioPacketSent = 0;

bool discoveryIdentity(uint8_t (&result)[kIdentitySize]) {
  const String identity = notePeerAdvertiser();
  if (identity.length() != 2 * kIdentitySize) return false;
  const auto nibble = [](char c) { return c <= '9' ? c - '0' : c - 'a' + 10; };
  for (size_t i = 0; i < kIdentitySize; ++i) result[i] = (nibble(identity[2 * i]) << 4) | nibble(identity[2 * i + 1]);
  return true;
}

void nonce(uint8_t (&iv)[12], bool fromDevice, uint64_t sequence) {
  memset(iv, 0, sizeof(iv));
  iv[3] = fromDevice ? 1 : 0;
  for (size_t pos = sizeof(iv); pos > 4; --pos) {
    iv[pos - 1] = static_cast<uint8_t>(sequence);
    sequence >>= 8;
  }
}

void resetLink() {
  if (preparedGeneration) notePairingDetach(NoteConfigTransport::Bluetooth, preparedGeneration);
  cancelBluetoothSync();
  if (keyReady) mbedtls_gcm_free(&cipher);
  keyReady = false;
  linkPeer = "";
  linkToken = "";
  linkCapability.fill(0);
  mbedtls_platform_zeroize(frame, sizeof(frame));
  mbedtls_platform_zeroize(audioPacket, sizeof(audioPacket));
  frameSize = 0;
  audioPacketSize = audioPacketSent = 0;
  sent = received = 0;
}

void clearPeerResponse() {
  mbedtls_platform_zeroize(peerResponse, sizeof(peerResponse));
  peerResponseSize = 0;
}

void disconnectLink(uint32_t generation = 0) {
  const std::lock_guard<std::mutex> lock(peerMutex);
  const int id = connection.load();
  if (id == kDisconnected || closingPeer ||
      (generation && connectionGeneration.load() != generation)) return;
  clearPeerResponse();
  closingPeer = esp_ble_gatts_close(gattInterface.load(), static_cast<uint16_t>(id)) == ESP_OK;
}

bool publishResponse(uint32_t generation, const uint8_t* packet, size_t size) {
  const std::lock_guard<std::mutex> lock(peerMutex);
  const int id = connection.load();
  if (!running.load() || id == kDisconnected || closingPeer ||
      connectionGeneration.load() != generation || size > sizeof(peerResponse)) return false;

  memcpy(peerResponse, packet, size);
  peerResponseSize = size;
  if (!signalSubscribed) return true;
  uint8_t ready = 0x01;
  // A notification targets the authenticated peer instead of broadcasting
  // a readiness flag to every physical connection in the Arduino BLE stack.
  if (esp_ble_gatts_send_indicate(gattInterface.load(), static_cast<uint16_t>(id),
                                 signal->getHandle(), 1, &ready, false) == ESP_OK) return true;
  clearPeerResponse();
  return false;
}

void markOverflow(uint32_t generation) {
  const std::lock_guard<std::mutex> lock(peerMutex);
  if (connectionGeneration.load() == generation) overflow.store(true);
}

bool nextChunk(uint32_t generation, Incoming& chunk) {
  const std::lock_guard<std::mutex> lock(peerMutex);
  // Never pop data from a new peer while finishing the previous generation.
  return !closingPeer && connection.load() != kDisconnected &&
         connectionGeneration.load() == generation &&
         xQueueReceive(input, &chunk, 0) == pdTRUE;
}

class ServerCallbacks : public BLEServerCallbacks {
  void onConnect(BLEServer* source, esp_ble_gatts_cb_param_t* event) override {
    const std::lock_guard<std::mutex> lock(peerMutex);
    if (!running.load() || connection.load() != kDisconnected) {
      source->disconnect(event->connect.conn_id);
      return;
    }
    advertisedCapability[0] = kProtocolVersion;
    esp_fill_random(advertisedCapability.data() + 1, kNonceSize);
    capability->setValue(advertisedCapability.data(), advertisedCapability.size());
    clearPeerResponse();
    response->setValue(peerResponse, 0);
    uint8_t unsubscribed[2] = {};
    signalConfiguration->setValue(unsubscribed, sizeof(unsubscribed));
    signalSubscribed = false;
    peerMtu = kDefaultMtu;
    transmitCongested = false;
    memcpy(remoteAddress, event->connect.remote_bda, sizeof(remoteAddress));
    numericWaiting = false;
    numericApproved = false;
    bondCommitted = false;
    nativeEnrollmentSecure.store(false);
    nativeEnrollmentDenied.store(false);
    xQueueReset(input);
    overflow.store(false);
    closingPeer = false;
    connectionGeneration.fetch_add(1);
    connection.store(event->connect.conn_id);
  }

  void onDisconnect(BLEServer*, esp_ble_gatts_cb_param_t* event) override {
    const std::lock_guard<std::mutex> lock(peerMutex);
    if (connection.load() != event->disconnect.conn_id) return;
    clearPeerResponse();
    advertisedCapability.fill(0);
    capability->setValue(advertisedCapability.data(), 0);
    response->setValue(peerResponse, 0);
    signalSubscribed = false;
    if (numericApproved && !bondCommitted) esp_ble_remove_bond_device(remoteAddress);
    if (bondCommitted) nativeEnrollmentCompleted.store(true);
    numericWaiting = false;
    nativeEnrollmentSecure.store(false);
    closingPeer = false;
    // A connect and disconnect can both occur during a single e-paper refresh.
    // Tell the loop that connectable advertising ended even if it never saw the link.
    restartAdvertising.store(true);
    connection.store(kDisconnected);
  }
};

class RequestCallbacks : public BLECharacteristicCallbacks {
  void onWrite(BLECharacteristic* characteristic, esp_ble_gatts_cb_param_t* event) override {
    // The Arduino stack uses the common conn_id field for normal and long writes.
    if (!running.load() || connection.load() != event->write.conn_id) return;
    const String data = characteristic->getValue();
    if (data.isEmpty() || data.length() > kMaxPacket) {
      overflow.store(true);
      return;
    }
    Incoming chunk;
    chunk.generation = connectionGeneration.load();
    chunk.length = data.length();
    memcpy(chunk.bytes, data.c_str(), chunk.length);
    if (xQueueSend(input, &chunk, 0) != pdTRUE) overflow.store(true);
  }
};

class ResponseCallbacks : public BLECharacteristicCallbacks {
  void onRead(BLECharacteristic* characteristic, esp_ble_gatts_cb_param_t* event) override {
    const std::lock_guard<std::mutex> lock(peerMutex);
    const bool current = running.load() && !closingPeer && connection.load() == event->read.conn_id;
    characteristic->setValue(peerResponse, current ? peerResponseSize : 0);
  }
};

void handleGattEvent(esp_gatts_cb_event_t event, esp_gatt_if_t interface, esp_ble_gatts_cb_param_t* param) {
  // This firmware registers a single GATT server. The public event hook
  // exposes its interface and the peer ID for each CCCD subscription.
  if (event == ESP_GATTS_REG_EVT && param->reg.status == ESP_GATT_OK) {
    gattInterface.store(interface);
    return;
  }
  if (!running.load() || interface != gattInterface.load()) return;
  if (event == ESP_GATTS_MTU_EVT || event == ESP_GATTS_CONGEST_EVT) {
    const std::lock_guard<std::mutex> lock(peerMutex);
    const int id = connection.load();
    if (event == ESP_GATTS_MTU_EVT && id == param->mtu.conn_id && param->mtu.mtu >= kDefaultMtu) {
      peerMtu = param->mtu.mtu;
    } else if (event == ESP_GATTS_CONGEST_EVT && id == param->congest.conn_id) {
      transmitCongested = param->congest.congested;
    }
    return;
  }
  if (event != ESP_GATTS_WRITE_EVT || param->write.is_prep ||
      param->write.handle != signalConfiguration->getHandle()) return;

  const std::lock_guard<std::mutex> lock(peerMutex);
  if (!closingPeer && connection.load() == param->write.conn_id) {
    signalSubscribed = param->write.len == 2 && (param->write.value[0] & 1) != 0;
  }
}

void handleGapEvent(esp_gap_ble_cb_event_t event, esp_ble_gap_cb_param_t* parameter) {
  if (event != ESP_GAP_BLE_NC_REQ_EVT && event != ESP_GAP_BLE_PASSKEY_REQ_EVT &&
      event != ESP_GAP_BLE_PASSKEY_NOTIF_EVT && event != ESP_GAP_BLE_AUTH_CMPL_EVT) return;
  const auto& security = parameter->ble_security;
  const std::lock_guard<std::mutex> lock(peerMutex);
  if (event == ESP_GAP_BLE_NC_REQ_EVT) {
    auto* address = parameter->ble_security.key_notif.bd_addr;
    if (!enrollmentMode.load() || connection.load() == kDisconnected || closingPeer ||
        memcmp(remoteAddress, address, sizeof(remoteAddress)) || numericWaiting || numericApproved) {
      esp_ble_confirm_reply(address, false);
      return;
    }
    numericCode = security.key_notif.passkey;
    numericWaiting = true;
  } else if (event == ESP_GAP_BLE_AUTH_CMPL_EVT) {
    if (!enrollmentMode.load() || connection.load() == kDisconnected ||
        memcmp(remoteAddress, security.auth_cmpl.bd_addr, sizeof(remoteAddress))) return;
    const uint8_t authMode = security.auth_cmpl.auth_mode;
    const bool secure = authorizesNativeEnrollment(numericApproved, security.auth_cmpl.success,
        (authMode & ESP_LE_AUTH_REQ_SC_ONLY) != 0, (authMode & ESP_LE_AUTH_REQ_MITM) != 0);
    const bool attempted = numericWaiting || numericApproved || nativeEnrollmentDenied.load();
    nativeEnrollmentSecure.store(secure);
    // Windows can resume an existing bond for routine app authentication.
    // A resumed bond alone grants no access to the enrollment channel.
    nativeEnrollmentDenied.store(attempted && !secure);
  } else if (enrollmentMode.load() && connection.load() != kDisconnected) {
    if (event == ESP_GAP_BLE_PASSKEY_REQ_EVT) esp_ble_passkey_reply(parameter->ble_security.ble_req.bd_addr, false, 0);
    nativeEnrollmentDenied.store(true);
  }
}

bool configureEnrollmentSecurity() {
  uint8_t auth = ESP_LE_AUTH_REQ_SC_MITM_BOND;
  uint8_t io = ESP_IO_CAP_IO;
  uint8_t onlySpecified = ESP_BLE_ONLY_ACCEPT_SPECIFIED_AUTH_ENABLE;
  uint8_t keySize = 16;
  return esp_ble_gap_set_security_param(ESP_BLE_SM_AUTHEN_REQ_MODE, &auth, sizeof(auth)) == ESP_OK &&
         esp_ble_gap_set_security_param(ESP_BLE_SM_IOCAP_MODE, &io, sizeof(io)) == ESP_OK &&
         esp_ble_gap_set_security_param(ESP_BLE_SM_ONLY_ACCEPT_SPECIFIED_SEC_AUTH, &onlySpecified, sizeof(onlySpecified)) == ESP_OK &&
         esp_ble_gap_set_security_param(ESP_BLE_SM_MIN_KEY_SIZE, &keySize, sizeof(keySize)) == ESP_OK &&
         esp_ble_gap_set_security_param(ESP_BLE_SM_MAX_KEY_SIZE, &keySize, sizeof(keySize)) == ESP_OK;
}

bool initialize() {
  if (server) return gattInterface.load() != ESP_GATT_IF_NONE;
  static ServerCallbacks serverCallbacks;
  static RequestCallbacks requestCallbacks;
  static ResponseCallbacks responseCallbacks;

  input = xQueueCreate(8, sizeof(Incoming));
  if (!input) return false;
  BLEDevice::init(palaNoteName() + "-Note");
  BLEDevice::setMTU(kPreferredMtu);
  BLEDevice::setCustomGattsHandler(handleGattEvent);
  BLEDevice::setCustomGapHandler(handleGapEvent);
  server = BLEDevice::createServer();
  server->setCallbacks(&serverCallbacks);
  auto* service = server->createService(BLEUUID(String(kService)), 14);
  capability = service->createCharacteristic(BLEUUID(String(kCapability)), BLECharacteristic::PROPERTY_READ);
  service->createCharacteristic(BLEUUID(String(kRequest)), BLECharacteristic::PROPERTY_WRITE)
      ->setCallbacks(&requestCallbacks);
  response = service->createCharacteristic(BLEUUID(String(kResponse)), BLECharacteristic::PROPERTY_READ);
  response->setCallbacks(&responseCallbacks);
  signal = service->createCharacteristic(BLEUUID(String(kSignal)), BLECharacteristic::PROPERTY_NOTIFY);
  signalConfiguration = new BLE2902();
  signal->addDescriptor(signalConfiguration);
  service->start();
  return gattInterface.load() != ESP_GATT_IF_NONE;
}

bool configureAdvertising(BluetoothWindow window) {
  uint8_t id[kIdentitySize];
  if (!discoveryIdentity(id)) return false;

  const String name = palaNoteName() + "-Note";
  if (esp_ble_gap_set_device_name(name.c_str()) != ESP_OK) return false;

  // Flags and 128-bit service data use 27 bytes of the 31-byte payload.
  // Put the local name in the scan response and use the existing paired
  // identity bytes for discovery. Session authentication still uses the token.
  BLEAdvertisementData data;
  data.setFlags(ESP_BLE_ADV_FLAG_GEN_DISC | ESP_BLE_ADV_FLAG_BREDR_NOT_SPT);
  char tagged[kIdentitySize + 1] = {window == BluetoothWindow::Delivery ? kDeliveryMarker : kEnrollmentMarker};
  memcpy(tagged + 1, id, sizeof(id));
  const String marker = window == BluetoothWindow::Recordings ? String(tagged + 1, sizeof(id))
                                                             : String(tagged, sizeof(tagged));
  data.setServiceData(BLEUUID(String(kService)), marker);
  BLEAdvertisementData scan;
  scan.setName(name);
  auto* broadcaster = BLEDevice::getAdvertising();
  broadcaster->setScanResponse(true);
  return broadcaster->setAdvertisementData(data) && broadcaster->setScanResponseData(scan);
}

bool prepareLink(uint32_t generation) {
  resetLink();
  const std::lock_guard<std::mutex> lock(peerMutex);
  if (connection.load() == kDisconnected || closingPeer ||
      connectionGeneration.load() != generation) return false;
  preparedGeneration = generation;
  connectedAt = millis();
  linkCapability = advertisedCapability;
  return linkCapability[0] == kProtocolVersion;
}

bool authenticateLink(const uint8_t* packet, size_t length) {
  if (!acceptsBridgeHello(linkCapability, packet, length)) return false;
  const auto* sha256 = mbedtls_md_info_from_type(MBEDTLS_MD_SHA256);
  if (!sha256) return false;

  const auto* bridgeNonce = packet + 1;
  const auto salt = handshakeSalt(linkCapability, bridgeNonce);
  const auto bridgeMessage = proofMessage("bridge", linkCapability, bridgeNonce);
  const auto palaMessage = proofMessage("pala", linkCapability, bridgeNonce);
  uint8_t keys[kProofSize + kSessionKeySize];
  uint8_t expected[kProofSize];
  uint8_t reply[kPalaReplySize] = {kPalaReplyOpcode};

  for (int i = 0; i < notePeerCount(); ++i) {
    const NotePeer candidate = notePeerAt(i);
    const auto* token = reinterpret_cast<const uint8_t*>(candidate.token.c_str());
    const bool verified = !candidate.token.isEmpty() &&
        mbedtls_hkdf(sha256, salt.data(), salt.size(), token, candidate.token.length(),
            reinterpret_cast<const uint8_t*>(kKdfInfo.data()), kKdfInfo.size(), keys, sizeof(keys)) == 0 &&
        mbedtls_md_hmac(sha256, keys, kProofSize, bridgeMessage.data(), bridgeMessage.size(), expected) == 0 &&
        mbedtls_ct_memcmp(expected, bridgeNonce + kNonceSize, kProofSize) == 0;
    if (!verified) continue;
    if (mbedtls_md_hmac(sha256, keys, kProofSize, palaMessage.data(), palaMessage.size(), reply + 1) != 0) break;
    mbedtls_gcm_init(&cipher);
    keyReady = mbedtls_gcm_setkey(&cipher, MBEDTLS_CIPHER_ID_AES, keys + kProofSize, 8 * kSessionKeySize) == 0;
    if (keyReady) {
      linkPeer = candidate.id;
      linkToken = candidate.token;
    } else mbedtls_gcm_free(&cipher);
    break;
  }
  mbedtls_platform_zeroize(keys, sizeof(keys));
  mbedtls_platform_zeroize(expected, sizeof(expected));
  const bool ready = keyReady && publishResponse(preparedGeneration, reply, sizeof(reply));
  mbedtls_platform_zeroize(reply, sizeof(reply));
  if (ready) connectedAt = millis();
  return ready;
}

void sendFrame(const String& plaintext) {
  if (connection.load() == kDisconnected || !keyReady || sent == UINT64_MAX ||
      plaintext.isEmpty() || plaintext.length() > kMaxPacket - kTagSize) {
    markOverflow(preparedGeneration);
    return;
  }
  uint8_t packet[kMaxPacket];
  uint8_t iv[12];
  nonce(iv, true, sent);
  const size_t size = plaintext.length();
  if (mbedtls_gcm_crypt_and_tag(&cipher, MBEDTLS_GCM_ENCRYPT, size, iv, sizeof(iv), nullptr, 0,
       reinterpret_cast<const uint8_t*>(plaintext.c_str()), packet, kTagSize, packet + size) != 0) {
    markOverflow(preparedGeneration);
    return;
  }
  ++sent;
  if (!publishResponse(preparedGeneration, packet, size + kTagSize)) markOverflow(preparedGeneration);
}

#if PALA_STAGE2_BENCH
uint32_t bleTraceSequence = 0;
#endif
#if PALA_STAGE2_BENCH && defined(PALA_TEST_BLE_FRAGMENT_GAP_MS)
uint32_t lastPacedFragmentAt = 0;
#endif

bool queueAudioPacket(const uint8_t* plaintext, size_t size) {
  const AudioRequest request = parseAudioRequest(plaintext, size);
  if (audioPacketSize || sent == UINT64_MAX || !request.length) return false;
#if PALA_STAGE2_BENCH
  bleTraceSequence = stage2::bleBlockRequested(request.offset, request.length);
#endif
  if (!readBluetoothAudio(linkPeer, request.offset, audioPacket + kAudioOffsetBytes, request.length)) return false;

  memcpy(audioPacket, plaintext + 1, kAudioOffsetBytes);
  uint8_t iv[12];
  nonce(iv, true, sent);
  const size_t bytes = kAudioOffsetBytes + request.length;
  // GCM encryption supports using the same storage for input and output.
  if (mbedtls_gcm_crypt_and_tag(&cipher, MBEDTLS_GCM_ENCRYPT, bytes, iv, sizeof(iv), nullptr, 0,
                              audioPacket, audioPacket, kTagSize, audioPacket + bytes) != 0) return false;
  ++sent;
  audioPacketSize = bytes + kTagSize;
  audioPacketSent = 0;
  return true;
}

bool sendAudioNotifications(uint32_t generation) {
  const std::lock_guard<std::mutex> lock(peerMutex);
  const int id = connection.load();
  if (id == kDisconnected || closingPeer || connectionGeneration.load() != generation || !signalSubscribed)
    return false;
  if (transmitCongested) return true;

  uint8_t fragment[kPreferredMtu - kNotificationOverhead] = {kAudioFragment};
  const size_t capacity = std::min<size_t>(sizeof(fragment), peerMtu - kNotificationOverhead) - 1;
  const unsigned credits = esp_ble_get_cur_sendable_packets_num(static_cast<uint16_t>(id));
#if PALA_STAGE2_BENCH && defined(PALA_TEST_BLE_FRAGMENT_GAP_MS)
  // Return to the recorder while waiting for the next test fragment.
  // A new audio packet starts immediately because audioPacketSent is zero.
  if (audioPacketSent && millis() - lastPacedFragmentAt < PALA_TEST_BLE_FRAGMENT_GAP_MS) return true;
  const unsigned budget = std::min(1U, credits);
#else
  const unsigned budget = std::min(kNotificationsPerPoll, credits);
#endif
#if PALA_STAGE2_BENCH
  uint32_t firstQueuedAt = 0, lastQueuedAt = 0;
  bool queued = false;
  bool completed = false;
#endif
  for (unsigned i = 0; i < budget; ++i) {
    const size_t count = std::min(capacity, audioPacketSize - audioPacketSent);
    memcpy(fragment + 1, audioPacket + audioPacketSent, count);
    if (esp_ble_gatts_send_indicate(gattInterface.load(), static_cast<uint16_t>(id), signal->getHandle(),
                                   count + 1, fragment, false) != ESP_OK) break;
    audioPacketSent += count;
#if PALA_STAGE2_BENCH
    lastQueuedAt = millis();
#if defined(PALA_TEST_BLE_FRAGMENT_GAP_MS)
    lastPacedFragmentAt = lastQueuedAt;
#endif
    if (!queued) firstQueuedAt = lastQueuedAt;
    queued = true;
#endif
    if (audioPacketSent == audioPacketSize) {
      audioPacketSize = audioPacketSent = 0;
#if PALA_STAGE2_BENCH
      completed = true;
#endif
      break;
    }
  }
#if PALA_STAGE2_BENCH
  if (queued) stage2::bleBlockNotifications(bleTraceSequence, firstQueuedAt, lastQueuedAt, completed);
#endif
  return true;
}

bool receiveEnrollment(const uint8_t* data, size_t length) {
  if (!nativeEnrollmentSecure.load() || !length || data[length - 1] != '\n' ||
      memchr(data, '\0', length)) return false;
  const String line(reinterpret_cast<const char*>(data), length - 1);
  if (!line.startsWith(kNoteConfigPrefix)) return false;
  const String answer = noteConfigRequest(line.substring(sizeof(kNoteConfigPrefix) - 1),
      NoteConfigTransport::Bluetooth, false, preparedGeneration, true);
  if (answer.indexOf(F("\"state\":\"paired\"")) >= 0) {
    const std::lock_guard<std::mutex> lock(peerMutex);
    if (connectionGeneration.load() == preparedGeneration) bondCommitted = true;
  }
  const String payload = String(kNoteConfigPrefix) + answer + "\n";
  return publishResponse(preparedGeneration, reinterpret_cast<const uint8_t*>(payload.c_str()), payload.length());
}

bool receive(const Incoming& chunk) {
  if (chunk.length > sizeof(frame) - frameSize) return false;
  memcpy(frame + frameSize, chunk.bytes, chunk.length);
  frameSize += chunk.length;
  if (frameSize < 2) return true;
  const size_t length = (static_cast<size_t>(frame[0]) << 8) | frame[1];
  const bool nativeConfig = enrollmentMode.load() && !keyReady && nativeEnrollmentSecure.load();
  const bool validLength = validFrameLength(length, keyReady, kMaxPacket, kTagSize) ||
                           (nativeConfig && length > 0 && length <= kMaxPacket);
  if (!validLength || frameSize > length + 2) return false;
  if (frameSize < length + 2) return true;
  frameSize = 0;
  if (!keyReady) {
    if (length == kBridgeHelloSize && frame[2] == kBridgeHelloOpcode) return authenticateLink(frame + 2, length);
    return nativeConfig && receiveEnrollment(frame + 2, length);
  }
  if (received == UINT64_MAX) return false;

  uint8_t iv[12];
  nonce(iv, false, received);
  const size_t plainSize = length - kTagSize;
  char plaintext[kMaxPacket - kTagSize + 1];
  if (mbedtls_gcm_auth_decrypt(&cipher, plainSize, iv, sizeof(iv), nullptr, 0,
       frame + 2 + plainSize, kTagSize, frame + 2,
       reinterpret_cast<uint8_t*>(plaintext)) != 0) {
    mbedtls_platform_zeroize(plaintext, sizeof(plaintext));
    return false;
  }
  ++received;
  connectedAt = millis();
  const bool audioSupported = !deliveryMode && !enrollmentMode.load();
  if (plaintext[0] == kAudioRequest) {
    const bool queued = audioSupported && queueAudioPacket(reinterpret_cast<const uint8_t*>(plaintext), plainSize);
    mbedtls_platform_zeroize(plaintext, sizeof(plaintext));
    return queued;
  }
  if (audioPacketSize) {
    mbedtls_platform_zeroize(plaintext, sizeof(plaintext));
    return false;
  }
  plaintext[plainSize] = '\0';
  if (plaintext[plainSize - 1] != '\n' || strlen(plaintext) != plainSize) {
    mbedtls_platform_zeroize(plaintext, sizeof(plaintext));
    return false;
  }
  String line(plaintext);
  mbedtls_platform_zeroize(plaintext, sizeof(plaintext));
  line.trim();
  return handleBluetoothSyncLine(line, linkPeer, sendFrame, deliveryMode, audioSupported);
}
}  // namespace

static bool startBluetoothWindow(BluetoothWindow window) {
  if (!palaBluetoothEnabled()) return false;
  const bool enrollment = window == BluetoothWindow::Settings;
  const bool delivery = window == BluetoothWindow::Delivery;
  if (running.load()) return enrollmentMode.load() == enrollment && deliveryMode == delivery;
  if (!initialize() || connection.load() != kDisconnected || (enrollment && !configureEnrollmentSecurity())) return false;
  if (!configureAdvertising(window)) return false;
  enrollmentMode.store(enrollment);
  deliveryMode = delivery;
  resetLink();
  takeBluetoothSyncDrained();
  xQueueReset(input);
  overflow.store(false);
  restartAdvertising.store(false);
  nativeEnrollmentCompleted.store(false);
  advertisedAt = millis();
  running.store(true);
  advertising = BLEDevice::getAdvertising()->start();
  if (!advertising) {
    running.store(false);
    enrollmentMode.store(false);
    deliveryMode = false;
  }
  return advertising;
}

bool startBluetoothSync() { return startBluetoothWindow(BluetoothWindow::Recordings); }
bool startBluetoothHomeDelivery() { return startBluetoothWindow(BluetoothWindow::Delivery); }
bool bluetoothHomeDeliveryOpen() { return running.load() && deliveryMode; }
bool bluetoothEnrollmentAvailable() { return palaBluetoothEnabled(); }
bool startBluetoothEnrollment() { return startBluetoothWindow(BluetoothWindow::Settings); }
bool bluetoothEnrollmentOpen() { return running.load() && enrollmentMode.load(); }

bool bluetoothEnrollmentPrompt(uint32_t& code) {
  const std::lock_guard<std::mutex> lock(peerMutex);
  if (!bluetoothEnrollmentOpen() || !numericWaiting || closingPeer) return false;
  code = numericCode;
  return true;
}

void bluetoothEnrollmentConfirm(bool accept) {
  const std::lock_guard<std::mutex> lock(peerMutex);
  if (!bluetoothEnrollmentOpen() || !numericWaiting || closingPeer) return;
  numericWaiting = false;
  const bool sent = esp_ble_confirm_reply(remoteAddress, accept) == ESP_OK;
  numericApproved = sent && accept;
  nativeEnrollmentDenied.store(!numericApproved);
}

void stopBluetoothSync() {
  if (!running.exchange(false)) return;
  if (advertising) BLEDevice::stopAdvertising();
  advertising = false;
  disconnectLink();
  enrollmentMode.store(false);
  deliveryMode = false;
  resetLink();
}

bool pollBluetoothSync() {
  if (palaBluetoothEnabled()) radioOffAt = 0;
  if (!running.load()) return false;
  if (!palaBluetoothEnabled()) {
    // The client reads the save acknowledgement after receiving a GATT notification.
    if (!radioOffAt) radioOffAt = millis() + 1500;
    if (connection.load() != kDisconnected && static_cast<int32_t>(millis() - radioOffAt) < 0) return true;
    stopBluetoothSync();
    return false;
  }
  const bool enrolling = enrollmentMode.load();
  if (enrolling && (nativeEnrollmentCompleted.load() || millis() - advertisedAt >= kEnrollmentTimeout)) {
    bluetoothEnrollmentConfirm(false);
    stopBluetoothSync();
    return false;
  }
  if (connection.load() == kDisconnected) {
    if (restartAdvertising.exchange(false)) advertising = false;
    if (linkCapability[0] != 0) resetLink();
    if (!enrolling && (takeBluetoothSyncDrained() || millis() - advertisedAt >= kAdvertiseMs)) {
      stopBluetoothSync();
      return false;
    }
    if (!advertising) {
      overflow.store(false);
      advertising = BLEDevice::getAdvertising()->start();
    }
    return true;
  }

  advertising = false;
  const uint32_t generation = connectionGeneration.load();
  if (preparedGeneration != generation && !prepareLink(generation)) {
    disconnectLink(generation);
    return true;
  }
  Incoming chunk;
  bool valid = linkCapability[0] == kProtocolVersion && !overflow.load() &&
               !(enrolling && nativeEnrollmentDenied.load()) &&
               (!keyReady || notePeerById(linkPeer).token == linkToken);
  while (valid && nextChunk(generation, chunk)) {
    if (chunk.generation == generation && connection.load() != kDisconnected &&
        connectionGeneration.load() == generation) {
      valid = receive(chunk) && !overflow.load();
    }
  }
  if (valid && audioPacketSize) valid = sendAudioNotifications(generation);
  if (!valid || (!enrolling && !bluetoothSyncActive() && millis() - connectedAt >= kUnauthenticatedMs)) {
    disconnectLink(generation);
    resetLink();
  }
  return true;
}
#else
bool startBluetoothSync() { return false; }
bool startBluetoothHomeDelivery() { return false; }
bool bluetoothHomeDeliveryOpen() { return false; }
bool bluetoothEnrollmentAvailable() { return false; }
bool startBluetoothEnrollment() { return false; }
bool bluetoothEnrollmentOpen() { return false; }
bool bluetoothEnrollmentPrompt(uint32_t&) { return false; }
void bluetoothEnrollmentConfirm(bool) {}
bool pollBluetoothSync() { return false; }
void stopBluetoothSync() {}
#endif
