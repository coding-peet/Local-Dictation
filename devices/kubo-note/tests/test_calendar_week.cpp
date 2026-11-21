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
constexpr auto november = calendarMonth(2026, 11, 21, 0);
static_assert(november.rows == 6 && november.today == 21);
static_assert(november.weeks[0].number == 44 && november.weeks[0].days[0] == 0);
static_assert(november.weeks[0].days[6] == 1);
static_assert(november.weeks[5].number == 49 && november.weeks[5].days[0] == 30);
static_assert(november.weeks[5].days[1] == 0);

constexpr auto january = calendarMonth(2026, 12, 31, 1);
static_assert(january.year == 2027 && january.month == 1 && january.today == 0);
static_assert(january.weeks[0].number == 53 && january.weeks[0].days[4] == 1);
static_assert(january.weeks[1].number == 1);
static_assert(calendarMonth(2024, 2, 29, 0).weeks[4].days[3] == 29);
static_assert(calendarMonth(2021, 2, 1, 0).rows == 4);
static_assert(calendarMonth(2026, 2, 30, 1).rows == 0);

constexpr auto januaryAfterNext = calendarMonth(2026, 11, 21, 2);
static_assert(januaryAfterNext.year == 2027 && januaryAfterNext.month == 1);
static_assert(januaryAfterNext.today == 0 && januaryAfterNext.weeks[0].number == 53);
constexpr auto leapFebruaryAfterNext = calendarMonth(2023, 12, 31, 2);
static_assert(leapFebruaryAfterNext.year == 2024 && leapFebruaryAfterNext.month == 2);
static_assert(leapFebruaryAfterNext.weeks[4].days[3] == 29);
static_assert(calendarMonth(2026, 11, 21, 3).rows == 0);
