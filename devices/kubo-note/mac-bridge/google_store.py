"""Personal appointments in the paired user's Google Calendar."""
import hashlib
import os


def google_credentials(state, interactive=False, *, scopes=None,
                       token_name="google-token.json", client_path=None):
    from google.auth.transport.requests import Request
    from google.oauth2.credentials import Credentials
    from google_auth_oauthlib.flow import InstalledAppFlow
    if scopes is None:
        scopes = ["https://www.googleapis.com/auth/calendar.events.owned"]
    path = state / token_name
    creds = Credentials.from_authorized_user_file(str(path), scopes) if path.exists() else None
    if creds and creds.expired and creds.refresh_token:
        creds.refresh(Request())
    if not creds or not creds.valid:
        if not interactive:
            raise RuntimeError("Spusť bridge.py --google-login.")
        config = client_path if client_path is not None else state / "google-client.json"
        flow = InstalledAppFlow.from_client_secrets_file(str(config), scopes)
        creds = flow.run_local_server(port=0)
    temp = path.with_suffix(".tmp")
    temp.write_text(creds.to_json())
    os.replace(temp, path)
    return creds


def apply_google(config, state, action, key):
    from googleapiclient.discovery import build
    from googleapiclient.errors import HttpError
    events = build("calendar", "v3", credentials=google_credentials(state), cache_discovery=False).events()
    body = {"id": hashlib.sha256(key.encode()).hexdigest(), "summary": action["title"],
            "start": {"dateTime": action["due"] + ":00", "timeZone": config["timezone"]},
            "end": {"dateTime": action["end"] + ":00", "timeZone": config["timezone"]}}
    if action["minutes"] >= 0:
        body["reminders"] = {"useDefault": False, "overrides": [
            {"method": "popup", "minutes": min(40320, action["minutes"])}]}
    try:
        events.insert(calendarId=config["calendar"], body=body).execute()
    except HttpError as err:
        if err.resp.status != 409:
            raise
