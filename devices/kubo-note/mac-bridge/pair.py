"""Pair one ESP32 build with a Mac on its private network."""
import argparse
import ipaddress
import json
import os
from pathlib import Path
import secrets
import shutil
import subprocess
import tempfile

from bridge_discovery import bridge_name, ipv4_interfaces, network_for_address, private_network

STATE = (Path(os.environ["LOCALAPPDATA"]) / "PalaBridge" if os.name == "nt"
         else Path.home() / "Library/Application Support/PalaBridge")
# A private profile prepared alongside this checkout takes precedence on macOS.
local_profile = Path(__file__).resolve().parents[1] / ".pala-personal"
if os.name != "nt" and (local_profile / "config.json").is_file():
    STATE = local_profile
WINDOWS_DICTATION_MODEL = "kinit/whisper-large-v3-turbo-sk"
HEADER = Path(__file__).resolve().parents[1] / "pala_note/bridge_pairing.h"


def certificate(state, name, stem, san):
    openssl = shutil.which("openssl") or str(Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "Git/usr/bin/openssl.exe")
    with tempfile.TemporaryDirectory(dir=state) as directory:
        key, cert = Path(directory) / "key.pem", Path(directory) / "cert.pem"
        result = subprocess.run(
            [openssl, "req", "-x509", "-newkey", "rsa:2048", "-nodes",
             "-keyout", str(key), "-out", str(cert), "-days", "730",
             "-subj", "/CN=" + name, "-addext", "subjectAltName=" + san],
            capture_output=True,
        )
        if result.returncode:
            raise RuntimeError("Could not create the Bridge TLS certificate.")
        key.replace(state / f"{stem}key.pem")
        cert.replace(state / f"{stem}cert.pem")


def pair(state, header, args):
    state.mkdir(mode=0o700, parents=True, exist_ok=True)
    config = state / "config.json"
    host = str(ipaddress.IPv4Address(args.ip))
    if not (-90 <= args.lat <= 90 and -180 <= args.lon <= 180):
        raise ValueError("Neplatné súradnice predajne.")
    device_id = args.device_id.strip()
    if not device_id or len(device_id) > 128:
        raise ValueError("Select a device with a stable USB serial number or supply a device ID.")
    explicit_networks = {str(private_network(value)) for value in args.networks}
    try:
        interfaces = ipv4_interfaces()
    except ModuleNotFoundError:
        interfaces = []
    approval = network_for_address(host, interfaces)
    # Existing Android and legacy firmware continue using their original IP and certificate.
    if config.exists():
        data = json.loads(config.read_text())
    else:
        data = {"token": secrets.token_urlsafe(32), "host": host}
        certificate(state, host, "", "IP:" + host)
    if "discovery_id" not in data:
        data["discovery_id"] = "pala-" + secrets.token_hex(12)
        hostname = bridge_name(data)
        certificate(state, hostname, "discovery-", "DNS:" + hostname)
    else:
        hostname = bridge_name(data)
        if not all((state / name).is_file() for name in ("discovery-cert.pem", "discovery-key.pem")):
            raise ValueError("Restore the existing discovery certificate and key from backup.")
    data["discovery_networks"] = sorted(
        set(data.get("discovery_networks", [])) | explicit_networks | {approval}
    )
    tokens = data.setdefault("device_tokens", {})
    token = tokens.setdefault(device_id, secrets.token_urlsafe(32))
    if os.name == "nt":
        data.pop("model", None)
        data["transcription_model"] = str(args.model)
    else:
        data["model"] = str(Path(args.model).expanduser().resolve())
    data.update(lat=args.lat, lon=args.lon,
                destination=args.destination, timezone=args.timezone, calendar=args.calendar, shopping=args.shopping, work=args.work, voice=args.voice)
    with tempfile.NamedTemporaryFile(dir=state, mode="w", delete=False) as tmp:
        json.dump(data, tmp)
    os.replace(tmp.name, config)
    cert = (state / "cert.pem").read_text()
    if args.destination == "google":
        # A separate read-only credential exposes only personal shopping items.
        phone_token = (state / "phone-token").read_text() if (state / "phone-token").exists() else secrets.token_urlsafe(32)
        (state / "phone-token").write_text(phone_token)
        phone = dict(host=data["host"], token=phone_token, cert=cert, lat=args.lat, lon=args.lon)
        (state / "pala-phone.json").write_text(json.dumps(phone))

    discovery_cert = (state / "discovery-cert.pem").read_text()
    header.write_text(f'''#pragma once
#define PALA_MAC_BRIDGE 1
#define PALA_BRIDGE_DISCOVERY 1
#define PALA_BRIDGE_ID "{data['discovery_id']}"
#define PALA_HOST "{hostname}"
#define PALA_PORT 8443
#define PALA_AUTHORITY "{hostname}:8443"
#define PALA_BRIDGE_TOKEN "{token}"
static const char PALA_BRIDGE_CERT[] = R"PEM({discovery_cert})PEM";
''')
    print("Párovanie pripravené. Zostav a nahraj firmvér Pala Note 2.0.")


if __name__ == "__main__":
    os.umask(0o077)
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--ip", required=True)
    p.add_argument("--allow-network", action="append", dest="networks", default=[],
                   help="Also listen and advertise on this approved private IPv4 CIDR.")
    p.add_argument("--device-id", required=True, help="Stable local ID of the Pala being paired.")
    p.add_argument("--model", required=True)
    p.add_argument("--lat", required=True, type=float)
    p.add_argument("--lon", required=True, type=float)
    p.add_argument("--destination", choices=["apple", "google"], default="apple")
    p.add_argument("--timezone", default="Europe/Bratislava")
    p.add_argument("--calendar", required=True)
    p.add_argument("--shopping", default="Nákup")
    p.add_argument("--work", default="Práca")
    p.add_argument("--voice", default="Laura")
    args = p.parse_args()
    if os.name != "nt" and not Path(args.model).expanduser().is_file():
        p.error("Chýba viacjazyčný model whisper.cpp.")
    pair(STATE, HEADER, args)
