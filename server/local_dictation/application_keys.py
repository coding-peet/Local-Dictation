"""Scope native loopback requests to a private registry of application key digests."""
import hashlib
import json
import re
from pathlib import Path

from starlette.datastructures import Headers
from starlette.responses import JSONResponse


SCOPES = {"transcription", "speech", "models"}
KEY = re.compile(r"[A-Za-z0-9_-]{32,128}")
DIGEST = re.compile(r"[0-9a-f]{64}")


def required_scopes(path):
    path = path.rstrip("/")
    if path == "/v1/audio/speech":
        return {"speech"}
    if path == "/v1/models/test":
        return {"transcription", "models"}
    if path.startswith("/v1/audio/"):
        return {"transcription"}
    if path == "/v1/models" or path.startswith(("/v1/models/", "/v1/model-imports")):
        return {"models"}
    return set()


class ApplicationKeyGuard:
    def __init__(self, app, registry):
        self.app = app
        self.registry = Path(registry)
        self.read_registry()

    def read_registry(self):
        entries = json.loads(self.registry.read_text(encoding="utf-8"))
        if not isinstance(entries, dict) or any(
            not DIGEST.fullmatch(digest) or not isinstance(scopes, list) or not scopes
            or any(not isinstance(scope, str) or scope not in SCOPES for scope in scopes)
            for digest, scopes in entries.items()
        ):
            raise ValueError("Invalid application key registry.")
        return entries

    def authorize(self, headers, required):
        values = headers.getlist("authorization")
        match = KEY.fullmatch(values[0][7:]) if len(values) == 1 and values[0].startswith("Bearer ") else None
        if match is None:
            return 401, "A valid application key is required."
        digest = hashlib.sha256(match[0].encode("ascii")).hexdigest()
        try:
            granted = self.read_registry().get(digest)
        except (OSError, ValueError):
            return 503, "The application key registry is unavailable."
        if granted is None:
            return 401, "A valid application key is required."
        if not required.issubset(granted):
            return 403, "The application key does not have this permission."
        return None

    async def __call__(self, scope, receive, send):
        if scope["type"] in ("http", "websocket"):
            required = required_scopes(scope["path"])
            rejected = self.authorize(Headers(scope=scope), required) if required else None
            if rejected:
                status, message = rejected
                if scope["type"] == "websocket":
                    await send({"type": "websocket.close", "code": 1008})
                else:
                    extra = {"WWW-Authenticate": "Bearer"} if status == 401 else None
                    await JSONResponse({"error": {"message": message, "type": "authorization_error"}},
                                       status_code=status, headers=extra)(scope, receive, send)
                return
        await self.app(scope, receive, send)
