"""Publish the paired HTTPS service on explicitly approved IPv4 networks."""
from ipaddress import IPv4Address, IPv4Network
import hmac
import logging
import re
import ssl
import threading
import time
from http.server import ThreadingHTTPServer

PORT = 8443
SERVICE = "_pala-bridge._tcp.local."
LOG = logging.getLogger("pala-bridge")
PRIVATE_NETWORKS = tuple(map(IPv4Network, ("10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16")))


def private_network(value):
    network = IPv4Network(value)
    if not any(network.subnet_of(private) for private in PRIVATE_NETWORKS):
        raise ValueError("Discovery requires an approved private IPv4 network in CIDR notation.")
    return network


def ipv4_interfaces():
    import ifaddr

    return [(ip.ip, ip.network_prefix) for adapter in ifaddr.get_adapters()
            for ip in adapter.ips if isinstance(ip.ip, str)]


def network_for_address(address, interfaces):
    prefix = next((prefix for ip, prefix in interfaces if ip == address), 32)
    return str(private_network(str(IPv4Network(f"{address}/{prefix}", strict=False))))


def allowed_addresses(config, addresses):
    networks = []
    for value in config.get("discovery_networks", []):
        try:
            networks.append(private_network(value))
        except ValueError:
            LOG.error("Ignoring an invalid discovery network.")
    return {address for address in addresses if address == config["host"] or
            any(IPv4Address(address) in network for network in networks)}


def bridge_name(config):
    identifier = config["discovery_id"]
    if not re.fullmatch(r"pala-[0-9a-f]{24}", identifier):
        raise ValueError("Invalid Pala Bridge identity.")
    return identifier + ".local"


def authorized_device(config, header):
    if not header.isascii() or not 0 < len(header) <= 128:
        return False
    tokens = [config["token"], *config.get("device_tokens", {}).values()]
    return any(hmac.compare_digest(header, "Bearer " + token) for token in tokens)


def make_tls_context(state, config, *, legacy):
    current = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    current.minimum_version = ssl.TLSVersion.TLSv1_2
    current.load_cert_chain(state / "discovery-cert.pem", state / "discovery-key.pem")
    if not legacy:
        return current

    original = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    original.minimum_version = ssl.TLSVersion.TLSv1_2
    original.load_cert_chain(state / "cert.pem", state / "key.pem")
    hostname = bridge_name(config)

    def select_certificate(socket, server_name, _):
        if server_name == hostname:
            socket.context = current

    original.sni_callback = select_certificate
    return original


def reconcile(listeners, desired, start):
    for address in sorted(listeners.keys() - desired):
        listeners.pop(address).close()
    for address in sorted(desired - listeners.keys()):
        try:
            listeners[address] = start(address)
        except OSError as error:
            LOG.warning("Cannot listen on %s:%s: %s", address, PORT, error)


class BridgeServer(ThreadingHTTPServer):
    """Perform TLS in the connection worker, with a bounded handshake."""

    def __init__(self, address, handler, bridge, tls):
        self.bridge = bridge
        self.tls = tls
        super().__init__(address, handler)

    def process_request_thread(self, request, client_address):
        try:
            request.settimeout(6)
            tls = self.tls.wrap_socket(request, server_side=True)
        except OSError:
            request.close()
            return
        super().process_request_thread(tls, client_address)


class Listener:
    def __init__(self, address, state, config, handler, bridge, *, drain=False):
        self.address = address
        self.hostname = bridge_name(config)
        tls = make_tls_context(state, config, legacy=address == config["host"])
        self.server = BridgeServer((address, PORT), handler, bridge, tls)
        self.server.daemon_threads = not drain
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.mdns = None
        self.service = None
        self.next_advertisement = 0.0
        self.refresh_discovery()
        print(f"Pala Bridge listens on {address}:{PORT} ({self.hostname}).", flush=True)

    def refresh_discovery(self):
        if self.mdns is not None or time.monotonic() < self.next_advertisement:
            return
        from zeroconf import IPVersion, ServiceInfo, Zeroconf

        publisher = None
        try:
            publisher = Zeroconf(interfaces=[self.address], ip_version=IPVersion.V4Only)
            service = ServiceInfo(
                SERVICE, self.hostname.removesuffix(".local") + "." + SERVICE,
                addresses=[IPv4Address(self.address).packed], port=PORT,
                server=self.hostname + ".", host_ttl=30, other_ttl=30,
            )
            publisher.register_service(service, cooperating_responders=True)
        except Exception as error:
            if publisher is not None:
                publisher.close()
            self.next_advertisement = time.monotonic() + 30
            LOG.warning("mDNS unavailable on %s (%s); manual IPv4 is available.",
                        self.address, type(error).__name__)
            return
        self.mdns, self.service = publisher, service

    def close(self):
        try:
            if self.mdns is not None:
                try:
                    self.mdns.unregister_service(self.service)
                finally:
                    self.mdns.close()
        except OSError:
            LOG.debug("Interface disappeared during mDNS shutdown.", exc_info=True)
        finally:
            self.server.shutdown()
            self.server.server_close()
            self.thread.join()


def serve(state, config, handler, bridge, *, stop=None, on_status=None, drain=False):
    # Check dependencies and certificates before opening any listener.
    import zeroconf  # noqa: F401

    make_tls_context(state, config, legacy=True)
    listeners = {}
    if stop is None:
        stop = threading.Event()
    try:
        while not stop.is_set():
            addresses = allowed_addresses(config, [ip for ip, _ in ipv4_interfaces()])
            reconcile(listeners, addresses,
                      lambda ip: Listener(ip, state, config, handler, bridge, drain=drain))
            for listener in listeners.values():
                listener.refresh_discovery()
            if on_status is not None:
                on_status(listeners, addresses)
            stop.wait(3)
    except KeyboardInterrupt:
        pass
    finally:
        reconcile(listeners, set(), None)
