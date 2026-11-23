"""Write the two candidate client credentials into an owner-private directory."""
import argparse
import hashlib
import json
from pathlib import Path
import secrets


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    directory = parser.parse_args().directory
    if any(directory.iterdir()):
        parser.error("Provision keys in an empty, owner-private directory.")

    clients = {"local-dictation.key": ["transcription", "speech", "models"],
               "bridge.key": ["transcription"]}
    registry = {}
    for filename, scopes in clients.items():
        key = secrets.token_urlsafe(32)
        with (directory / filename).open("x", encoding="ascii") as handle:
            handle.write(key + "\n")
        registry[hashlib.sha256(key.encode("ascii")).hexdigest()] = scopes
    with (directory / "registry.json").open("x", encoding="utf-8") as handle:
        json.dump(registry, handle, indent=2)


if __name__ == "__main__":
    main()
