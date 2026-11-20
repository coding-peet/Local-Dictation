#include "../pala_note/src/app/calendar_week.h"

// Midnight refreshes the Home view with the same local date model.
constexpr auto thursday = calendarWeek(2026, 11, 19);
static_assert(thursday.number == 47);
static_assert(thursday.today == 3);
static_assert(thursday.days[0] == 16 && thursday.days[6] == 22);

constexpr auto newYear = calendarWeek(2021, 1, 1);
static_assert(newYear.number == 53);
static_assert(newYear.today == 4);
static_assert(newYear.days[0] == 28 && newYear.days[6] == 3);

constexpr auto nextWeek = calendarWeek(2021, 1, 4);
static_assert(nextWeek.number == 1 && nextWeek.today == 0);
static_assert(nextWeek.days[0] == 4 && nextWeek.days[6] == 10);

constexpr auto endOfYear = calendarWeek(2024, 12, 30);
static_assert(endOfYear.number == 1 && endOfYear.today == 0);
static_assert(endOfYear.days[0] == 30 && endOfYear.days[6] == 5);

constexpr auto leapWeek = calendarWeek(2024, 2, 29);
static_assert(leapWeek.days[0] == 26 && leapWeek.days[6] == 3);