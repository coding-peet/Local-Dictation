#include "bluetooth_sync.h"
#include "provider.h"

#if PALA_BRIDGE_DISCOVERY && defined(PALA_BRIDGE_TOKEN)
#include "usb_sync.h"
#include <Arduino.h>
#include <BLE2902.h>
#include <BLEDevice.h>
#include <BLEServer.h>
#include <atomic>
#include <cstring>
#include <esp_random.h>
#include <freertos/queue.h>
#include <mbedtls/gcm.h>
#include <mbedtls/md.h>
#include <mbedtls/platform_util.h>

namespace {
constexpr char kService[] = "a7727a10-7a9d-4bdd-aaf5-d3e331eddf71";
constexpr char kCapability[] = "a7727a11-7a9d-4bdd-aaf5-d3e331eddf71";
constexpr char kRequest[] = "a7727a12-7a9d-4bdd-aaf5-d3e331eddf71";
constexpr char kResponse[] = "a7727a13-7a9d-4bdd-aaf5-d3e331eddf71";
constexpr char kSignal[] = "a7727a14-7a9d-4bdd-aaf5-d3e331eddf71";
constexpr char kLabel[] = "KUBO_BLE_V1 ";
constexpr size_t kChallengeSize = 16;
constexpr size_t kTagSize = 16;
constexpr size_t kMaxPacket = 512;
constexpr int kDisconnected = -1;
constexpr uint32_t kAdvertiseMs = 16000;
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
std::atomic<int> connection{kDisconnected};
std::atomic<uint32_t> connectionGeneration{0};
std::atomic<bool> running{false};
std::atomic<bool> overflow{false};
uint32_t preparedGeneration = 0;
uint32_t connectedAt = 0;
uint32_t advertisedAt = 0;
bool advertising = false;
bool keyReady = false;
mbedtls_gcm_context cipher;
uint64_t sent = 0;
uint64_t received = 0;
uint8_t frame[kMaxPacket + 2];
size_t frameSize = 0;

bool digest(const uint8_t* suffix, size_t suffixLength, uint8_t* result) {
  uint8_t material[sizeof(kLabel) - 1 + kChallengeSize];
  memcpy(material, kLabel, sizeof(kLabel) - 1);
  memcpy(material + sizeof(kLabel) - 1, suffix, suffixLength);
  const auto* token = reinterpret_cast<const uint8_t*>(PALA_BRIDGE_TOKEN);
  const auto* sha256 = mbedtls_md_info_from_type(MBEDTLS_MD_SHA256);
  return sha256 && mbedtls_md_hmac(sha256, token, strlen(PALA_BRIDGE_TOKEN),
                                  material, sizeof(kLabel) - 1 + suffixLength, result) == 0;
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
  cancelBluetoothSync();
  if (keyReady) mbedtls_gcm_free(&cipher);
  keyReady = false;
  frameSize = 0;
  sent = received = 0;
}

void disconnectLink() {
  const int id = connection.load();
  if (id != kDisconnected) server->disconnect(static_cast<uint16_t>(id));
}

class ServerCallbacks : public BLEServerCallbacks {
  void onConnect(BLEServer* source, esp_ble_gatts_cb_param_t* event) override {
    if (!running.load() || connection.load() != kDisconnected) {
      source->disconnect(event->connect.conn_id);
      return;
    }
    uint8_t challenge[kChallengeSize];
    esp_fill_random(challenge, sizeof(challenge));
    capability->setValue(challenge, sizeof(challenge));
    connectionGeneration.fetch_add(1);
    connection.store(event->connect.conn_id);
  }

  void onDisconnect(BLEServer*, esp_ble_gatts_cb_param_t* event) override {
    int id = event->disconnect.conn_id;
    connection.compare_exchange_strong(id, kDisconnected);
  }
};

class RequestCallbacks : public BLECharacteristicCallbacks {
  void onWrite(BLECharacteristic* characteristic) override {
    if (!running.load() || connection.load() == kDisconnected) return;
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

bool initialize() {
  if (server) return true;
  static ServerCallbacks serverCallbacks;
  static RequestCallbacks requestCallbacks;
  static constexpr uint8_t identity[] = "identity";
  uint8_t id[32];
  if (!digest(identity, sizeof(identity) - 1, id)) return false;
  char name[18];
  snprintf(name, sizeof(name), "Kubo-%02x%02x%02x%02x%02x%02x",
           id[0], id[1], id[2], id[3], id[4], id[5]);

  input = xQueueCreate(8, sizeof(Incoming));
  if (!input) return false;
  BLEDevice::init(name);
  BLEDevice::setMTU(247);
  server = BLEDevice::createServer();
  server->setCallbacks(&serverCallbacks);
  auto* service = server->createService(BLEUUID(String(kService)), 14);
  capability = service->createCharacteristic(BLEUUID(String(kCapability)), BLECharacteristic::PROPERTY_READ);
  service->createCharacteristic(BLEUUID(String(kRequest)), BLECharacteristic::PROPERTY_WRITE)
      ->setCallbacks(&requestCallbacks);
  response = service->createCharacteristic(BLEUUID(String(kResponse)), BLECharacteristic::PROPERTY_READ);
  signal = service->createCharacteristic(BLEUUID(String(kSignal)), BLECharacteristic::PROPERTY_NOTIFY);
  signal->addDescriptor(new BLE2902());
  service->start();

  BLEAdvertisementData data;
  data.setFlags(ESP_BLE_ADV_FLAG_GEN_DISC | ESP_BLE_ADV_FLAG_BREDR_NOT_SPT);
  data.setCompleteServices(BLEUUID(String(kService)));
  BLEAdvertisementData scan;
  scan.setName(name);
  auto* broadcaster = BLEDevice::getAdvertising();
  broadcaster->setAdvertisementData(data);
  broadcaster->setScanResponseData(scan);
  broadcaster->setScanResponse(true);
  return true;
}

bool prepareLink(uint32_t generation) {
  resetLink();
  preparedGeneration = generation;
  connectedAt = millis();
  const String challenge = capability->getValue();
  uint8_t key[32];
  if (challenge.length() != kChallengeSize ||
      !digest(reinterpret_cast<const uint8_t*>(challenge.c_str()), challenge.length(), key)) return false;
  mbedtls_gcm_init(&cipher);
  keyReady = mbedtls_gcm_setkey(&cipher, MBEDTLS_CIPHER_ID_AES, key, 256) == 0;
  mbedtls_platform_zeroize(key, sizeof(key));
  if (!keyReady) mbedtls_gcm_free(&cipher);
  return keyReady;
}

void sendFrame(const String& plaintext) {
  if (connection.load() == kDisconnected || !keyReady ||
      plaintext.isEmpty() || plaintext.length() > kMaxPacket - kTagSize) {
    overflow.store(true);
    return;
  }
  uint8_t packet[kMaxPacket];
  uint8_t iv[12];
  nonce(iv, true, sent);
  const size_t size = plaintext.length();
  if (mbedtls_gcm_crypt_and_tag(&cipher, MBEDTLS_GCM_ENCRYPT, size, iv, sizeof(iv), nullptr, 0,
       reinterpret_cast<const uint8_t*>(plaintext.c_str()), packet, kTagSize, packet + size) != 0) {
    overflow.store(true);
    return;
  }
  ++sent;
  response->setValue(packet, size + kTagSize);
  uint8_t ready = 1;
  signal->setValue(&ready, 1);
  signal->notify();
}

bool receive(const Incoming& chunk) {
  if (chunk.length > sizeof(frame) - frameSize) return false;
  memcpy(frame + frameSize, chunk.bytes, chunk.length);
  frameSize += chunk.length;
  if (frameSize < 2) return true;
  const size_t length = (static_cast<size_t>(frame[0]) << 8) | frame[1];
  if (length <= kTagSize || length > kMaxPacket || frameSize > length + 2) return false;
  if (frameSize < length + 2) return true;

  uint8_t iv[12];
  nonce(iv, false, received);
  const size_t plainSize = length - kTagSize;
  char plaintext[kMaxPacket - kTagSize + 1];
  if (mbedtls_gcm_auth_decrypt(&cipher, plainSize, iv, sizeof(iv), nullptr, 0,
       frame + 2 + plainSize, kTagSize, frame + 2,
       reinterpret_cast<uint8_t*>(plaintext)) != 0) return false;
  frameSize = 0;
  ++received;
  connectedAt = millis();
  plaintext[plainSize] = '\0';
  if (plaintext[plainSize - 1] != '\n' || strlen(plaintext) != plainSize) return false;
  String line(plaintext);
  line.trim();
  return handleBluetoothSyncLine(line, sendFrame);
}
}  // namespace

bool startBluetoothSync() {
  if (running.load()) return true;
  if (!initialize() || connection.load() != kDisconnected) return false;
  resetLink();
  takeBluetoothSyncDrained();
  xQueueReset(input);
  overflow.store(false);
  advertisedAt = millis();
  running.store(true);
  BLEDevice::startAdvertising();
  advertising = true;
  return true;
}

void stopBluetoothSync() {
  if (!running.exchange(false)) return;
  if (advertising) BLEDevice::stopAdvertising();
  advertising = false;
  disconnectLink();
  resetLink();
}

bool pollBluetoothSync() {
  if (!running.load()) return false;
  if (connection.load() == kDisconnected) {
    if (keyReady) resetLink();
    if (takeBluetoothSyncDrained() || millis() - advertisedAt >= kAdvertiseMs) {
      stopBluetoothSync();
      return false;
    }
    if (!advertising) {
      overflow.store(false);
      BLEDevice::startAdvertising();
      advertising = true;
    }
    return true;
  }

  advertising = false;
  const uint32_t generation = connectionGeneration.load();
  if (preparedGeneration != generation && !prepareLink(generation)) {
    disconnectLink();
    return true;
  }
  Incoming chunk;
  bool valid = keyReady && !overflow.load();
  while (valid && xQueueReceive(input, &chunk, 0) == pdTRUE) {
    if (chunk.generation == generation && connection.load() != kDisconnected)
      valid = receive(chunk);
  }
  if (!valid || (!bluetoothSyncActive() && millis() - connectedAt >= kUnauthenticatedMs)) {
    disconnectLink();
    resetLink();
  }
  return true;
}
#else
bool startBluetoothSync() { return false; }
bool pollBluetoothSync() { return false; }
void stopBluetoothSync() {}
#endif
