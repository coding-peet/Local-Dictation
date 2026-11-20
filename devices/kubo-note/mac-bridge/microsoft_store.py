"""Explicit work destinations in the colleague's own Microsoft 365 tenant."""
import datetime as dt
import html
import json
import os
from urllib.parse import quote
from zoneinfo import ZoneInfo

GRAPH = "https://graph.microsoft.com/v1.0"
SCOPES = ["Calendars.ReadWrite", "Tasks.ReadWrite", "Notes.ReadWrite", "User.Read"]


def credentials(state, interactive=False):
    import msal
    settings = json.loads((state / "microsoft.json").read_text())
    path = state / "ms-token.json"
    cache = msal.SerializableTokenCache()
    if path.exists():
        cache.deserialize(path.read_text())
    app = msal.PublicClientApplication(settings["client_id"], token_cache=cache,
                                     authority="https://login.microsoftonline.com/" + settings["tenant_id"])
    accounts = app.get_accounts()
    result = app.acquire_token_silent(SCOPES, account=accounts[0]) if accounts else None
    if not result and interactive:
        result = app.acquire_token_interactive(SCOPES)
    if not result or "access_token" not in result:
        raise RuntimeError("Pracovné prihlásenie vyžaduje bridge.py --ms-login.")
    if cache.has_state_changed:
        temp = path.with_suffix(".tmp")
        temp.write_text(cache.serialize())
        os.replace(temp, path)
    return settings, result["access_token"]


def deliver(state, config, action, key):
    import requests
    settings, token = credentials(state)
    session = requests.Session()
    session.headers["Authorization"] = "Bearer " + token

    def request(method, url, **kwargs):
        if not url.startswith(GRAPH + "/"):
            raise ValueError("Neplatná stránka Graph")
        r = session.request(method, url, timeout=25, **kwargs)
        r.raise_for_status()
        return r.json() if r.content else {}

    def contains(url, prop, title):
        while url:
            page = request("GET", url)
            if any(item.get(prop) == title for item in page["value"]):
                return True
            url = page.get("@odata.nextLink")
        return False

    def utc(value):
        d = dt.datetime.fromisoformat(value).replace(tzinfo=ZoneInfo(config["timezone"]))
        return d.astimezone(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")

    if action["kind"] == "work_event":
        body = {"subject": action["title"], "transactionId": key,
                "start": {"dateTime": utc(action["due"])[:-1], "timeZone": "UTC"},
                "end": {"dateTime": utc(action["end"])[:-1], "timeZone": "UTC"}}
        if action["minutes"] >= 0:
            body.update(isReminderOn=True, reminderMinutesBeforeStart=action["minutes"])
        request("POST", GRAPH + "/me/events", json=body)
    elif action["kind"] == "work":
        plan = settings["plan_id"]
        title = action["title"][:215] + " [Pala " + key[:12] + key[64:] + "]"
        if contains(GRAPH + "/planner/plans/" + quote(plan, safe="") + "/tasks", "title", title):
            return
        user = request("GET", GRAPH + "/me?$select=id")["id"]
        body = {"title": title, "planId": plan, "bucketId": settings["bucket_id"],
                "assignments": {user: {"@odata.type": "#microsoft.graph.plannerAssignment", "orderHint": " !"}}}
        if action["due"]:
            body["dueDateTime"] = utc(action["due"])
        request("POST", GRAPH + "/planner/tasks", json=body)
    else:
        url = GRAPH + "/me/onenote/sections/" + quote(settings["section_id"], safe="") + "/pages"
        title = action["title"][:70] + " [Pala " + key[:12] + key[64:] + "]"
        if contains(url + "?$select=title", "title", title):
            return
        page = "<html><head><title>" + html.escape(title) + "</title></head><body><p>" + html.escape(action["title"]) + "</p></body></html>"
        request("POST", url, data=page.encode(), headers={"Content-Type": "text/html; charset=utf-8"})
