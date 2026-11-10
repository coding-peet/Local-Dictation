"""Start the candidate duplex API without changing the deployed batch server."""
import argparse
import logging
import os
import socket

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port",type=int,default=48125)
    args=parser.parse_args()
    logging.basicConfig(level=logging.INFO,format="%(asctime)s %(levelname)s %(message)s")
    from local_dictation.config import RuntimeConfig
    config=RuntimeConfig.from_env()
    os.environ["LOCAL_DICTATION_MODEL_ROOT"]=str(config.model_root)
    os.environ["HF_HOME"]=str(config.data_root/"cache")
    # Bind before initializing another model process.
    listener=socket.socket()
    listener.bind(("127.0.0.1",args.port))
    listener.listen(128)
    from local_dictation.bootstrap import PreparingEngine
    from local_dictation.application import create_app
    def load():
        from model_engine import WhisperEngine
        from local_dictation.engine_adapter import EngineAdapter
        engine=WhisperEngine()
        engine.prepare_runtime()
        return EngineAdapter(engine)
    engine=PreparingEngine(load)
    app=create_app(config,engine)
    engine.start()
    import uvicorn
    server=uvicorn.Server(uvicorn.Config(app,host="127.0.0.1",port=args.port,
        ws_max_size=config.max_frame_bytes,ws_max_queue=16,
        timeout_keep_alive=10,access_log=False))
    server.run(sockets=[listener])

if __name__=="__main__":main()
