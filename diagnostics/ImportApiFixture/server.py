"""Synthetic loopback integration fixture. Never opens actual models or user files."""
import argparse,asyncio,json,sys,threading,time
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'server'))
from local_dictation.application import create_app
from local_dictation.catalog import Catalog,BUILTINS
from local_dictation.config import RuntimeConfig
from test_model_files import make_model
from starlette.responses import JSONResponse
from starlette.routing import Route
import uvicorn

parser=argparse.ArgumentParser();parser.add_argument('--root',type=Path,required=True);parser.add_argument('--port',type=int,default=48226);parser.add_argument('--max-seconds',type=int,default=1);args=parser.parse_args()
root=args.root.resolve();allowed=Path(r'C:\AI\build\Local-Dictation-personal-tools').resolve()
if not root.is_relative_to(allowed) or args.port in (8123,48125):raise SystemExit('Use a new fixture directory and a separate test port.')
root.mkdir(parents=True,exist_ok=False)
config=RuntimeConfig(root/'models',root/'state',progress_interval_seconds=.1,max_seconds=args.max_seconds)
for b in BUILTINS:make_model(config.model_root/b['folder'],b['id'],b['revision'])
catalog=Catalog(config)
for b in BUILTINS:catalog.mark_tested(b['id'])
class Engine:
    def __init__(self):self.blocking=False;self.cancelled=0;self.calls=0
    def status(self):return {'ready':True,'synthetic_fixture':True,'blocking':self.blocking,'cancelled_count':self.cancelled,'calls':self.calls}
    def transcribe_pcm(self,pcm,model,language,cancelled,preferred_languages=None,prompt=''):
        self.calls+=1
        if pcm[:2]==b'\0\x20':
            self.blocking=True
            try:
                deadline=time.monotonic()+20
                while not cancelled.wait(.005):
                    if time.monotonic()>deadline:raise RuntimeError('Synthetic fixture cancel deadline.')
                self.cancelled+=1;raise RuntimeError('Cancelled synthetic worker.')
            finally:self.blocking=False
        return {'text':'Synthetic primary words.' if model.startswith('kinit/') else 'Synthetic alternate words.','language':'sk','language_mode':'forced'}
    def unload(self):pass
engine=Engine();app=create_app(config,engine,catalog)
server=None
async def stop(request):
    server.should_exit=True;return JSONResponse({'stopping':True})
app.app.router.routes.append(Route('/__fixture/stop',stop,methods=['POST']))
server=uvicorn.Server(uvicorn.Config(app,host='127.0.0.1',port=args.port,log_level='warning',access_log=False))
print(json.dumps({'fixture':'synthetic_import','port':args.port,'root':str(root)}),flush=True)
server.run()
