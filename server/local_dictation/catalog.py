"""Backend-owned registry. Capability and quality evidence are separate."""
import json
import os
from pathlib import Path
import threading
from .installation import validate_installation
from .downloads import parse_repository

BUILTINS = [
    {"id":"kinit/whisper-large-v3-turbo-sk","name":"KInIT Turbo · Slovak","folder":"kinit-turbo",
     "revision":"eb2c35bf0971f836fbf045ebe6e897826172d5d7","languages":["sk"],
     "live_supported":True,"live_ready":False},
    {"id":"openai/whisper-large-v3","name":"Whisper Large-v3 · Multilingual","folder":"whisper-large",
     "revision":"06f233fe06e710322aca913c1bc4249a0d71fce1","languages":["sk","en","auto"],
     # The original-weight session path supports live updates; measured latency
     # and quality remain separate from capability (see live-visibility.md).
     "live_supported":True,"live_ready":False},
    {"id":"openai/whisper-large-v3-turbo","name":"Whisper Large-v3 Turbo · Multilingual",
     "folder":"whisper-large-turbo",
     "revision":"41f01f3fe87f28c78e2fbf8b568835947dd65ed9","languages":["sk","en","auto"],
     "live_supported":True,"live_ready":False,"prevalidated":False}
]

class Catalog:
    def __init__(self,config):
        self.config=config
        self.file=config.data_root/"models.json"
        self.lock=threading.RLock()
        self.entries={}
        self.validations={}
        for model in BUILTINS:
            entry={**model,"path":str(config.model_root/model["folder"]),"installed":False,
                   "compatible":True,"runtime_tested":False,"quality_verified":False}
            self.check(entry)
            pinned=entry.get("installed_revision")==model["revision"]
            # A new profile's pinned files are not runtime or quality evidence.
            prevalidated=model.get("prevalidated",True)
            entry.update(runtime_tested=entry["installed"] and pinned and prevalidated,
                         quality_verified=entry["installed"] and pinned and prevalidated)
            self.entries[model["id"]]=entry
        if self.file.exists():
            for model in json.loads(self.file.read_text(encoding="utf-8")):
                builtin=next((b for b in BUILTINS if b["id"]==model["id"]),None)
                if builtin:
                    if not model.get("managed") or model.get("revision")!=builtin["revision"]:
                        continue
                    path=Path(model["path"]).resolve()
                    if not path.is_relative_to(self.managed_root):continue
                    model={**model,**builtin,"path":str(path)}
                self.check(model)
                self.entries[model["id"]]=model

    @property
    def managed_root(self):return (self.config.data_root.parent/"models").resolve()

    def save(self):
        self.file.parent.mkdir(parents=True,exist_ok=True)
        temporary=self.file.with_suffix(".json.tmp")
        builtin_ids={b["id"] for b in BUILTINS}
        saved=[m for m in self.entries.values() if m["id"] not in builtin_ids or m.get("managed")]
        temporary.write_text(json.dumps(saved,ensure_ascii=False,indent=2),encoding="utf-8")
        os.replace(temporary,self.file)

    def check(self,entry):
        try:
            path=Path(entry["path"])
            signature=(str(path.resolve()),tuple(sorted(
                (p.name,p.stat().st_size,p.stat().st_mtime_ns) for p in path.iterdir() if p.is_file())))
            if self.validations.get(entry["id"])!=signature:
                validate_installation(path,entry.get("files"))
                entry["installed_revision"]=None
                manifest=path/"local-manifest.json"
                if entry.get("managed") and not manifest.exists():
                    raise ValueError("Managed model has no pinned installation manifest.")
                if manifest.exists():
                    pinned=json.loads(manifest.read_text(encoding="utf-8"))
                    if pinned.get("repo")!=entry["id"] or pinned.get("revision")!=entry.get("revision"):
                        raise ValueError("Installed manifest differs from the pinned model.")
                    entry["installed_revision"]=pinned["revision"]
                self.validations[entry["id"]]=signature
            entry["installed"]=True
        except (OSError,ValueError,KeyError,TypeError):
            entry["installed"]=False
            entry["installed_revision"]=None
            entry["runtime_tested"]=False
            entry["quality_verified"]=False
            entry["live_ready"]=False
            self.validations.pop(entry["id"],None)

    def models(self):
        with self.lock:
            for entry in self.entries.values():self.check(entry)
            return [{"object":"model","owned_by":"local",**m,"can_install":not m["installed"],
                     "verification_eligible":m["installed"] and m["runtime_tested"]}
                    for m in self.entries.values()]

    def require(self,model):
        aliases={"kinit-turbo":BUILTINS[0]["id"],"whisper-large":BUILTINS[1]["id"],
                 "whisper-large-turbo":BUILTINS[2]["id"]}
        with self.lock:
            entry=self.entries.get(aliases.get(model,model))
            if not entry:raise ValueError("Unknown model. Select an installed model from Models.")
            self.check(entry)
            if not entry["installed"]:raise ValueError("This model installation is missing or incomplete.")
            return dict(entry)

    def inspection_target(self,value):
        repo,revision=parse_repository(value)
        with self.lock:
            known=self.entries.get(repo)
            if known:
                if revision not in ("main",known["revision"]):
                    raise ValueError("This registered model keeps its pinned revision. Updates require a separate explicit workflow.")
                revision=known["revision"]
        return f"https://huggingface.co/{repo}/tree/{revision}"

    def can_install(self,spec):
        with self.lock:
            previous=self.entries.get(spec["id"])
            if previous:
                if spec["revision"]!=previous["revision"]:
                    raise ValueError("A different registered revision cannot be replaced.")
                self.check(previous)
                if previous["installed"]:raise ValueError("This model is already installed; its revision is preserved.")
            return previous

    def install(self,spec,publish=None):
        with self.lock:
            previous=self.can_install(spec)
            builtin=next((b for b in BUILTINS if b["id"]==spec["id"]),None)
            if builtin and not Path(spec["path"]).resolve().is_relative_to(self.managed_root):
                raise ValueError("Catalog models must install into managed storage.")
            if publish:publish(previous)
            metadata={**spec,**(builtin or {}),"path":spec["path"],"managed":True,"installed":True,
                      "compatible":True,"runtime_tested":False,"quality_verified":False,
                      "live_supported":bool(builtin and builtin["live_supported"]),"live_ready":False}
            self.entries[spec["id"]]=metadata
            self.validations.pop(spec["id"],None)
            try:self.save()
            except Exception:
                if previous is None:self.entries.pop(spec["id"],None)
                else:self.entries[spec["id"]]=previous
                self.validations.pop(spec["id"],None)
                raise

    def add(self,spec):
        with self.lock:
            if spec["id"] in self.entries:raise ValueError("This model is already registered; its revision is preserved.")
            self.install(spec)

    def mark_tested(self,model):
        with self.lock:
            previous=self.entries[model]["runtime_tested"]
            self.entries[model]["runtime_tested"]=True
            try:self.save()
            except Exception:
                self.entries[model]["runtime_tested"]=previous
                raise
