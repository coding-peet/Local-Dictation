"""Explicit inspect/download jobs with a recoverable installation boundary."""
import asyncio
import hashlib
import os
from pathlib import Path
import shutil
import threading
import uuid
from starlette.responses import JSONResponse
from starlette.routing import Route
from .downloads import inspect_repository,download_snapshot
from .installation import validate_installation

class Imports:
    def __init__(self,config,catalog):
        self.config,self.catalog=config,catalog
        self.inspections,self.jobs={},{}
        self.tasks=set()

    async def inspect(self,request):
        try:
            data=await request.json()
            if len(self.inspections)>=16:self.inspections.pop(next(iter(self.inspections)))
            repository=str(data.get("repository",""))
            selected=data.get("catalog_model_id")
            if selected:
                known=self.catalog.entries.get(selected)
                if not known:raise ValueError("Unknown catalog model.")
                repository=selected
            target=self.catalog.inspection_target(repository)
            spec=await asyncio.to_thread(inspect_repository,target)
            known=self.catalog.entries.get(spec["id"])
            if known:
                if known["revision"]!=spec["revision"]:raise ValueError("The server did not return the pinned catalog revision.")
                spec["name"]=known["name"]
            ident=str(uuid.uuid4())
            self.inspections[ident]=spec
            return JSONResponse({"inspection_id":ident,**spec})
        except ValueError as exc:
            return JSONResponse({"error":{"message":str(exc)}},400)
        except OSError:
            return JSONResponse({"error":{"message":"Public model metadata could not be downloaded. Check the network connection."}},400)

    async def start(self,request):
        data=await request.json()
        spec=self.inspections.get(data.get("inspection_id"))
        if not spec:return JSONResponse({"error":{"message":"Inspect the repository again before downloading."}},400)
        if any(j["state"] in ("queued","downloading") for j in self.jobs.values()):
            return JSONResponse({"error":{"message":"A download is already running."}},409)
        try:self.catalog.can_install(spec)
        except ValueError as exc:return JSONResponse({"error":{"message":str(exc)}},409)
        if len(self.jobs)>=16:self.jobs.pop(next(iter(self.jobs)))
        ident=str(uuid.uuid4())
        job={"id":ident,"model":spec["id"],"revision":spec["revision"],"state":"queued",
             "downloaded_bytes":0,"total_bytes":spec["download_bytes"],"cancel":threading.Event()}
        self.jobs[ident]=job
        task=asyncio.create_task(asyncio.to_thread(self.run,job,spec))
        self.tasks.add(task);task.add_done_callback(self.tasks.discard)
        return JSONResponse(self.public(job),202)

    @staticmethod
    def public(job):return {k:v for k,v in job.items() if k!="cancel"}

    def run(self,job,spec):
        root=self.catalog.managed_root
        stage=(root/".staging"/job["id"]).resolve()
        backup=(root/".replaced"/job["id"]).resolve()
        final=(root/(hashlib.sha256(spec["id"].encode()).hexdigest()[:16]+"-"+spec["revision"])).resolve()
        if not all(p.is_relative_to(root) for p in (stage,backup,final)):
            job.update(state="failed",message="Invalid installation path.");return
        published=False
        backed_up=False
        registered=False
        try:
            self.catalog.can_install(spec)
            job["state"]="downloading"
            def progress(done,total):job["downloaded_bytes"]=done
            download_snapshot(spec,stage,job["cancel"],progress)
            if job["cancel"].is_set():raise InterruptedError()
            validate_installation(stage,spec["files"])
            def publish(previous):
                nonlocal published,backed_up
                if job["cancel"].is_set():raise InterruptedError()
                if final.exists():
                    if not previous or Path(previous["path"]).resolve()!=final:
                        raise ValueError("An unregistered directory already occupies this revision.")
                    backup.parent.mkdir(parents=True,exist_ok=True)
                    os.replace(final,backup);backed_up=True
                root.mkdir(parents=True,exist_ok=True)
                os.replace(stage,final);published=True
            with self.catalog.lock:
                try:
                    self.catalog.install({**spec,"path":str(final)},publish)
                    registered=True
                finally:
                    if not registered:
                        if published and final.exists():shutil.rmtree(final)
                        if backed_up and backup.exists():os.replace(backup,final)
            job["state"]="completed"
        except InterruptedError:job.update(state="cancelled",message="Download cancelled.")
        except Exception:job.update(state="failed",message="Installation failed. Existing models were preserved.")
        finally:
            # Every recursive target was resolved and verified inside managed root.
            try:
                if stage.exists():shutil.rmtree(stage)
                if registered and backup.exists():shutil.rmtree(backup)
            except OSError:
                job.update(state="completed" if registered else "failed",message="Installation cleanup needs attention; a recovery directory remains in .replaced.")
            if not registered and backed_up and backup.exists():
                job.update(state="failed",recovery_required=True,message="Repair rollback needs attention. The previous directory is preserved in .replaced.")

    async def status(self,request):
        job=self.jobs.get(request.path_params["job"])
        if not job:return JSONResponse({"error":{"message":"Unknown download."}},404)
        return JSONResponse(self.public(job))

    async def cancel(self,request):
        job=self.jobs.get(request.path_params["job"])
        if not job:return JSONResponse({"error":{"message":"Unknown download."}},404)
        if job["state"] in ("queued","downloading"):job["cancel"].set()
        return JSONResponse(self.public(job))

    def routes(self):
        return [Route("/v1/model-imports/inspect",self.inspect,methods=["POST"]),
                Route("/v1/model-imports",self.start,methods=["POST"]),
                Route("/v1/model-imports/{job}",self.status),
                Route("/v1/model-imports/{job}/cancel",self.cancel,methods=["POST"])]
