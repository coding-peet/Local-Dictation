"""Explicit public original-weight import lifecycle check against the candidate API."""
import argparse
import json
import time
from urllib.request import Request,urlopen

def request(base,path,data=None):
    body=None if data is None else json.dumps(data).encode()
    with urlopen(Request(base+path,data=body,headers={"Content-Type":"application/json"}),timeout=60) as response:
        return json.load(response)

def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("--repository",default="openai/whisper-tiny")
    p.add_argument("--base",default="http://127.0.0.1:48125")
    a=p.parse_args()
    spec=request(a.base,"/v1/model-imports/inspect",{"repository":a.repository})
    print(json.dumps({k:v for k,v in spec.items() if k!="files"}),flush=True)
    job=request(a.base,"/v1/model-imports",{"inspection_id":spec["inspection_id"]})
    while True:
        state=request(a.base,"/v1/model-imports/"+job["id"])
        print(json.dumps(state),flush=True)
        if state["state"] in ("completed","cancelled","failed"):break
        time.sleep(2)
    if state["state"]!="completed":raise SystemExit(1)

if __name__=="__main__":main()
