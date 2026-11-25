namespace {
constexpr uint32_t kPortalAgendaPageSize = 16;
constexpr uint32_t kPortalKeyBudget = 128;

String agendaRetryMessage() {
  return "<p role='status'>" + String(viewerText("Index wird aktualisiert. Diese Seite wird erneut geladen.",
      "Updating the index. This page will reload.")) + "</p>";
}

void sendAgendaUpdating(const String& html) {
  transferServer.sendHeader("Retry-After", "2");
  transferServer.sendHeader("Refresh", "2");
  transferServer.send(503, "text/html", html + agendaRetryMessage() + "</div></body></html>");
}


void beginAgendaHtml(const String& html) {
  transferServer.setContentLength(CONTENT_LENGTH_UNKNOWN);
  transferServer.send(200, "text/html", html);
}

void finishAgendaHtml(const String& navigation) {
  transferServer.sendContent(navigation +
      "<script>window.addEventListener('pageshow',function(e){if(e.persisted)location.reload();});"
      "document.querySelectorAll('[data-utc]').forEach(function(el){"
      "var dayOnly=el.dataset.dayOnly==='1',d=new Date(dayOnly?el.dataset.day+'T12:00:00':el.dataset.utc);"
      "if(!isNaN(d))el.textContent=dayOnly?d.toLocaleDateString():d.toLocaleString();});"
      "</script></div></body></html>");
  transferServer.sendContent(String());
}

bool useAgendaPage(AgendaIndexCursor& cursor, uint32_t firstPosition) {
  int start = 0, revision = 0;
  if (!positiveFormValue("start", start) || !positiveFormValue("rev", revision) ||
      static_cast<uint32_t>(revision) != cursor.generation) return false;
  if (cursor.recent ? static_cast<uint32_t>(start) > firstPosition
                   : static_cast<uint32_t>(start) < firstPosition || static_cast<uint32_t>(start) > cursor.end)
    return false;
  cursor.next = start;
  return cursor.next != firstPosition;
}

String agendaPageNavigation(const String& base, bool paged, bool more, uint32_t next, uint32_t revision) {
  String html = "<nav class='monthnav'>";
  if (paged) html += "<a class='pill' href='" + base + "'>" + viewerText("Erste Seite", "First page") + "</a>";
  if (more) html += "<a class='pill' href='" + base + (base.indexOf('?') < 0 ? "?" : "&amp;") +
      "start=" + String(next) + "&amp;rev=" + String(revision) + "'>" + viewerText("Weiter", "Next page") + "</a>";
  return html + "</nav>";
}

String taskDateHtml(const BrainTaskRecord& task, const String& day) {
  return "<span data-day='" + day + "' data-day-only='" + String(task.datePrecision == "day" ? "1" : "0") +
      "' data-utc='" + htmlEscape(task.dueUtc) + "'>" + htmlEscape(task.dueUtc) + "</span>";
}

struct AgendaWebPage {
  AgendaIndexCursor& cursor;
  String month;
  uint32_t emitted = 0;
  uint32_t next = 0;
  bool more = false;
};

String calendarMonthKey(int year, int month) {
  char value[12];
  snprintf(value, sizeof(value), "%04d-%02d", year, month);
  return String(value);
}

bool validCalendarMonthKey(const String& value) {
  return value.length() == 7 && value[4] == '-' &&
      value.substring(0, 4).toInt() >= 2020 && value.substring(0, 4).toInt() <= 2100 &&
      value.substring(5, 7).toInt() >= 1 && value.substring(5, 7).toInt() <= 12 &&
      calendarMonthKey(value.substring(0, 4).toInt(), value.substring(5, 7).toInt()) == value;
}

struct AgendaMonthMarks {
  String month;
  std::array<uint8_t, 32> kinds{};
  std::array<uint8_t, 32> counts{};
  AgendaIndexCursor scan{};
  uint32_t first = UINT32_MAX, end = 0;
  bool ready = false;
};
AgendaMonthMarks monthMarks;

bool addAgendaMonthMark(const char* due, bool event, void* context) {
  auto& marks = *static_cast<AgendaMonthMarks*>(context);
  if (!*due) return true;
  const String localDue = utcToLocalDeviceLabel(due);
  if (localDue.length() < 10 || localDue == "time not set") return true;
  const String month = localDue.substring(0, 7);
  if (month != marks.month) return month < marks.month;
  if (marks.first == UINT32_MAX) marks.first = marks.scan.next - 1;
  marks.end = marks.scan.next;
  const int day = localDue.substring(8, 10).toInt();
  if (day >= 1 && day <= 31 && marks.counts[day] < 5) {
    if (event) marks.kinds[day] |= 1U << marks.counts[day];
    ++marks.counts[day];
  }
  return true;
}

void advanceCalendarMonth() {
  if (monthMarks.month.isEmpty() || monthMarks.ready) return;
  if (monthMarks.scan.generation != agendaIndexGeneration()) { monthMarks = {}; return; }
  if (!visitAgendaDates(monthMarks.scan, addAgendaMonthMark, &monthMarks, kPortalKeyBudget, monthMarks.ready)) {
    monthMarks = {};
    requestAgendaIndexRebuild();
  }
}

String calendarMonthGrid(const AgendaMonthMarks& marks, const String& today) {
  const int year = marks.month.substring(0, 4).toInt();
  const int month = marks.month.substring(5, 7).toInt();
  const int first = weekDayIndex(year, month, 1);
  const int last = weekMonthDays(year, month);
  const int highlighted = today.substring(0, 7) == marks.month ? today.substring(8, 10).toInt() : 0;
  String html = "<section class='calendar'><div class='calhead'>";
  const char* weekdaysDe[] = {"Mo","Di","Mi","Do","Fr","Sa","So"};
  const char* weekdaysEn[] = {"Mon","Tue","Wed","Thu","Fri","Sat","Sun"};
  for (int i = 0; i < 7; ++i) html += "<span>" + String(viewerText(weekdaysDe[i], weekdaysEn[i])) + "</span>";
  html += "</div><div class='calgrid'>";
  for (int i = 0; i < first; ++i) html += "<div class='calday blank'></div>";
  for (int day = 1; day <= last; ++day) {
    html += "<div class='calday" + String(day == highlighted ? " today" : "") + "'>" + String(day) + "<div class='dots'>";
    for (unsigned i = 0; i < marks.counts[day]; ++i)
      html += marks.kinds[day] & (1U << i) ? "<i class='dot'></i>" : "<i class='dot task'></i>";
    html += "</div></div>";
  }
  return html + "</div></section>";
}

bool emitAgendaTask(const BrainTaskRecord& task, void* context) {
  auto& page = *static_cast<AgendaWebPage*>(context);
  if (page.emitted == kPortalAgendaPageSize) { page.more = true; return false; }
  const String due = utcToLocalDeviceLabel(task.dueUtc);
  String html = "<article class='taskrow " + String(task.done ? "completed" : "") +
      "'><form class='checkform' action='/task/status' method='post'><input type='hidden' name='id' value='" +
      htmlEscape(task.id) + "'><input type='hidden' name='done' value='" + String(task.done ? "0" : "1") +
      "'><input type='hidden' name='return' value='/tasks'><button class='check " +
      String(task.done ? "done" : "") + "' type='submit'>" + String(task.done ? "&#10003;" : "") +
      "</button></form><div class='taskcopy'><b>" + htmlEscape(task.text) + "</b><div class='taskmeta'>";
  if (!task.dueUtc.isEmpty())
    html += String(viewerText("Fällig ", "Due ")) + taskDateHtml(task, due.substring(0, 10)) + " · ";
  html += String(viewerText("Gedanke #", "Memory #")) + String(task.noteNum) +
      "</div></div><span class='badge'>" +
      String(task.done ? viewerText("Erledigt", "Done") : viewerText("Offen", "Open")) + "</span></article>";
  transferServer.sendContent(html);
  ++page.emitted;
  page.next = page.cursor.next;
  return true;
}

bool emitCalendarTask(const BrainTaskRecord& task, void* context) {
  auto& page = *static_cast<AgendaWebPage*>(context);
  if (task.dueUtc.isEmpty()) return true;
  const String due = utcToLocalDeviceLabel(task.dueUtc);
  if (due.length() < 10 || due == "time not set") return true;
  const String month = due.substring(0, 7);
  if (month != page.month) return month < page.month;
  if (page.emitted == kPortalAgendaPageSize) { page.more = true; return false; }
  const bool event = task.type == "event";
  const String kind = event ? viewerText("Termin", "Event") : viewerText("Aufgabe", "Task");
  const String completion = event ? "" : task.done ? viewerText("Erledigt", "Done") : viewerText("Offen", "Open");
  transferServer.sendContent("<article class='agendarow agenda-item" + String(!event && task.done ? " completed" : "") +
      "'><div class='agendadate'>" +
      taskDateHtml(task, due.substring(0, 10)) + "</div><div class='agendacopy'><b>" + htmlEscape(task.text) +
      "</b><span>" + kind + (completion.isEmpty() ? String() : " · " + completion) + " · #" + String(task.noteNum) + "</span></div><span class='badge'>" +
      kind + "</span></article>");
  ++page.emitted;
  page.next = page.cursor.next;
  return true;
}
} // namespace

static void handleTasksPage() {
  transferServer.sendHeader("Cache-Control", "no-store");
  const String title = viewerText("Aufgaben", "Tasks");
  String html = portalPageStart(palaNoteName() + " · " + title) + portalNav("tasks") +
      "<div class='top'><div><h1>" + title + "</h1></div></div>";
  AgendaIndexCursor cursor;
  if (!noteCatalogReady() || !openAgendaIndex(cursor, true)) {
    sendAgendaUpdating(html);
    return;
  }
  cursor.mask = 1; // Tasks excludes calendar events.
  const bool paged = useAgendaPage(cursor, cursor.next);
  uint32_t total, open;
  agendaTaskCounts(total, open);
  html += "<div class='pill'>" + String(open) + " " + viewerText("offen", "open") + " · " +
      String(total) + " " + viewerText("gesamt", "total") + "</div><div class='tasklist'>";
  beginAgendaHtml(html);
  AgendaWebPage page{cursor, ""};
  bool moreKeys = false;
  if (total && !visitAgendaIndex(cursor, emitAgendaTask, &page, kPortalKeyBudget, &moreKeys)) {
    transferServer.sendContent(agendaRetryMessage() + "<script>setTimeout(function(){location.reload()},2000)</script>");
  } else if (moreKeys) {
    page.more = true;
    page.next = cursor.next;
    if (!page.emitted) transferServer.sendContent("<p>" + String(viewerText("Weitere Einträge auf der nächsten Seite.",
        "Continue to the next page for more entries.")) + "</p>");
  } else if (!page.emitted)
    transferServer.sendContent("<div class='empty'>" +
        String(paged ? viewerText("Keine weiteren Aufgaben.", "No further tasks.") :
                       viewerText("Noch keine Aufgaben gespeichert.", "No tasks remembered yet.")) + "</div>");
  transferServer.sendContent("</div>");
  finishAgendaHtml(agendaPageNavigation("/tasks", paged, page.more, page.next, cursor.generation));
}

static void handleCalendarPage() {
  transferServer.sendHeader("Cache-Control", "no-store");
  const String title = viewerText("Kalender", "Calendar");
  String html = portalPageStart(palaNoteName() + " · " + title) + portalNav("calendar") +
      "<div class='top'><div><h1>" + title + "</h1></div>";
  const String today = utcToLocalDeviceLabel(currentUtcIso()).substring(0, 10);
  String month = transferServer.arg("month");
  if (!validCalendarMonthKey(month)) month = today.substring(0, 7);
  if (!validCalendarMonthKey(month)) {
    transferServer.send(200, "text/html", html + "</div><div class='empty'>" +
        viewerText("Datum fehlt.", "Date not set.") + "</div></div></body></html>");
    return;
  }
  const int y = month.substring(0, 4).toInt(), m = month.substring(5, 7).toInt();
  const String previous = calendarMonthKey(y - (m == 1), m == 1 ? 12 : m - 1);
  const String next = calendarMonthKey(y + (m == 12), m == 12 ? 1 : m + 1);
  html += "<nav class='monthnav'>";
  if (validCalendarMonthKey(previous)) html += "<a class='monthnav-button' aria-label='Previous month' href='/calendar?month=" + previous + "'>&lsaquo;</a>";
  html += "<div class='pill'>" + month + "</div>";
  if (validCalendarMonthKey(next)) html += "<a class='monthnav-button' aria-label='Next month' href='/calendar?month=" + next + "'>&rsaquo;</a>";
  html += "<a class='pill' href='/calendar'>" + String(viewerText("Heute", "This month")) + "</a></nav></div>";
  // UTC boundaries for a local month can lie on either side of midnight.
  const String monthStartUtc = previous + "-" +
      String(weekMonthDays(y - (m == 1), m == 1 ? 12 : m - 1));
  AgendaIndexCursor cursor;
  if (!noteCatalogReady() || !openAgendaIndex(cursor, false, monthStartUtc.c_str())) { sendAgendaUpdating(html); return; }
  if (monthMarks.month != month || monthMarks.scan.generation != cursor.generation) {
    monthMarks = {};
    monthMarks.month = month;
    monthMarks.scan = cursor;
  }
  advanceCalendarMonth();
  if (!monthMarks.ready) { sendAgendaUpdating(html); return; }
  cursor.next = monthMarks.first == UINT32_MAX ? 0 : monthMarks.first;
  cursor.end = monthMarks.end;
  const bool paged = useAgendaPage(cursor, cursor.next);
  html += calendarMonthGrid(monthMarks, today) + "<div class='sectionhead'><div><h2>Agenda</h2></div></div><div class='agenda'>";
  beginAgendaHtml(html);
  AgendaWebPage page{cursor, month};
  if (!visitAgendaIndex(cursor, emitCalendarTask, &page))
    transferServer.sendContent(agendaRetryMessage() + "<script>setTimeout(function(){location.reload()},2000)</script>");
  else if (!page.emitted)
    transferServer.sendContent("<div class='empty'>" +
        String(viewerText("Keine Termine in diesem Monat.", "No appointments or dated tasks in this month.")) + "</div>");
  transferServer.sendContent("</div>");
  finishAgendaHtml(agendaPageNavigation("/calendar?month=" + month, paged, page.more, page.next, cursor.generation));
}

void maintainCompanionAgenda() {
  if (!noteCatalogReady()) { maintainNoteCatalog(); return; }
  if (!agendaIndexReady()) { maintainAgendaIndex(); return; }
  advanceCalendarMonth();
}

static void handleTaskStatus() {
  transferServer.sendHeader("Cache-Control", "no-store");
  const String value = transferServer.arg("done");
  if (value != "0" && value != "1") { transferServer.send(400, "text/plain", "Invalid task status"); return; }
  if (!noteCatalogReady()) { transferServer.send(503, "text/plain", "Storage is updating. Please retry the edit."); return; }
  const auto result = editBrainTask(transferServer.arg("id"), value == "1");
  if (result != TaskEdit::Saved) {
    if (result == TaskEdit::Invalid) transferServer.send(400, "text/plain", "Invalid task");
    else if (result == TaskEdit::Missing) transferServer.send(404, "text/plain", "Task not found");
    else transferServer.send(503, "text/plain", "Could not save the task on the SD card. Please retry the edit.");
    return;
  }
  String returnTo = transferServer.arg("return");
  if (returnTo != "/" && returnTo != "/tasks" && returnTo != "/calendar") returnTo = "/tasks";
  transferServer.sendHeader("Location", returnTo);
  transferServer.send(303);
}
