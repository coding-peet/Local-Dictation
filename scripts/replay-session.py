"""Replay local audio at real-time pace; private results stay in the chosen output directory."""
import argparse
from collections import Counter
import asyncio
import json
from pathlib import Path
import sys
import time
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/"server"))
from asr import read_audio
from websockets.asyncio.client import connect
import numpy as np

async def run(args):
    audio=read_audio(args.audio)
    pcm=(np.clip(audio,-1,1)*32767).astype("<i2").tobytes()
    events=[]
    started=time.perf_counter()
    stopped=None
    first=None
    final=None
    async with connect(args.url,max_size=2*1024*1024,origin=None,proxy=None) as socket:
        await socket.send(json.dumps({"type":"start","protocol_version":1,"model":args.model,
            "language":args.language,"verification_model":args.verify,"live":not args.batch}))
        async def receive():
            nonlocal first,final
            async for message in socket:
                event=json.loads(message)
                event["received_seconds"]=time.perf_counter()-started
                events.append(event)
                kind=event["type"]
                if kind=="transcript.updated" and event.get("text") and first is None:
                    first=event["received_seconds"]
                if kind=="transcript.final": final=event["received_seconds"]
                if kind in ("session.completed","session.error","session.cancelled"):return
        receiver=asyncio.create_task(receive())
        for offset in range(0,len(pcm),3200):
            if receiver.done():break
            await socket.send(pcm[offset:offset+3200])
            target=started+(offset+3200)/32000/args.pace
            await asyncio.sleep(max(0,target-time.perf_counter()))
        stopped=time.perf_counter()-started
        if not receiver.done():await socket.send(json.dumps({"type":"finish"}))
        await asyncio.wait_for(receiver,timeout=300)
    result={"audio_seconds":len(audio)/16000,"first_draft_seconds":first,
            "stop_to_primary_seconds":None if final is None else final-stopped,
            "wall_seconds":time.perf_counter()-started,
            "events":events}
    output=Path(args.output)
    output.parent.mkdir(parents=True,exist_ok=True)
    output.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding="utf-8")
    print(json.dumps({k:v for k,v in result.items() if k!="events"}))
    print("event_counts:",dict(Counter(e["type"] for e in events)))

def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("audio");p.add_argument("--output",required=True)
    p.add_argument("--url",default="ws://127.0.0.1:48125/v1/audio/sessions")
    p.add_argument("--model",default="kinit/whisper-large-v3-turbo-sk")
    p.add_argument("--verify");p.add_argument("--language",default="sk")
    p.add_argument("--batch",action="store_true");p.add_argument("--pace",type=float,default=1)
    args=p.parse_args()
    if args.pace<=0:p.error("pace must be positive")
    asyncio.run(run(args))

if __name__=="__main__":main()
