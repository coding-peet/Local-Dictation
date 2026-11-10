"""A coroutine never abandons a running model thread before cleanup."""
import asyncio

async def join(task):
    while not task.done():
        try:await asyncio.shield(task)
        except asyncio.CancelledError:continue
        except Exception:break
    return task.result()

async def run_worker(function,*args,cancelled,timeout=None):
    work=asyncio.create_task(asyncio.to_thread(function,*args))
    try:
        if timeout is None:return await asyncio.shield(work)
        return await asyncio.wait_for(asyncio.shield(work),timeout)
    except asyncio.CancelledError:
        cancelled.set()
        try:await join(work)
        except Exception:pass
        raise
    except TimeoutError:
        cancelled.set()
        try:await join(work)
        except Exception:pass
        raise RuntimeError("The model exceeded its processing deadline. Received text is preserved.") from None
