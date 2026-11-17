"""Backend-owned registry. Capability and quality evidence are separate."""
import json
import os
import shutil
import stat
import uuid
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


def canonical_model_id(model):
    aliases = {"kinit-turbo": BUILTINS[0]["id"], "whisper-large": BUILTINS[1]["id"],
               "whisper-large-turbo": BUILTINS[2]["id"]}
    return aliases.get(model, model)

class Catalog:
    def __init__(self,config):
        self.config=config
        self.file=config.data_root/"models.json"
        self.lock=threading.RLock()
        self.entries={}
        self.validations={}
        for model in BUILTINS:
            self.entries[model["id"]]=self.builtin_entry(model)
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

    def builtin_entry(self,model):
        entry={**model,"path":str(self.config.model_root/model["folder"]),"installed":False,
               "compatible":True,"runtime_tested":False,"quality_verified":False}
        self.check(entry)
        pinned=entry.get("installed_revision")==model["revision"]
        # Only the original prevalidated profile retains its existing evidence.
        prevalidated=model.get("prevalidated",True)
        entry.update(runtime_tested=entry["installed"] and pinned and prevalidated,
                     quality_verified=entry["installed"] and pinned and prevalidated)
        return entry

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
            validation=self.validations.get(entry["id"])
            if validation is None or validation[0]!=signature:
                languages=validate_installation(path,entry.get("files"))
                # A Slovak fine-tune retains the multilingual tokenizer, but
                # that does not make its other language tokens supported.
                entry["languages"]=["sk"] if entry["id"]==BUILTINS[0]["id"] else languages
                entry["installed_revision"]=None
                manifest=path/"local-manifest.json"
                if entry.get("managed") and not manifest.exists():
                    raise ValueError("Managed model has no pinned installation manifest.")
                if manifest.exists():
                    pinned=json.loads(manifest.read_text(encoding="utf-8"))
                    if pinned.get("repo")!=entry["id"] or pinned.get("revision")!=entry.get("revision"):
                        raise ValueError("Installed manifest differs from the pinned model.")
                    entry["installed_revision"]=pinned["revision"]
                validation=(signature,tuple(entry["languages"]),entry["installed_revision"])
                self.validations[entry["id"]]=validation
            # Registry restoration may construct a different entry object for
            # this same validated path. Reapply metadata even on a cache hit.
            entry["languages"]=list(validation[1])
            entry["installed_revision"]=validation[2]
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
            models=[]
            for entry in self.entries.values():
                restriction=self.removal_restriction(entry)
                models.append({"object":"model","owned_by":"local",**entry,
                    "can_install":not entry["installed"],"can_remove":restriction is None,
                    "removal_restriction":restriction,
                    "verification_eligible":entry["installed"] and entry["runtime_tested"]})
            return models

    @staticmethod
    def plain_path(path):
        """Reject Windows junctions/reparse points as well as ordinary symlinks."""
        for candidate in (path,*path.parents):
            info=os.lstat(candidate)
            if stat.S_ISLNK(info.st_mode) or getattr(info,"st_file_attributes",0)&0x400:
                raise ValueError("Linked or reparse-point model paths cannot be removed.")

    @staticmethod
    def plain_tree(path):
        def failed(exc):raise exc
        for current,directories,files in os.walk(path,followlinks=False,onerror=failed):
            for name in (*directories,*files):
                info=os.lstat(Path(current)/name)
                if stat.S_ISLNK(info.st_mode) or getattr(info,"st_file_attributes",0)&0x400:
                    raise ValueError("Linked or reparse-point model files cannot be removed.")
                if stat.S_ISREG(info.st_mode) and info.st_nlink>1:
                    raise ValueError("Hard-linked model files may be shared and cannot be removed.")

    def removal_target(self,entry):
        if not entry.get("installed"):
            raise ValueError("This model is not installed. Repair an incomplete installation before removing it.")
        if not entry.get("managed"):
            raise ValueError("Shared model files are preserved. Only models downloaded by Local Dictation can be removed.")
        raw=Path(entry["path"])
        if not raw.is_absolute() or ".." in raw.parts:
            raise ValueError("The model path is unsafe for removal.")
        raw_root=(self.config.data_root.parent/"models").absolute()
        root=raw_root.resolve()
        path=raw.resolve()
        if path.parent!=root or path.name.startswith(".") or not path.is_dir():
            raise ValueError("Only an owned model directory directly inside managed storage can be removed.")
        self.plain_path(raw_root)
        self.plain_path(raw)
        for other in self.entries.values():
            if other["id"]==entry["id"]:continue
            other_path=Path(other["path"]).resolve()
            if other_path==path or other_path.is_relative_to(path) or path.is_relative_to(other_path):
                raise ValueError("Another registered model shares this directory. Its files are preserved.")
        self.plain_tree(path)
        manifest=json.loads((path/"local-manifest.json").read_text(encoding="utf-8"))
        if manifest.get("repo")!=entry["id"] or manifest.get("revision")!=entry.get("revision"):
            raise ValueError("The model installation ownership manifest does not match the registry.")
        return path

    def removal_restriction(self,entry):
        try:self.removal_target(entry)
        except ValueError as exc:return str(exc)
        except (OSError,KeyError,TypeError):return "The model installation cannot be safely removed. Check its managed files."
        return None

    def remove(self,model,before_remove=None):
        """Quarantine an explicitly selected owned installation, then commit the registry."""
        with self.lock:
            ident=canonical_model_id(model)
            previous=self.entries.get(ident)
            if previous is None:raise ValueError("Unknown model. Select an installed model from Models.")
            self.check(previous)
            source=self.removal_target(previous)
            root=self.managed_root
            quarantine_root=root/".removing"
            quarantine_root.mkdir(exist_ok=True)
            self.plain_path(quarantine_root)
            if quarantine_root.resolve().parent!=root:
                raise ValueError("The model recovery directory is unsafe.")
            quarantine=quarantine_root/str(uuid.uuid4())
            if quarantine.exists():raise ValueError("The model recovery directory is already occupied.")
            if before_remove:before_remove(ident)
            # Revalidate immediately before the move, including links and shared ownership.
            source=self.removal_target(previous)
            os.replace(source,quarantine)
            self.validations.pop(ident,None)
            builtin=next((item for item in BUILTINS if item["id"]==ident),None)
            try:
                if builtin:self.entries[ident]=self.builtin_entry(builtin)
                else:self.entries.pop(ident)
                self.save() # Atomic os.replace: registry points at either the old or new state.
            except Exception:
                self.entries[ident]=previous
                self.validations.pop(ident,None)
                # Rollback never overwrites a newly occupied source directory.
                if source.exists():
                    raise RuntimeError("Removal rollback needs attention. The previous model is preserved in the managed recovery directory.") from None
                self.plain_path(quarantine)
                os.replace(quarantine,source)
                raise
            try:
                self.plain_path(quarantine)
                if quarantine.resolve().parent!=quarantine_root.resolve():
                    raise ValueError("The model recovery path changed.")
                self.plain_tree(quarantine)
                shutil.rmtree(quarantine)
                cleanup_pending=False
            except (OSError,ValueError):cleanup_pending=True
            message="Model removed."
            if builtin and self.entries[ident]["installed"]:
                message="Managed installation removed. A shared model copy remains available and has been preserved."
            if cleanup_pending:
                message+=" Some files remain in the managed recovery directory; cleanup needs attention."
            return {"model_id":ident,"removed":True,"cleanup_pending":cleanup_pending,"message":message}

    def require(self,model):
        with self.lock:
            entry=self.entries.get(canonical_model_id(model))
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
