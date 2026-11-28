#if PALA_WORKSPACE_CALENDAR
#include "workspace_calendar.h"
#include "workspace_view_data.h"

#include <ArduinoJson.h>
#include <HTTPClient.h>
#include <ctime>
#include <utility>
#include <vector>

#include "bridge_client.h"
#include "display_pipeline.h"
#include "draw.h"
#include "home_almanac.h"
#include "note_peers.h"
#include "notes.h"

namespace {
using workspace_view::maxRows;
using workspace_view::rowsPerPage;
using workspace_view::Status;
#if PALA_STAGE3
constexpr int maxBody = 32768;
#else
constexpr int maxBody = 16384;
#endif

struct Row {
  String title;
  String start;
  String end;
  bool event;
};

struct Snapshot {
  std::vector<Row> rows;
  String peerId;
  int64_t updated = 0;
  bool offline = false;
  bool truncated = false;
#if PALA_STAGE3
  std::optional<workspace_view::SavedItems> saved;
#endif
};

Snapshot calendar;

bool usableTime(const String& value) {
  const auto day = value.substring(0, 10);
  if (!validForecastDate(day.c_str())) return false;
  if (value.length() == 10) return true;
  return value.length() == 16 && value[10] == 'T' && value[13] == ':' &&
         isDigit(value[11]) && isDigit(value[12]) &&
         isDigit(value[14]) && isDigit(value[15]) &&
         value.substring(11, 13).toInt() < 24 && value.substring(14).toInt() < 60;
}

Status snapshotStatus() {
  const bool paired = calendar.updated > 0 && !notePeerById(calendar.peerId).token.isEmpty();
  return workspace_view::cacheStatus(time(nullptr), calendar.updated, paired, calendar.offline);
}

String offlineLabel() {
  char iso[21]{};
  const time_t updated = calendar.updated;
  const tm* utc = gmtime(&updated);
  if (utc) strftime(iso, sizeof(iso), "%Y-%m-%dT%H:%M:%SZ", utc);
  const String local = utcToLocalDeviceLabel(iso);
  return "OFFLINE " + local.substring(8, 10) + "." + local.substring(5, 7) + " " + local.substring(11, 16);
}

bool parseCalendar(const String& body, const String& peer, Snapshot& next) {
  JsonDocument doc;
  if (deserializeJson(doc, body, DeserializationOption::NestingLimit(4))) return false;
  const String status = doc["status"] | "";
  if (status == "unavailable") return true;
  if (status != "online" && status != "offline") return false;
  next.peerId = peer;
  next.updated = doc["updated"] | int64_t{0};
  next.offline = status == "offline";
  next.truncated = doc["truncated"] | false;
#if PALA_STAGE3
  next.saved = workspace_view::readSavedItems(doc["saved"]);
#endif
  const auto rows = doc["rows"].as<JsonArrayConst>();
  if (next.updated <= 0 || rows.isNull() || rows.size() > maxRows) return false;
  next.rows.reserve(rows.size());
  for (JsonObjectConst item : rows) {
    const String kind = item["kind"] | "";
    Row row = {item["title"] | "", item["start"] | "", item["end"] | "", kind == "event"};
    if ((kind != "task" && kind != "event") || row.title.length() > 400 ||
        !usableTime(row.start) || (row.event && !usableTime(row.end))) return false;
    next.rows.push_back(std::move(row));
  }
  return true;
}

String dayTime(const Row& row) {
  String result = row.start.substring(8, 10) + "." + row.start.substring(5, 7);
  if (row.start.length() == 16) {
    result += "  " + row.start.substring(11);
    if (row.end.length() == 16 && row.start.substring(0, 10) == row.end.substring(0, 10))
      result += "-" + row.end.substring(11);
  }
  return result;
}

int fetchWorkspaceCalendar(ProviderClient& client, const char* path) {
  const String url = client.url(path);
  if (url.isEmpty()) {
    calendar.offline = true;
    return -1;
  }
  if (calendar.peerId != client.bridge().id) calendar = Snapshot{};
  HTTPClient http;
  http.setConnectTimeout(600);
  http.setTimeout(700);
  if (!http.begin(client, url)) {
    calendar.offline = true;
    return -1;
  }
  http.addHeader("Authorization", "Bearer " + client.bridge().token);
  const int status = http.GET();
  const int size = http.getSize();
  if (status == HTTP_CODE_OK && size > 0 && size <= maxBody) {
    const auto body = http.getString();
    Snapshot next;
    if (body.length() == static_cast<size_t>(size) && parseCalendar(body, client.bridge().id, next)) {
      calendar = std::move(next);
    } else {
      calendar.offline = true;
    }
  } else if (status == HTTP_CODE_UNAUTHORIZED || status == HTTP_CODE_FORBIDDEN ||
             status == HTTP_CODE_NOT_FOUND) {
    calendar = Snapshot{};
  } else {
    calendar.offline = true;
  }
  http.end();
  return status;
}
}

void refreshWorkspaceCalendar() {
  ProviderClient client;
  client.setHandshakeTimeout(1);
#if PALA_STAGE3
  if (fetchWorkspaceCalendar(client, "/pala/workspace-calendar?v=2") != HTTP_CODE_NOT_FOUND) return;
#endif
  fetchWorkspaceCalendar(client, "/pala/workspace-calendar");
}

int workspaceCalendarPages() {
  if (snapshotStatus() == Status::Unavailable || calendar.rows.empty()) return 1;
  return (calendar.rows.size() + rowsPerPage - 1) / rowsPerPage;
}

void showWorkspaceCalendar(int page) {
  clearWhite();
  drawStr(10, 8, "CALENDAR", 1, BLACK);
  hline(10, 29, 180, BLACK);
  const Status availability = snapshotStatus();
  if (availability == Status::Unavailable) {
    drawStrC(W / 2, 78, "UNAVAILABLE", 1, BLACK);
    drawStrC(W / 2, 100, "Refresh from Home", 1, BLACK);
  } else {
    String status;
    if (availability == Status::Offline) {
      status = offlineLabel();
    } else if (calendar.truncated) {
      status = "FIRST " + String(maxRows);
    }
    if (!status.isEmpty()) drawStrFit(10, 35, 180, status.c_str(), 1, BLACK);
    if (calendar.rows.empty()) drawStrC(W / 2, 85, "NO UPCOMING ITEMS", 1, BLACK);
    for (int line = 0; line < rowsPerPage; ++line) {
      const auto index = page * rowsPerPage + line;
      if (index < 0 || index >= static_cast<int>(calendar.rows.size())) break;
      const auto& row = calendar.rows[index];
      const int y = 54 + 38 * line;
      const String when = dayTime(row) + (row.event ? "  EVENT" : "  TASK");
      drawStrFit(10, y, 180, when.c_str(), 1, BLACK);
      const String title = normalizeForDisplay(row.title);
      drawStrFit(10, y + 16, 180, title.isEmpty() ? "Untitled" : title.c_str(), 1, BLACK);
    }
  }
  hline(10, 174, 180, BLACK);
  if (page + 1 < workspaceCalendarPages()) drawStr(10, 181, "NEXT", 1, BLACK);
#if PALA_STAGE3
  else drawStr(10, 181, "SAVED ITEMS", 1, BLACK);
#endif
  drawStr(145, 181, "HOME", 1, BLACK);
  note_display::present(note_display::Refresh::Regular, nullptr, true);
}

#if PALA_STAGE3
int workspaceSavedItemsPages() {
  return workspace_view::savedPages(snapshotStatus() != Status::Unavailable && calendar.saved
                                       ? &*calendar.saved : nullptr);
}

void showWorkspaceSavedItems(int page) {
  clearWhite();
  drawStr(10, 8, "SAVED ITEMS", 1, BLACK);
  hline(10, 29, 180, BLACK);
  const Status availability = snapshotStatus();
  if (!calendar.saved || availability == Status::Unavailable) {
    drawStrC(W / 2, 78, "UNAVAILABLE", 1, BLACK);
    drawStrC(W / 2, 100, "Refresh from Home", 1, BLACK);
  } else {
    const auto& list = *calendar.saved;
    const String listName = normalizeForDisplay(list.title.c_str());
    if (!listName.isEmpty()) drawStrFit(10, 33, 180, listName.c_str(), 1, BLACK);
    if (availability == Status::Offline) {
      const auto status = offlineLabel();
      drawStrFit(10, 50, 145, status.c_str(), 1, BLACK);
    }
    if (list.truncated) drawStrFit(159, 50, 31, "32+", 1, BLACK);
    if (list.rows.empty()) drawStrC(W / 2, 86, "NO OPEN ITEMS", 1, BLACK);
    for (int row = 0; row < rowsPerPage; ++row) {
      const auto index = page * rowsPerPage + row;
      if (index < 0 || index >= static_cast<int>(list.rows.size())) break;
      const int y = 68 + row * 35;
      auto title = normalizeForDisplay(list.rows[index].c_str());
      if (title.isEmpty()) title = "Untitled";
      constexpr int titleWidth = 180;
      const auto firstLine = workspace_view::firstTitleLine(
          {title.c_str(), title.length()}, [](std::string_view prefix) {
            return textW(std::string(prefix).c_str(), 1) <= titleWidth;
          });
      drawStrFit(10, y, titleWidth, title.substring(0, firstLine).c_str(), 1, BLACK);
      if (firstLine < title.length()) {
        auto remainder = title.substring(firstLine);
        remainder.trim();
        drawStrFit(10, y + 15, titleWidth, remainder.c_str(), 1, BLACK);
      }
    }
  }
  hline(10, 174, 180, BLACK);
  if (page + 1 < workspaceSavedItemsPages()) drawStr(10, 181, "NEXT", 1, BLACK);
  drawStr(145, 181, "HOME", 1, BLACK);
  note_display::present(note_display::Refresh::Regular, nullptr, true);
}
#endif
#endif
