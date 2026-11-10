"""Backend-owned registry. Capability and quality evidence are separate."""
import json
import os
from pathlib import Path
import threading

BUILTINS = [
    {"id":"kinit/whisper-large-v3-turbo-sk","name":"KInIT Turbo · Slovak","folder":"kinit-turbo",
     "revision":"eb2c35bf0971f836fbf045ebe6e897826172d5d7","languages":["sk"],
     "live_supported":True,"live_ready":False},
    {"id":"openai/whisper-large-v3","name":"Whisper Large-v3 · Multilingual","folder":"whisper-large",
     "revision":"06f233fe06e710322aca913c1bc4249a0d71fce1","languages":["sk","en","auto"],
     "live_supported":False,"live_ready":False}
]

class Catalog:
    def __init__(self,config):
        self.config=config
        self.file=config.data_root/"models.json"
        self.lock=threading.RLock()
        self.entries={}
        for model in BUILTINS:
            path=config.model_root/model["folder"]
            installed=(path/"config.json").exists()
            pinned=False
            try:
                manifest=json.loads((path/"local-manifest.json").read_text(encoding="utf-8"))
                pinned=manifest.get("revision")==model["revision"]
            except (OSError,ValueError): pass
            self.entries[model["id"]]={**model,"path":str(path),"installed":installed,
                "compatible":True,"runtime_tested":installed and pinned,
                "quality_verified":installed and pinned}
        if self.file.exists():
            saved=json.loads(self.file.read_text(encoding="utf-8"))
            for model in saved:
                if model["id"] not in self.entries:
                    model["installed"]=Path(model["path"]).is_dir()
                    self.entries[model["id"]]=model

    def save(self):
        self.file.parent.mkdir(parents=True,exist_ok=True)
        temporary=self.file.with_suffix(".json.tmp")
        temporary.write_text(json.dumps([m for m in self.entries.values()
            if m["id"] not in {b["id"] for b in BUILTINS}],ensure_ascii=False,indent=2),encoding="utf-8")
        os.replace(temporary,self.file)

    def models(self):
        with self.lock:
            return [{"object":"model","owned_by":"local",**m,
                     "verification_eligible":m["installed"] and m["runtime_tested"]}
                    for m in self.entries.values()]

    def require(self,model):
        aliases={"kinit-turbo":BUILTINS[0]["id"],"whisper-large":BUILTINS[1]["id"]}
        with self.lock:
            entry=self.entries.get(aliases.get(model,model))
            if not entry: raise ValueError("Unknown model. Select an installed model from Models.")
            if not entry["installed"]: raise ValueError("This model is not installed.")
            return dict(entry)

    def add(self,spec):
        with self.lock:
            if spec["id"] in self.entries:
                raise ValueError("This model is already registered; its installed revision is preserved.")
            self.entries[spec["id"]]={**spec,"installed":True,"compatible":True,
                "runtime_tested":False,"quality_verified":False,"live_supported":False,"live_ready":False}
            self.save()

    def mark_tested(self,model):
        with self.lock:
            self.entries[model]["runtime_tested"]=True
            self.save()
