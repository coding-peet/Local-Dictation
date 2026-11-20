import json
from http.server import BaseHTTPRequestHandler
import os
from pathlib import Path
import queue
import socket
import ssl
import sys
import tempfile
import threading
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))

from bridge_discovery import (
    allowed_addresses, authorized_device, make_tls_context, reconcile,
    bridge_name, network_for_address, Listener, PORT, serve,
)
import bridge
from pair import pair


class DiscoveryTests(unittest.TestCase):
    def test_includes_changed_address_and_excludes_unapproved_interface(self):
        config = {"host": "192.168.1.31", "discovery_networks": ["192.168.1.0/24"]}
        self.assertEqual(allowed_addresses(config, ["192.168.1.63", "10.59.167.226"]), {"192.168.1.63"})
        self.assertEqual(allowed_addresses(config, ["10.59.167.226"]), set())
        self.assertEqual(network_for_address("192.168.1.31", [("192.168.1.31", 24)]), "192.168.1.0/24")
        self.assertEqual(network_for_address("192.168.1.31", []), "192.168.1.31/32")
        self.assertEqual(allowed_addresses(config, ["192.168.1.31"]), {"192.168.1.31"})
        self.assertEqual(allowed_addresses({**config, "discovery_networks": ["0.0.0.0/0"]}, ["10.59.167.226"]), set())

    def test_rebind_drops_old_listener_before_opening_new_one(self):
        events = []
        listeners = {"192.168.1.31": SimpleNamespace(close=lambda: events.append("close 31"))}

        def start(address):
            events.append("start " + address)
            return SimpleNamespace(close=lambda: None)

        reconcile(listeners, {"192.168.1.62"}, start)
        reconcile(listeners, {"192.168.1.62"}, start)
        self.assertEqual(events, ["close 31", "start 192.168.1.62"])

    @patch.dict(sys.modules, {"zeroconf": Mock()})
    def test_managed_shutdown_closes_listener_and_stops_discovery(self):
        stop = threading.Event()
        listener = Mock()
        listener.refresh_discovery.side_effect = stop.set
        config = {"host": "192.168.1.31"}
        with patch("bridge_discovery.make_tls_context"), \
                patch("bridge_discovery.ipv4_interfaces", return_value=[("192.168.1.31", 24)]) as interfaces, \
                patch("bridge_discovery.Listener", return_value=listener):
            serve(Path("."), config, None, None, stop=stop)
        listener.close.assert_called_once_with()
        interfaces.assert_called_once_with()

    def test_managed_bridge_waits_for_its_parent_to_close_stdin(self):
        read_fd, write_fd = os.pipe()
        with os.fdopen(read_fd, "rb") as parent_input:
            try:
                stop = bridge.managed_stop_event(parent_input)
                self.assertFalse(stop.is_set())
            finally:
                os.close(write_fd)
            self.assertTrue(stop.wait(2))

    def test_pairing_is_stable_and_peer_tokens_are_distinct(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            a_path = root / "a"
            b_path = root / "b"
            a = self.pair(a_path, "device-1")
            b = self.pair(b_path, "device-1")
            again = self.pair(a_path, "device-1", network="10.59.167.0/24", address="192.168.1.62")
            other = self.pair(a_path, "device-2")
            self.assertEqual(a["discovery_id"], again["discovery_id"])
            self.assertNotEqual(a["discovery_id"], b["discovery_id"])
            self.assertEqual(a["device_tokens"]["device-1"], again["device_tokens"]["device-1"])
            self.assertIn("10.59.167.0/24", other["discovery_networks"])
            token = a["device_tokens"]["device-1"]
            self.assertNotEqual(token, other["device_tokens"]["device-2"])
            self.assertTrue(authorized_device(other, "Bearer " + token))
            self.assertFalse(authorized_device(b, "Bearer " + token))
            self.assertFalse(authorized_device(a, "Bearer " + (a_path / "phone-token").read_text()))
            self.assertFalse(authorized_device(b, "Bearer ż"))
            self.assertEqual((a_path / "cert.pem").read_bytes(), (a_path / "legacy-cert.copy").read_bytes())
            self.assertEqual(json.loads((a_path / "pala-phone.json").read_text())["host"], "192.168.1.31")

    def test_tls_name_and_trust_stay_the_same_when_tcp_endpoint_changes(self):
        with tempfile.TemporaryDirectory() as directory:
            a_path, b_path = Path(directory) / "a", Path(directory) / "b"
            a, b = self.pair(a_path, "dev"), self.pair(b_path, "dev")
            hostname = bridge_name(a)
            trust = ssl.create_default_context(cafile=str(a_path / "discovery-cert.pem"))
            a_context = make_tls_context(a_path, a, legacy=True)
            b_context = make_tls_context(b_path, b, legacy=False)
            self.exchange(a_context, trust, hostname)
            self.exchange(make_tls_context(a_path, a, legacy=False), trust, hostname, address="127.0.0.2")
            with self.assertRaises(ssl.SSLCertVerificationError):
                self.exchange(b_context, trust, hostname)
            with self.assertRaises(ssl.SSLCertVerificationError):
                self.exchange(a_context, trust, bridge_name(b))
            legacy = ssl.create_default_context(cafile=str(a_path / "cert.pem"))
            self.exchange(a_context, legacy, "192.168.1.31")

    def test_published_bridge_answers_a_query_for_its_exact_name(self):
        from zeroconf import IPVersion, Zeroconf
        from zeroconf._services.info import AddressResolverIPv4

        with tempfile.TemporaryDirectory() as directory:
            state = Path(directory)
            config = self.pair(state, "fixture")
            publisher = Listener("127.0.0.1", state, config, BaseHTTPRequestHandler, None)
            try:
                with Zeroconf(interfaces=["127.0.0.1"], ip_version=IPVersion.V4Only) as client:
                    own = AddressResolverIPv4(bridge_name(config) + ".")
                    self.assertTrue(own.request(client, 3000))
                    self.assertEqual(own.parsed_addresses(), ["127.0.0.1"])
                    peer = AddressResolverIPv4("pala-000000000000000000000000.local.")
                    self.assertFalse(peer.request(client, 300))
                    self.assertEqual(publisher.service.port, PORT)
            finally:
                publisher.close()

    @staticmethod
    def pair(state, device, network=None, address="192.168.1.31"):
        args = SimpleNamespace(ip=address, model="fixture", lat=0, lon=0,
                               destination="google", timezone="UTC", calendar="primary",
                               shopping="shopping", work="work", voice="voice",
                               device_id=device, networks=[network] if network else [])
        pair(state, state / "bridge_pairing.h", args)
        if not (state / "legacy-cert.copy").exists():
            (state / "legacy-cert.copy").write_bytes((state / "cert.pem").read_bytes())
        return json.loads((state / "config.json").read_text())

    def exchange(self, server_context, client_context, hostname, address="127.0.0.1"):
        with socket.socket() as listener:
            listener.bind((address, 0))
            listener.listen()
            listener.settimeout(4)
            errors = queue.Queue()

            def serve():
                try:
                    connection, _ = listener.accept()
                    with connection:
                        connection.settimeout(3)
                        with server_context.wrap_socket(connection, server_side=True) as tls:
                            tls.sendall(b"ok")
                except OSError as error:
                    errors.put(error)

            worker = threading.Thread(target=serve)
            worker.start()
            try:
                with socket.create_connection(listener.getsockname(), timeout=3) as client:
                    with client_context.wrap_socket(client, server_hostname=hostname) as tls:
                        self.assertEqual(tls.recv(2), b"ok")
                self.assertTrue(errors.empty())
            finally:
                worker.join(timeout=5)
                self.assertFalse(worker.is_alive())


if __name__ == "__main__":
    unittest.main()
