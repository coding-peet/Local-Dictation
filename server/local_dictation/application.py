"""Loopback-only ASGI transport. Inference never runs on the event loop."""
import asyncio
from email import policy
from email.parser import BytesParser
import json
import os
import threading
from urllib.parse import urlsplit
from starlette.applications import Starlette
from starlette.requests import Request
from starlette.responses import JSONResponse, PlainTextResponse
from starlette.routing import Route, WebSocketRoute
from starlette.websockets import WebSocketDisconnect
from .config import RuntimeConfig
from .sessions import AudioSession, Admission, BusyError

def error(message, status=400):
    return JSONResponse({"error":{"message":message,"type":"invalid_request_error"}},status_code=status)

class LoopbackGuard:
    def __init__(self, app):
        self.app = app
    async def __call__(self, scope, receive, send):
        headers = {k.lower():v for k,v in scope.get("headers",[])}
        host = headers.get(b"host",b"").decode("ascii",errors="replace")
        allowed = urlsplit("//"+host).hostname in ("127.0.0.1","localhost")
        if scope["type"] in ("http","websocket") and (not allowed or b"origin" in headers):
            if scope["type"] == "websocket":
                await send({"type":"websocket.close","code":1008})
            else:
                await PlainTextResponse("Loopback native clients only.",403)(scope,receive,send)
            return
        await self.app(scope,receive,send)

def create_app(config, engine, catalog=None):
    if catalog is None:
        from .catalog import Catalog
        catalog = Catalog(config)
    admission = Admission()
    admission.unavailable = False

    async def health(request):
        runtime=engine.status() if hasattr(engine,"status") else {}
        return JSONResponse({"service":"local-dictation","protocol_version":1,"process_id":os.getpid(),
                             "ready":not admission.unavailable,"busy":admission.lock.locked(),
                             "phase":admission.kind,"max_seconds":config.max_seconds,"runtime":runtime})

    async def models(request):
        return JSONResponse({"object":"list","data":catalog.models()})

    def validate(options):
        if options.get("type") != "start" or options.get("protocol_version") != 1:
            raise ValueError("Start with protocol_version 1.")
        model = catalog.require(options.get("model",""))
        verifier = options.get("verification_model") or None
        live = options.get("live",True)
        if not isinstance(live,bool): raise ValueError("live must be a boolean.")
        if live and not model.get("live_supported"):
            raise ValueError("This model is available after recording; live capability has not been verified.")
        if verifier:
            verified = catalog.require(verifier)
            if not verified["runtime_tested"]:
                raise ValueError("Test this verification model with an audio file before using it.")
            if verifier == model["id"]: raise ValueError("Choose a different verification model.")
        language=options.get("language","sk")
        if language not in ("sk","en","auto"): raise ValueError("Select sk, en or auto.")
        if model.get("languages") == ["sk"] and language == "en":
            raise ValueError("This model supports Slovak. Choose a multilingual model for English.")
        return {**options,"model":model["id"],"verification_model":verifier,
                "live":live,"language":language}

    async def socket(ws):
        await ws.accept()
        session, runner = None, None
        owned = False
        try:
            raw = await asyncio.wait_for(ws.receive_text(),timeout=10)
            if len(raw)>8192: raise ValueError("Start message is too large.")
            options=validate(json.loads(raw))
            if admission.unavailable: raise BusyError("API cleanup failed. Restart the candidate server.")
            session=AudioSession(config,engine,options,ws.send_json,admission)
            await admission.acquire("primary",session.cancelled)
            owned=True
            runner=asyncio.create_task(session.run())
            while not runner.done():
                receive=asyncio.create_task(ws.receive())
                done,_=await asyncio.wait((receive,runner),return_when=asyncio.FIRST_COMPLETED)
                if runner in done:
                    receive.cancel()
                    await asyncio.gather(receive,return_exceptions=True)
                    break
                message=receive.result()
                if message["type"] == "websocket.disconnect":
                    session.cancel()
                    break
                if message.get("bytes") is not None:
                    session.feed(message["bytes"])
                elif message.get("text") is not None:
                    if len(message["text"])>1024: raise ValueError("Control message too large.")
                    control=json.loads(message["text"])
                    if control.get("type") == "finish": session.finish()
                    elif control.get("type") == "cancel": session.cancel()
                    else: raise ValueError("Unknown control message.")
            if runner:
                await runner
        except (WebSocketDisconnect, asyncio.CancelledError):
            if session: session.cancel()
        except (ValueError, BusyError, TimeoutError) as exc:
            if session: session.cancel()
            try: await ws.send_json({"type":"session.error","message":str(exc)})
            except Exception: pass
        except Exception:
            if session: session.cancel()
            try: await ws.send_json({"type":"session.error","message":"Local transcription failed. Your received text is preserved."})
            except Exception: pass
        finally:
            if runner:
                # Cancellation is cooperative. Join before releasing model ownership.
                await asyncio.gather(runner,return_exceptions=True)
            if owned: admission.release()
            try: await ws.close()
            except Exception: pass

    async def batch(request):
        cancelled=threading.Event()
        try:
            if admission.unavailable: raise BusyError("API unavailable after cleanup failure.")
            async with admission.own("primary",cancelled):
                tested_model=None
                try:
                    length=int(request.headers.get("content-length","0"))
                    if request.headers.get("transfer-encoding") or not 0<length<=26*1024*1024:
                        raise ValueError("Send Content-Length and at most 26 MiB.")
                    content_type=request.headers.get("content-type","")
                    if not content_type.lower().startswith("multipart/form-data;"):
                        raise ValueError("Expected multipart/form-data.")
                    body=bytearray()
                    async for part in request.stream():
                        body.extend(part)
                        if len(body)>length: raise ValueError("Upload exceeded Content-Length.")
                    if len(body)!=length: raise ValueError("Incomplete upload.")
                    msg=BytesParser(policy=policy.default).parsebytes(
                        ("Content-Type: "+content_type+"\r\nMIME-Version: 1.0\r\n\r\n").encode("ascii")+body)
                    if not msg.is_multipart() or msg.defects: raise ValueError("Invalid multipart upload.")
                    fields,files={},[]
                    for part in msg.iter_parts():
                        name=part.get_param("name",header="content-disposition")
                        value=part.get_payload(decode=True)
                        if value is None: raise ValueError("Invalid form data.")
                        if name=="file": files.append(value)
                        elif name in ("model","language","prompt","response_format"):
                            if name in fields or len(value)>16384: raise ValueError("Duplicate or oversized field.")
                            fields[name]=value.decode("utf-8")
                    if len(files)!=1 or not files[0]: raise ValueError("Exactly one audio file is required.")
                    model=catalog.require(fields.get("model",""))
                    fmt=fields.get("response_format","json")
                    if fmt not in ("json","text","verbose_json"): raise ValueError("Unsupported response format.")
                    work=asyncio.create_task(asyncio.to_thread(engine.transcribe,files[0],fields.get("prompt",""),
                                          model["id"],fields.get("language","auto"),cancelled))
                    try:
                        result=await asyncio.shield(work)
                    except asyncio.CancelledError:
                        cancelled.set()
                        await asyncio.gather(work,return_exceptions=True)
                        raise
                    if request.url.path.endswith("/test"):
                        tested_model=model["id"]
                    if fmt=="text": return PlainTextResponse(result["text"])
                    return JSONResponse({"text":result["text"]} if fmt=="json" else
                                        {"task":"transcribe",**result,"segments":[]})
                finally:
                    try: await asyncio.to_thread(engine.unload)
                    except Exception:
                        admission.unavailable=True
                        raise RuntimeError("Model cleanup failed.") from None
                    if tested_model is not None:
                        catalog.mark_tested(tested_model)
        except BusyError as exc: return error(str(exc),409)
        except ValueError as exc: return error(str(exc))
        except Exception: return error("Transcription failed. Check candidate API availability.",500)

    routes=[Route("/health",health),Route("/v1/models",models),
            Route("/v1/audio/transcriptions",batch,methods=["POST"]),
            Route("/v1/models/test",batch,methods=["POST"]),
            WebSocketRoute("/v1/audio/sessions",socket)]
    from .imports import Imports
    imports=Imports(config,catalog)
    routes.extend(imports.routes())
    if hasattr(engine,"catalog"):
        engine.catalog=catalog
    app=Starlette(routes=routes)
    app.state.catalog,app.state.admission=catalog,admission
    app.state.validate=validate
    app.state.engine=engine
    return LoopbackGuard(app)
