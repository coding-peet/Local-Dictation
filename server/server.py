"""Loopback-only batch API for fast Slovak and precise multilingual CUDA ASR."""
import argparse
from email import policy
from email.parser import BytesParser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from io import BytesIO
import json
import logging
import os
from pathlib import Path
import threading
import time
from urllib.parse import urlsplit

from model_engine import WhisperEngine, FAST_MODEL_ID, PRECISE_MODEL_ID, MODELS, resolve_request

MODEL_ID = FAST_MODEL_ID
MAX_UPLOAD = 26 * 1024 * 1024
LOG = logging.getLogger('local-asr')


class LocalServer(ThreadingHTTPServer):
    daemon_threads = True
    allow_reuse_address = False

    def __init__(self, address, engine):
        if address[0] != '127.0.0.1':
            raise ValueError('This server may only bind to 127.0.0.1.')
        self.engine = engine
        self.gate = threading.Lock()
        self.completed = 0
        self.last_seconds = None
        super().__init__(address, Handler)


class Handler(BaseHTTPRequestHandler):
    server_version = 'LocalWhisper/2.0'

    def setup(self):
        super().setup()
        self.connection.settimeout(30)

    def log_message(self, fmt, *args):
        LOG.info('%s %s', self.client_address[0], fmt % args)

    def reply(self, status, payload, content_type='application/json; charset=utf-8'):
        data = payload.encode('utf-8') if isinstance(payload, str) else json.dumps(payload, ensure_ascii=False).encode('utf-8')
        self.send_response(status)
        self.send_header('Content-Type', content_type)
        self.send_header('Content-Length', str(len(data)))
        self.send_header('Cache-Control', 'no-store')
        self.end_headers()
        try:
            self.wfile.write(data)
        except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
            LOG.info('Client disconnected before response delivery.')

    def error(self, status, message):
        self.reply(status, {'error': {'message': message, 'type': 'invalid_request_error' if status < 500 else 'server_error'}})

    def allowed(self):
        if self.headers.get('Origin'):
            self.error(403, 'Browser-origin requests are not supported.')
            return False
        host = self.headers.get('Host', '').split(':')[0].lower()
        if host not in ('127.0.0.1', 'localhost'):
            self.error(403, 'A loopback Host header is required.')
            return False
        return True

    def do_GET(self):
        if not self.allowed(): return
        path = urlsplit(self.path).path.rstrip('/')
        if path == '/v1/models':
            self.reply(200, {'object': 'list', 'data': [
                {'id': key, 'object': 'model', 'created': 0, 'owned_by': key.split('/')[0]} for key in MODELS]})
        elif path == '/health':
            self.reply(200, {'service': 'palanote-asr', **self.server.engine.status(), 'pid': os.getpid(),
                'busy': self.server.gate.locked(), 'completed': self.server.completed,
                'last_inference_seconds': self.server.last_seconds})
        else:
            self.error(404, 'Endpoint not found. This service only provides audio transcription.')

    def do_POST(self):
        if not self.allowed(): return
        if urlsplit(self.path).path.rstrip('/') != '/v1/audio/transcriptions':
            self.error(404, 'Only /v1/audio/transcriptions is supported; translation and AI rewriting are unavailable.')
            return
        if not self.server.gate.acquire(blocking=False):
            self.error(409, 'A transcription is already running. Wait until it finishes.')
            return
        status = 500
        payload = {'error': {'message': 'Local transcription failed; check the server log.', 'type': 'server_error'}}
        content_type = 'application/json; charset=utf-8'
        started = None
        result = None
        try:
            fields, data = self.read_upload()
            model_id, language = resolve_request(fields.get('model', ''), fields.get('language'))
            fmt = fields.get('response_format', 'json')
            if fmt not in ('json', 'verbose_json', 'text'):
                raise ValueError('Supported response formats: json, verbose_json, text.')
            started = time.perf_counter()
            LOG.info('Transcription started; upload_bytes=%d', len(data))
            result = self.server.engine.transcribe(data, fields.get('prompt', ''), model_id, language)
            text = result['text']
            duration = result['duration']
            status = 200
            if fmt == 'text':
                payload = text
                content_type = 'text/plain; charset=utf-8'
            elif fmt == 'verbose_json':
                payload = {'task': 'transcribe', **result, 'segments': []}
            else:
                payload = {'text': text}
        except ValueError as exc:
            status = 400
            payload = {'error': {'message': str(exc), 'type': 'invalid_request_error'}}
        except Exception:
            LOG.exception('Transcription failed.')
            status = 500
            payload = {'error': {'message': 'Local transcription failed; check the server log.', 'type': 'server_error'}}
            content_type = 'application/json; charset=utf-8'
        finally:
            # Only the admitted request owns model cleanup. Complete it before
            # replying, after exception scopes have released inference frames.
            try:
                try:
                    self.server.engine.unload()
                except Exception:
                    LOG.exception('GPU model cleanup failed.')
                    status = 500
                    payload = {'error': {'message': 'GPU model cleanup failed; check the server log.', 'type': 'server_error'}}
                    content_type = 'application/json; charset=utf-8'
                if status == 200:
                    self.server.last_seconds = round(time.perf_counter() - started, 3)
                    self.server.completed += 1
                    LOG.info('Transcription completed and model released; audio_seconds=%.3f; processing_seconds=%.3f',
                        duration, self.server.last_seconds)
            finally:
                self.server.gate.release()
            self.reply(status, payload, content_type)

    def read_upload(self):
        if self.headers.get('Transfer-Encoding'):
            raise ValueError('Chunked uploads are not supported; send Content-Length.')
        try:
            length = int(self.headers.get('Content-Length', '0'))
        except ValueError:
            raise ValueError('Invalid Content-Length.') from None
        if not 0 < length <= MAX_UPLOAD:
            raise ValueError('Upload must be nonempty and at most 26 MiB including form fields.')
        content_type = self.headers.get('Content-Type', '')
        if '\r' in content_type or '\n' in content_type or not content_type.lower().startswith('multipart/form-data;'):
            raise ValueError('Expected multipart/form-data with a boundary.')
        body = self.rfile.read(length)
        if len(body) != length:
            raise ValueError('Incomplete upload.')
        message = BytesParser(policy=policy.default).parsebytes(
            ('Content-Type: ' + content_type + '\r\nMIME-Version: 1.0\r\n\r\n').encode('ascii') + body)
        if not message.is_multipart() or message.defects:
            raise ValueError('Invalid multipart upload.')
        fields, files = {}, []
        for part in message.iter_parts():
            name = part.get_param('name', header='content-disposition')
            value = part.get_payload(decode=True)
            if value is None: raise ValueError('Invalid form field.')
            if name == 'file':
                files.append(value)
            elif name in ('model', 'prompt', 'language', 'response_format'):
                if name in fields or len(value) > 16384: raise ValueError('Duplicate or oversized form field.')
                fields[name] = value.decode('utf-8')
        if len(files) != 1 or not files[0]:
            raise ValueError('Exactly one nonempty file field is required.')
        return fields, files[0]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--port', type=int, default=8123)
    args = parser.parse_args()
    logging.basicConfig(level=logging.INFO, format='%(asctime)s %(levelname)s %(message)s')
    # Bind first: a second launch must not load another copy of the model.
    with LocalServer(('127.0.0.1', args.port), None) as server:
        server.engine = WhisperEngine()
        server.engine.prepare_runtime()
        LOG.info('Ready: http://127.0.0.1:%d ; audio/prompt/transcripts are not retained by this server.', args.port)
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            LOG.info('Server stopped.')


if __name__ == '__main__':
    main()
