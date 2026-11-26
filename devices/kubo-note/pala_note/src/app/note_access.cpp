#include "note_access.h"

#include <Preferences.h>
#include <WebServer.h>
#include <mbedtls/pkcs5.h>
#include <mbedtls/platform_util.h>

namespace {
constexpr size_t kSaltBytes = 16;
constexpr size_t kHashBytes = 32;
constexpr size_t kTokenBytes = 24;
constexpr unsigned kPasswordRounds = 60000;
constexpr uint32_t kSessionMs = 15 * 60 * 1000;
constexpr char kRecordKey[] = "web_auth_v1";
constexpr char kOpenAccessKey[] = "allow_no_pw";
constexpr char kCookieName[] = "pala_note_session=";
// Keep the inline login script limited to this hash in the response policy.
constexpr char kLoginEyeScript[] =
    "const p=document.getElementById('login-password'),b=document.getElementById('login-eye');b.hidden=false;b.onclick=()=>{const visible=p.type==='password';p.type=visible?'text':'password';const label=visible?'Hide password':'Show password';b.setAttribute('aria-pressed',String(visible));b.setAttribute('aria-label',label);b.title=label;};";

struct Credential {
  uint8_t salt[kSaltBytes];
  uint8_t hash[kHashBytes];
};
struct Session {
  String token;
  uint32_t lastSeen = 0;
};
Preferences prefs;
Credential credential{};
bool storageOpen = false;
bool configured = false;
bool passwordRequired = true;
Session webSession;
Session bluetoothSession;
uint8_t failedAttempts = 0;
uint32_t lastFailure = 0;

Session& sessionFor(NoteConfigTransport transport) {
  return transport == NoteConfigTransport::Web ? webSession : bluetoothSession;
}

bool openStorage() {
  if (!storageOpen) {
    storageOpen = prefs.begin("pala_access", false);
    if (storageOpen) passwordRequired = prefs.getUChar(kOpenAccessKey, 0) != 1;
    if (storageOpen && prefs.isKey(kRecordKey)) {
      configured = prefs.getBytesLength(kRecordKey) == sizeof(credential) &&
                   prefs.getBytes(kRecordKey, &credential, sizeof(credential)) == sizeof(credential);
    }
  }
  return storageOpen;
}

bool validPassword(const String& password) {
  if (password.length() < 8 || password.length() > 48) return false;
  for (size_t index = 0; index < password.length(); ++index) {
    const unsigned char ch = password[index];
    if (ch < 0x20 || ch > 0x7e) return false;
  }
  return true;
}

bool constantEqual(const void* first, const void* second, size_t size) {
  auto a = static_cast<const uint8_t*>(first);
  auto b = static_cast<const uint8_t*>(second);
  uint8_t difference = 0;
  for (size_t index = 0; index < size; ++index) difference |= a[index] ^ b[index];
  return difference == 0;
}

bool derive(const String& password, const uint8_t* salt, uint8_t* hash) {
  return mbedtls_pkcs5_pbkdf2_hmac_ext(MBEDTLS_MD_SHA256,
      reinterpret_cast<const uint8_t*>(password.c_str()), password.length(),
      salt, kSaltBytes, kPasswordRounds, kHashBytes, hash) == 0;
}

const char* verifyPassword(const String& password) {
  if (!noteAccessConfigured()) return "Set the Note password from a paired USB connection.";
  const uint32_t backoffMs = failedAttempts >= 5 ? 30000 : 1500;
  if (failedAttempts && millis() - lastFailure < backoffMs) return "Wait before trying again.";
  uint8_t hash[kHashBytes]{};
  const bool match = validPassword(password) && derive(password, credential.salt, hash) &&
                     constantEqual(hash, credential.hash, sizeof(hash));
  mbedtls_platform_zeroize(hash, sizeof(hash));
  if (match) {
    failedAttempts = 0;
    return nullptr;
  }
  if (failedAttempts < 5) ++failedAttempts;
  lastFailure = millis();
  return "Incorrect password.";
}

String newToken() {
  uint8_t bytes[kTokenBytes]{};
  String token;
  if (noteConfigRandom(bytes, sizeof(bytes)) && token.reserve(sizeof(bytes) * 2)) {
    for (uint8_t byte : bytes) {
      token += "0123456789abcdef"[byte >> 4];
      token += "0123456789abcdef"[byte & 15];
    }
  }
  mbedtls_platform_zeroize(bytes, sizeof(bytes));
  return token;
}

String cookieToken(const String& cookies) {
  int start = 0;
  while (start < static_cast<int>(cookies.length())) {
    const int end = cookies.indexOf(';', start);
    String cookie = end < 0 ? cookies.substring(start) : cookies.substring(start, end);
    cookie.trim();
    if (cookie.startsWith(kCookieName)) return cookie.substring(sizeof(kCookieName) - 1);
    if (end < 0) break;
    start = end + 1;
  }
  return {};
}

void sendLogin(WebServer& server, const char* error = nullptr) {
  // Form navigation needs its same-origin Origin header for the write guard.
  String content = F("<!doctype html><html lang='en'><meta charset='utf-8'>"
      "<meta name='viewport' content='width=device-width,initial-scale=1'>"
      "<meta name='referrer' content='same-origin'><title>Note sign in</title>"
      "<style>:root{color-scheme:light dark;font:1em system-ui}main{max-width:24rem;"
      "margin:8vh auto;padding:1.25rem}form,label{display:grid;gap:.75rem}input,button"
      "{font:inherit;padding:.65rem;border-radius:.55rem;border:1px solid GrayText}"
      "button{justify-self:start}.password{position:relative}.password input{box-sizing:border-box;"
      "width:100%;padding-right:3rem}.password button{position:absolute;right:.25rem;top:.15rem;"
      "bottom:.15rem;padding:0 .6rem;border:0;background:transparent;color:inherit;cursor:pointer}"
      ".password input::-ms-reveal{display:none}</style><main><h1>Sign in to Note</h1>");
  if (error) content += String("<p role='alert'>") + error + "</p>";
  if (noteAccessConfigured()) {
    content += F("<form method='post' action='/login'><label for='login-password'>Note password</label>"
                 "<div class='password'><input id='login-password' type='password' name='password' "
                 "minlength='8' maxlength='48' autocomplete='current-password' required autofocus>"
                 "<button id='login-eye' type='button' aria-controls='login-password' aria-label='Show password' "
                 "title='Show password' aria-pressed='false' hidden>&#128065;&#65038;</button></div>"
                 "<button type='submit'>Sign in</button></form><script>");
    content += kLoginEyeScript;
    content += "</script>";
  } else {
    content += F("<p>Set the Note password in Note settings over a paired USB connection.</p>");
  }
  content += "</main>";
  server.sendHeader("Content-Security-Policy",
      "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; frame-ancestors 'none'; "
      "script-src 'sha256-HyDLo0pGfof+6Mw5khR3qzT7XOWDLgxBS49ltd5LIBw='");
  server.sendHeader("X-Frame-Options", "DENY");
  server.send(error ? 401 : 200, "text/html; charset=utf-8", content);
}

void redirectTo(WebServer& server, const char* path) {
  server.sendHeader("Location", path);
  server.send(303);
}
}  // namespace

bool noteAccessConfigured() {
  return openStorage() && configured;
}

bool noteAccessPasswordRequired() {
  return !openStorage() || passwordRequired;
}

const char* noteAccessSetPasswordRequired(bool required) {
  if (!openStorage()) return "Could not open Note password storage.";
  if (passwordRequired == required) return nullptr;
  const uint8_t open = required ? 0 : 1;
  if (prefs.putUChar(kOpenAccessKey, open) != sizeof(open) || prefs.getUChar(kOpenAccessKey, 0xff) != open)
    return "Could not save password protection.";
  passwordRequired = required;
  webSession = {};
  bluetoothSession = {};
  failedAttempts = 0;
  return nullptr;
}

bool noteAccessSessionValid(NoteConfigTransport transport, const String& token) {
  if (transport == NoteConfigTransport::Usb || token.length() != kTokenBytes * 2) return false;
  auto& session = sessionFor(transport);
  if (session.token.length() != kTokenBytes * 2) return false;
  const uint32_t now = millis();
  if (now - session.lastSeen >= kSessionMs) {
    session = {};
    return false;
  }
  if (!constantEqual(session.token.c_str(), token.c_str(), kTokenBytes * 2)) return false;
  session.lastSeen = now;
  return true;
}

const char* noteAccessLogin(NoteConfigTransport transport, const String& password, String& token) {
  if (transport == NoteConfigTransport::Usb) return "Sign-in is available on a wireless connection.";
  if (const char* error = verifyPassword(password)) return error;
  token = newToken();
  if (token.length() != kTokenBytes * 2) return "Could not open a sign-in session.";
  sessionFor(transport) = {token, millis()};
  return nullptr;
}

void noteAccessLogout(NoteConfigTransport transport) {
  if (transport != NoteConfigTransport::Usb) sessionFor(transport) = {};
}

const char* noteAccessSave(bool localUsb, const String& oldPassword, const String& password, const String& repeat) {
  if (!validPassword(password)) return "Use 8 to 48 printable ASCII characters for the password.";
  if (password != repeat) return "Enter the new password twice.";
  if (!localUsb) {
    if (const char* error = verifyPassword(oldPassword)) return error;
  }
  if (!openStorage()) return "Could not open Note password storage.";
  Credential replacement{};
  const bool saved = noteConfigRandom(replacement.salt, sizeof(replacement.salt)) &&
                     derive(password, replacement.salt, replacement.hash) &&
                     prefs.putBytes(kRecordKey, &replacement, sizeof(replacement)) == sizeof(replacement);
  if (saved) {
    credential = replacement;
    configured = true;
    failedAttempts = 0;
    webSession = {};
    bluetoothSession = {};
  }
  mbedtls_platform_zeroize(&replacement, sizeof(replacement));
  return saved ? nullptr : "Could not save the Note password.";
}

bool noteAccessGuardHttp(WebServer& server) {
  server.sendHeader("Cache-Control", "no-store");
  if (!noteAccessPasswordRequired()) return true;
  const String uri = server.uri();
  if (uri == "/login" && (server.method() == HTTP_GET || server.method() == HTTP_POST)) return true;
  if (noteAccessSessionValid(NoteConfigTransport::Web, cookieToken(server.header("Cookie")))) return true;
  if (uri.startsWith("/api/")) {
    server.send(401, "application/json; charset=utf-8",
        noteAccessConfigured()
        ? F("{\"ok\":false,\"access\":\"locked\",\"transport\":\"Wi-Fi\",\"error\":\"Enter the Note password.\"}")
        : F("{\"ok\":false,\"access\":\"setup\",\"transport\":\"Wi-Fi\",\"error\":\"Set the Note password over USB.\"}"));
  } else {
    redirectTo(server, "/login");
  }
  return false;
}

void noteAccessHttp(WebServer& server) {
  server.on("/login", HTTP_GET, [&server]() {
    if (noteAccessPasswordRequired()) sendLogin(server);
    else redirectTo(server, "/");
  });
  server.on("/login", HTTP_POST, [&server]() {
    if (!noteAccessPasswordRequired()) { redirectTo(server, "/"); return; }
    String token;
    if (const char* error = noteAccessLogin(NoteConfigTransport::Web, server.arg("password"), token)) {
      sendLogin(server, error);
      return;
    }
    server.sendHeader("Set-Cookie", String(kCookieName) + token + "; Path=/; HttpOnly; SameSite=Strict");
    redirectTo(server, "/");
  });
}
