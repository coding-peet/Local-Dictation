#include "../pala_note/src/app/calendar_week.h"
#include "../pala_note/src/app/holiday_calendar.h"

constexpr auto slovak = "SK,2026,1226,01010106040304060501070508291101122412251226,"
                        "0101010603260329050105080705082909151101122412251226";

constexpr bool hasHoliday(const char* snapshot, CalendarDate date) {
  HolidayCalendar calendar{};
  return parseHolidaySnapshot(snapshot, calendar) && calendar.contains(date.year, date.month, date.day);
}

static_assert(hasHoliday(slovak, {2026, 4, 3}));
static_assert(!hasHoliday(slovak, {2026, 5, 8}));
static_assert(!hasHoliday(slovak, {2026, 9, 15}));
static_assert(hasHoliday(slovak, {2027, 5, 8}));
static_assert(hasHoliday(slovak, {2027, 9, 15}));

// Home week can reach into the next year or the preceding December.
static_assert(hasHoliday(slovak, calendarDateInWeek(2026, 12, 31, 1)));
static_assert(!hasHoliday(slovak, calendarDateInWeek(2027, 1, 1, -1)));
static_assert(hasHoliday("SK,2034,1226,0101,0101", calendarDateInWeek(2034, 1, 1, -6)));
static_assert(!hasHoliday("SK,2034,1226,0101,0101", calendarDateInWeek(2034, 1, 1, -5)));
static_assert(hasHoliday("CZ,2028,1226,0501,0101", calendarDateInWeek(2028, 4, 30, 1)));

constexpr bool rejectsInvalidSnapshotsWithoutReplacingTheCache() {
  HolidayCalendar calendar{};
  return parseHolidaySnapshot(slovak, calendar) &&
    !parseHolidaySnapshot("SK,2026,1226,0132,0101", calendar) &&
    !parseHolidaySnapshot("SK,2026,1225,0101,0101", calendar) &&
    !parseHolidaySnapshot("SK,2026,1226,01010101,0101", calendar) &&
    !parseHolidaySnapshot("SK,2026,1226,0101,", calendar) &&
    calendar.contains(2027, 5, 8);
}
static_assert(rejectsInvalidSnapshotsWithoutReplacingTheCache());
