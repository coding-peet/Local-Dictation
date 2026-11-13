"""Keep health responsive while the original local runtime prepares."""
import logging
import threading

class PreparingEngine:
    def __init__(self,loader):
        self.loader=loader
        self.adapter=None
        self.catalog=None
        self.failed=False
        self.thread=threading.Thread(target=self.prepare,name="ASR initialization",daemon=True)
    def start(self):self.thread.start()
    def prepare(self):
        try:
            adapter=self.loader()
            adapter.catalog=self.catalog
            self.adapter=adapter
        except Exception:
            self.failed=True
            logging.getLogger("local-asr").exception("Candidate runtime initialization failed.")
    def status(self):
        if self.adapter is None:return {"ready":False,"preparing":not self.failed,"initialization_failed":self.failed}
        return self.adapter.status()
    def require(self):
        if self.adapter is None:raise RuntimeError("The local runtime is still preparing or failed to initialize.")
        return self.adapter
    def transcribe_pcm(self,*args):return self.require().transcribe_pcm(*args)
    def transcribe(self,*args):return self.require().transcribe(*args)
    def unload(self):
        if self.adapter is not None:self.adapter.unload()
    def forget_model(self,model):
        self.require().forget_model(model)
