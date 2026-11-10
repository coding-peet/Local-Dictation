"""Public original-Whisper import. No repository code or credentials are executed."""
import json
from pathlib import Path, PurePosixPath
import re
import threading
from urllib.error import HTTPError
from urllib.parse import quote, urlsplit, unquote
from urllib.request import Request, urlopen
from .installation import required_names, languages_from_metadata

REPO = re.compile(r"[A-Za-z0-9][A-Za-z0-9_.-]*/[A-Za-z0-9][A-Za-z0-9_.-]*\Z")
FILES = {"config.json","generation_config.json","preprocessor_config.json","tokenizer.json",
         "tokenizer_config.json","special_tokens_map.json","vocab.json","merges.txt",
         "normalizer.json","added_tokens.json","model.safetensors","model.safetensors.index.json"}

def parse_repository(value):
    value=value.strip()
    revision="main"
    if "://" in value:
        url=urlsplit(value)
        if url.scheme!="https" or url.netloc!="huggingface.co" or url.query or url.fragment:
            raise ValueError("Enter a public huggingface.co model URL or owner/repository.")
        parts=url.path.strip("/").split("/")
        if len(parts)>2:
            if len(parts)!=4 or parts[2]!="tree":
                raise ValueError("Use the repository URL or its /tree/revision URL.")
            revision=unquote(parts[3])
        value="/".join(parts[:2])
    if not REPO.fullmatch(value) or ".." in value or not re.fullmatch(r"[A-Za-z0-9_.-]+",revision):
        raise ValueError("Invalid Hugging Face repository or revision.")
    return value,revision

def get_json(url):
    try:
        with urlopen(Request(url,headers={"User-Agent":"Local-Dictation/0.1"}),timeout=30) as response:
            data=response.read(4*1024*1024+1)
        if len(data)>4*1024*1024: raise ValueError("Model metadata is too large.")
        return json.loads(data)
    except HTTPError as exc:
        if exc.code in (401,403):
            raise ValueError("Private or gated repositories are not supported by this public importer.") from None
        raise ValueError("The public model metadata could not be downloaded.") from None

def inspect_repository(value, fetch=get_json):
    repo,revision=parse_repository(value)
    info=fetch(f"https://huggingface.co/api/models/{repo}/revision/{quote(revision,safe='')}?blobs=true")
    sha=info.get("sha","")
    if not re.fullmatch("[0-9a-f]{40}",sha): raise ValueError("The repository did not resolve to a pinned commit.")
    base=f"https://huggingface.co/{repo}/resolve/{sha}/"
    config=fetch(base+"config.json")
    if config.get("model_type")!="whisper" or config.get("auto_map") or config.get("architectures")!=["WhisperForConditionalGeneration"]:
        raise ValueError("Only built-in Whisper Transformers models without remote code are supported.")
    processor=fetch(base+"preprocessor_config.json")
    if processor.get("sampling_rate")!=16000 or processor.get("auto_map"):
        raise ValueError("Expected a built-in Whisper 16 kHz audio processor.")
    generation=fetch(base+"generation_config.json")
    languages=languages_from_metadata(config,generation)
    tokenizer=fetch(base+"tokenizer_config.json")
    if tokenizer.get("auto_map") or tokenizer.get("tokenizer_class") not in (None,"WhisperTokenizer","WhisperTokenizerFast"):
        raise ValueError("Unsupported remote-code tokenizer.")
    siblings={s["rfilename"]:s for s in info.get("siblings",[]) if "rfilename" in s}
    required_names(siblings)
    weights=[]
    if "model.safetensors" in siblings:
        weights=["model.safetensors"]
    elif "model.safetensors.index.json" in siblings:
        index=fetch(base+"model.safetensors.index.json")
        weights=sorted(set(index.get("weight_map",{}).values()))
        if not weights or any(not re.fullmatch(r"model-\d+-of-\d+\.safetensors",w) for w in weights):
            raise ValueError("Invalid safetensors shard index.")
        weights.append("model.safetensors.index.json")
    else:
        raise ValueError("This model has no supported original safetensors weights. No conversion is performed.")
    names=sorted(set(weights)|{name for name in siblings if name in FILES and not name.startswith("model.")})
    files=[]
    for name in names:
        item=siblings.get(name)
        if not item: raise ValueError("A referenced model file is missing.")
        size=item.get("size") or item.get("lfs",{}).get("size")
        if not isinstance(size,int) or size<=0: raise ValueError("A model file has no verifiable download size.")
        files.append({"name":name,"size":size})
    size=sum(f["size"] for f in files)
    if size>20*1024**3: raise ValueError("This original model exceeds the 20 GiB import limit.")
    return {"id":repo,"revision":sha,"files":files,"download_bytes":size,
            "compatible":True,"runtime_tested":False,"quality_verified":False,"live_ready":False,
            "name":repo,"languages":languages}

def download_snapshot(spec,stage,cancelled,progress,opener=urlopen):
    stage=Path(stage)
    stage.mkdir(parents=True,exist_ok=True)
    total=0
    for item in spec["files"]:
        if cancelled.is_set(): raise InterruptedError("Download cancelled.")
        name=item["name"]
        if PurePosixPath(name).name!=name or name in (".",".."):
            raise ValueError("Unsafe model filename.")
        target=stage/name
        url=f"https://huggingface.co/{spec['id']}/resolve/{spec['revision']}/{quote(name)}"
        with opener(Request(url,headers={"User-Agent":"Local-Dictation/0.1"}),timeout=30) as source, target.open("wb") as output:
            count=0
            while True:
                if cancelled.is_set(): raise InterruptedError("Download cancelled.")
                block=source.read(256*1024)
                if not block: break
                count+=len(block)
                if count>item["size"]: raise ValueError("Download exceeded inspected file size.")
                output.write(block)
                total+=len(block)
                progress(total,spec["download_bytes"])
        if count!=item["size"]: raise ValueError("Incomplete model download.")
    (stage/"local-manifest.json").write_text(json.dumps(
        {"repo":spec["id"],"revision":spec["revision"],"original_weights":True},indent=2),encoding="utf-8")
