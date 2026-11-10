"""Stop only this project's server, identified by its health reply and command line."""
import json
from pathlib import Path
import urllib.request
import psutil
from asr import ROOT

try:
    with urllib.request.urlopen('http://127.0.0.1:8123/health',timeout=3) as r:
        status=json.load(r)
except OSError:
    print('Server is not responding; no process was stopped.')
    raise SystemExit(1)
if status.get('service')!='palanote-asr':
    raise SystemExit('Port belongs to another service; no process was stopped.')
p=psutil.Process(status['pid'])
args=p.cmdline()
expected=(ROOT/'server.py').resolve()
if not any(Path(x).resolve()==expected for x in args if x.endswith('server.py')):
    raise SystemExit('Server process identity did not match; no process was stopped.')
if status.get('busy'):
    raise SystemExit('A transcription is running; wait until it completes before stopping.')
p.terminate()
p.wait(timeout=10)
print('Local ASR server stopped. Model memory released.')
