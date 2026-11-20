#include "WifiCard.h"
#include <SD_MMC.h>
#include <esp_random.h>
#include <mbedtls/gcm.h>
#include <mbedtls/platform_util.h>
#include <memory>

namespace {
constexpr char keyName[] = "sd-aes-v1";
constexpr size_t maxPayload = 1024;
struct RecordFiles { const char* path; const char* previous; const char* pending; uint8_t magic[8]; };
constexpr RecordFiles wifiFiles{"/desk-wifi.bin", "/desk-wifi.bak", "/desk-wifi.tmp", {'D','E','S','K','W','I','F',1}};
constexpr RecordFiles settingsFiles{"/desk-settings.bin", "/desk-settings.bak", "/desk-settings.tmp", {'D','E','S','K','S','E','T',1}};

const RecordFiles& filesFor(CardRecord record) { return record == CardRecord::Settings ? settingsFiles : wifiFiles; }

struct CardHeader {
  uint8_t marker[8];
  uint8_t nonce[12];
  uint8_t tag[16];
};

bool mounted() {
  static bool started = false;
  if (started) return true;
  // Waveshare V2: on-board 1-bit SD wiring. Never format a card from firmware.
  if (!SD_MMC.setPins(39, 41, 40)) return false;
  started = SD_MMC.begin("/desk-sd", true, false);
  return started;
}

bool loadKey(Preferences& prefs, uint8_t (&key)[32]) {
  return prefs.getBytesLength(keyName) == sizeof(key) && prefs.getBytes(keyName, key, sizeof(key)) == sizeof(key);
}

bool openEncrypted(const RecordFiles& files, const char* filename, const uint8_t (&key)[32], void* data, size_t size) {
  File file = SD_MMC.open(filename, FILE_READ);
  CardHeader header{};
  std::unique_ptr<uint8_t[]> ciphertext(new (std::nothrow) uint8_t[size]);
  if (!file || file.size() != sizeof(header) + size || file.read(reinterpret_cast<uint8_t*>(&header), sizeof(header)) != sizeof(header) ||
      !ciphertext || memcmp(header.marker, files.magic, sizeof(files.magic)) || file.read(ciphertext.get(), size) != size) return false;
  mbedtls_gcm_context gcm;
  mbedtls_gcm_init(&gcm);
  int error = mbedtls_gcm_setkey(&gcm, MBEDTLS_CIPHER_ID_AES, key, sizeof(key) * 8);
  if (!error) error = mbedtls_gcm_auth_decrypt(&gcm, size, header.nonce, sizeof(header.nonce),
    files.magic, sizeof(files.magic), header.tag, sizeof(header.tag), ciphertext.get(), static_cast<uint8_t*>(data));
  mbedtls_gcm_free(&gcm);
  if (error) memset(data, 0, size);
  return !error;
}
}

WifiCardState loadWifiCard(Preferences& prefs, void* data, size_t size, CardRecord record) {
  if (!size || size > maxPayload) return WifiCardState::IoError;
  if (!mounted()) return WifiCardState::Missing;
  const RecordFiles& files = filesFor(record);
  if (!SD_MMC.exists(files.path) && !SD_MMC.exists(files.previous)) return WifiCardState::Empty;
  uint8_t key[32]{};
  if (!loadKey(prefs, key)) {
    mbedtls_platform_zeroize(key, sizeof(key));
    return WifiCardState::KeyMissing;
  }
  const bool ok = openEncrypted(files, files.path, key, data, size) || openEncrypted(files, files.previous, key, data, size);
  mbedtls_platform_zeroize(key, sizeof(key));
  return ok ? WifiCardState::Ready : WifiCardState::Invalid;
}

WifiCardState saveWifiCard(Preferences& prefs, const void* data, size_t size, CardRecord record) {
  if (!size || size > maxPayload) return WifiCardState::IoError;
  const RecordFiles& files = filesFor(record);
  std::unique_ptr<uint8_t[]> verified(new (std::nothrow) uint8_t[size]);
  if (!verified) return WifiCardState::IoError;
  const WifiCardState current = loadWifiCard(prefs, verified.get(), size, record);
  const bool matched = current == WifiCardState::Ready && !memcmp(data, verified.get(), size);
  mbedtls_platform_zeroize(verified.get(), size);
  if (matched) return WifiCardState::Ready;
  if (current != WifiCardState::Empty && current != WifiCardState::Ready) return current;

  // A card with an unrecoverable legacy backup may belong to a different device.
  if (record == CardRecord::Settings && current == WifiCardState::Empty &&
      (SD_MMC.exists(wifiFiles.path) || SD_MMC.exists(wifiFiles.previous)) && prefs.getBytesLength(keyName) != 32) {
    return WifiCardState::KeyMissing;
  }

  std::unique_ptr<uint8_t[]> ciphertext(new (std::nothrow) uint8_t[size]);
  if (!ciphertext) return WifiCardState::IoError;
  uint8_t key[32]{};
  if (!loadKey(prefs, key)) {
    esp_fill_random(key, sizeof(key));
    if (prefs.putBytes(keyName, key, sizeof(key)) != sizeof(key)) {
      mbedtls_platform_zeroize(key, sizeof(key));
      return WifiCardState::IoError;
    }
  }
  CardHeader header{};
  memcpy(header.marker, files.magic, sizeof(files.magic));
  esp_fill_random(header.nonce, sizeof(header.nonce));
  mbedtls_gcm_context gcm;
  mbedtls_gcm_init(&gcm);
  int error = mbedtls_gcm_setkey(&gcm, MBEDTLS_CIPHER_ID_AES, key, sizeof(key) * 8);
  if (!error) error = mbedtls_gcm_crypt_and_tag(&gcm, MBEDTLS_GCM_ENCRYPT, size, header.nonce, sizeof(header.nonce),
    files.magic, sizeof(files.magic), static_cast<const uint8_t*>(data), ciphertext.get(), sizeof(header.tag), header.tag);
  mbedtls_gcm_free(&gcm);
  mbedtls_platform_zeroize(key, sizeof(key));
  if (error) return WifiCardState::IoError;

  File file = SD_MMC.open(files.pending, FILE_WRITE);
  if (!file) return WifiCardState::IoError;
  const bool written = file.write(reinterpret_cast<const uint8_t*>(&header), sizeof(header)) == sizeof(header) &&
    file.write(ciphertext.get(), size) == size;
  file.flush();
  file.close();
  if (!written) return WifiCardState::IoError;

  const bool haveKey = loadKey(prefs, key);
  const bool pendingValid = haveKey && openEncrypted(files, files.pending, key, verified.get(), size) && !memcmp(data, verified.get(), size);
  const bool primaryExists = SD_MMC.exists(files.path);
  const bool primaryValid = pendingValid && primaryExists && openEncrypted(files, files.path, key, verified.get(), size);
  mbedtls_platform_zeroize(key, sizeof(key));
  mbedtls_platform_zeroize(verified.get(), size);
  if (!pendingValid) return WifiCardState::IoError;

  if (primaryValid) {
    if (SD_MMC.exists(files.previous) && !SD_MMC.remove(files.previous)) return WifiCardState::IoError;
    if (!SD_MMC.rename(files.path, files.previous)) return WifiCardState::IoError;
  } else if (primaryExists && !SD_MMC.remove(files.path)) {
    return WifiCardState::IoError; // Keep the last valid backup until the new file is in place.
  }
  if (!SD_MMC.rename(files.pending, files.path)) return WifiCardState::IoError;

  const bool ok = loadKey(prefs, key) && openEncrypted(files, files.path, key, verified.get(), size) && !memcmp(data, verified.get(), size);
  mbedtls_platform_zeroize(key, sizeof(key));
  mbedtls_platform_zeroize(verified.get(), size);
  if (ok) SD_MMC.remove(files.previous);
  return ok ? WifiCardState::Ready : WifiCardState::IoError;
}

const char* wifiCardLabel(WifiCardState state) {
  switch (state) {
    case WifiCardState::Missing: return "SD nedostupná";
    case WifiCardState::Empty: return "Na SD zatiaľ nie je záloha";
    case WifiCardState::Ready: return "Šifrované nastavenia sú na SD";
    case WifiCardState::KeyMissing: return "Chýba kľúč k zálohe na SD";
    case WifiCardState::Invalid: return "Záloha na SD sa nedá overiť";
    default: return "Chyba zápisu na SD";
  }
}
