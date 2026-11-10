"""Validate a complete original Whisper installation without loading model code."""
import json
from pathlib import Path
import re
import struct

REQUIRED={"config.json","preprocessor_config.json","generation_config.json","tokenizer_config.json"}
def read_json(path):
    if path.stat().st_size>4*1024*1024:raise ValueError("Model metadata exceeds 4 MiB.")
    value=json.loads(path.read_text(encoding="utf-8"))
    def remote(obj):
        if isinstance(obj,dict):return bool(obj.get("auto_map")) or any(remote(v) for v in obj.values())
        if isinstance(obj,list):return any(remote(v) for v in obj)
        return False
    if remote(value):raise ValueError("Remote-code model metadata is not supported.")
    return value

def required_names(names):
    names=set(names)
    if not REQUIRED<=names:raise ValueError("Missing Whisper model/processor/tokenizer configuration.")
    if "tokenizer.json" not in names and not {"vocab.json","merges.txt"}<=names:
        raise ValueError("Missing Whisper tokenizer assets.")
    if "model.safetensors" not in names and "model.safetensors.index.json" not in names:
        raise ValueError("Missing original safetensors weights.")

def languages_from_metadata(config,generation):
    if generation.get("is_multilingual") is False or config.get("vocab_size")==51864:
        return ["en"]
    mapping=generation.get("lang_to_id",{})
    languages=[lang for lang in ("sk","en") if f"<|{lang}|>" in mapping]
    if not languages:raise ValueError("Whisper generation metadata does not identify supported language tokens.")
    return languages+["auto"]

def supports_language(model,language):
    languages=model.get("languages",[])
    explicit=[l for l in languages if l!="auto"]
    if language=="auto" and len(explicit)==1:return explicit[0]
    if language not in languages:raise ValueError("This model does not support the selected language.")
    return language

def validate_installation(path,files=None):
    path=Path(path)
    names={p.name for p in path.iterdir() if p.is_file()}
    required_names(names)
    config=read_json(path/"config.json")
    generation=read_json(path/"generation_config.json")
    processor=read_json(path/"preprocessor_config.json")
    tokenizer=read_json(path/"tokenizer_config.json")
    if config.get("model_type")!="whisper" or config.get("architectures")!=["WhisperForConditionalGeneration"]:
        raise ValueError("Expected built-in Whisper architecture.")
    if processor.get("sampling_rate")!=16000:raise ValueError("Expected 16 kHz Whisper processor.")
    if tokenizer.get("tokenizer_class") not in (None,"WhisperTokenizer","WhisperTokenizerFast"):
        raise ValueError("Unsupported tokenizer class.")
    for name in names:
        if name.endswith(".json") and name!="local-manifest.json":read_json(path/name)
    weights=["model.safetensors"] if "model.safetensors" in names else sorted(set(
        read_json(path/"model.safetensors.index.json").get("weight_map",{}).values()))
    if not weights:raise ValueError("No safetensors shards.")
    for name in weights:
        if not re.fullmatch(r"model(?:-\d+-of-\d+)?\.safetensors",name):
            raise ValueError("Unsafe safetensors shard path.")
        file=path/name
        size=file.stat().st_size
        with file.open("rb") as stream:
            raw=stream.read(8)
            if len(raw)!=8:raise ValueError("Truncated safetensors header.")
            length=struct.unpack("<Q",raw)[0]
            if not 2<=length<=16*1024*1024 or 8+length>size:raise ValueError("Invalid safetensors header.")
            header=json.loads(stream.read(length))
        offsets=[v["data_offsets"] for k,v in header.items() if k!="__metadata__"]
        if not offsets or any(not isinstance(a,int) or not isinstance(b,int) or a<0 or b<a for a,b in offsets):
            raise ValueError("Invalid tensor offsets.")
        if max(b for a,b in offsets)+8+length!=size:
            raise ValueError("Incomplete safetensors weight file.")
    for file in files or []:
        target=path/file["name"]
        if Path(file["name"]).name!=file["name"] or target.stat().st_size!=file["size"]:
            raise ValueError("Installed file is missing or differs from its pinned size.")
    return languages_from_metadata(config,generation)
