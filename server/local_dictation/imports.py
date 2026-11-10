"""Explicit inspect/download jobs with an atomic installation boundary."""
import asyncio
from dataclasses import dataclass
import hashlib
import os
from pathlib import Path
import shutil
import threading
import uuid
from starlette.responses import JSONResponse
from starlette.routing import Route
from .downloads import inspect_repository, download_snapshot
from .installation import validate_installation

class Imports:
    def __init__(self,config,catalog):
        self.config,self.catalog=config,catalog
        self.inspections,self.jobs={},{}
        self.tasks=set()

    async def inspect(self,request):
        try:
            data=await request.json()
            if len(self.inspections)>=16: self.inspections.pop(next(iter(self.inspections)))
            spec=await asyncio.to_thread(inspect_repository,str(data.get("repository","")))
            ident=str(uuid.uuid4())
            self.inspections[ident]=spec
            return JSONResponse({"inspection_id":ident,**spec})
        except (ValueError,OSError):
            return JSONResponse({"error":{"message":"Cannot import this public original Whisper model. Check its URL, safetensors files and built-in architecture."}},400)

    async def start(self,request):
        data=await request.json()
        spec=self.inspections.get(data.get("inspection_id"))
        if not spec: return JSONResponse({"error":{"message":"Inspect the repository again before downloading."}},400)
        if any(j["state"] in ("queued","downloading") for j in self.jobs.values()):
            return JSONResponse({"error":{"message":"A download is already running."}},409)
        if spec["id"] in self.catalog.entries:
            return JSONResponse({"error":{"message":"This model is already registered. Its revision is preserved."}},409)
        if len(self.jobs)>=16: self.jobs.pop(next(iter(self.jobs)))
        ident=str(uuid.uuid4())
        job={"id":ident,"model":spec["id"],"revision":spec["revision"],"state":"queued",
             "downloaded_bytes":0,"total_bytes":spec["download_bytes"],"cancel":threading.Event()}
        self.jobs[ident]=job
        task=asyncio.create_task(asyncio.to_thread(self.run,job,spec))
        self.tasks.add(task)
        task.add_done_callback(self.tasks.discard)
        return JSONResponse(self.public(job),202)

    @staticmethod
    def public(job):
        return {k:v for k,v in job.items() if k!="cancel"}

    def run(self,job,spec):
        root=(self.config.data_root.parent/"models").resolve()
        stage=(root/".staging"/job["id"]).resolve()
        final=root/(hashlib.sha256(spec["id"].encode()).hexdigest()[:16]+"-"+spec["revision"])
        if not stage.is_relative_to(root) or not final.is_relative_to(root):
            job.update(state="failed",message="Invalid installation path.")
            return
        published=False
        registered=False
        try:
            job["state"]="downloading"
            def progress(done,total): job["downloaded_bytes"]=done
            download_snapshot(spec,stage,job["cancel"],progress)
            if job["cancel"].is_set(): raise InterruptedError()
            validate_installation(stage,spec["files"])
            if final.exists(): raise ValueError("An installation already exists at this revision.")
            root.mkdir(parents=True,exist_ok=True)
            os.replace(stage,final)
            published=True
            self.catalog.add({**spec,"path":str(final)})
            registered=True
            job["state"]="completed"
        except InterruptedError:
            job.update(state="cancelled",message="Download cancelled.")
        except Exception:
            job.update(state="failed",message="Download or installation failed; existing models were preserved.")
        finally:
            if stage.exists(): shutil.rmtree(stage)
            if published and not registered and final.exists():shutil.rmtree(final)

    async def status(self,request):
        job=self.jobs.get(request.path_params["job"])
        if not job: return JSONResponse({"error":{"message":"Unknown download."}},404)
        return JSONResponse(self.public(job))

    async def cancel(self,request):
        job=self.jobs.get(request.path_params["job"])
        if not job: return JSONResponse({"error":{"message":"Unknown download."}},404)
        if job["state"] in ("queued","downloading"): job["cancel"].set()
        return JSONResponse(self.public(job))

    def routes(self):
        return [Route("/v1/model-imports/inspect",self.inspect,methods=["POST"]),
                Route("/v1/model-imports",self.start,methods=["POST"]),
                Route("/v1/model-imports/{job}",self.status),
                Route("/v1/model-imports/{job}/cancel",self.cancel,methods=["POST"])]
