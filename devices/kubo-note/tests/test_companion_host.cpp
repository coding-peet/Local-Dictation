#include "../pala_note/src/app/companion_host.h"
#include <cassert>

int main() {
  using namespace companion_host;
  assert(label("Kubo Note") == "kubo-note");
  assert(label("kubo-backup") == "kubo-backup");
  assert(label("kub-dev") == "kub-dev");
  assert(label("Kubo2") == "kubo2");
  assert(label("A  -- B-") == "a-b");
  const char* ip = "192.168.1.45";
  const char* name = "kubo-note.local";
  assert(accepts(name, ip, name));
  assert(accepts("kubo-note.local:80", ip, name));
  assert(accepts("192.168.1.45:80", ip, name));
  assert(!accepts("kubo-note.local.example", ip, name));
  assert(!accepts("kubo-note.local:8080", ip, name));
  assert(!accepts(name, ip, ""));
  assert(!accepts(name, "", name));
  assert(!accepts(":80", ip, ""));
}
