#pragma once
#include <array>

struct CalendarWeek {
  int number;
  int today;  // Monday = 0.
  std::array<int, 7> days;
};

constexpr bool weekLeapYear(int year) {
  return year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);
}

constexpr int weekMonthDays(int year, int month) {
  constexpr int length[] = {31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31};
  return length[month - 1] + (month == 2 && weekLeapYear(year) ? 1 : 0);
}

constexpr int weekDayIndex(int year, int month, int day) {
  constexpr int offset[] = {0, 3, 2, 5, 0, 3, 5, 1, 4, 6, 2, 4};
  const int adjustedYear = year - (month < 3 ? 1 : 0);
  const int sundayBased = (adjustedYear + adjustedYear / 4 - adjustedYear / 100 +
                           adjustedYear / 400 + offset[month - 1] + day) % 7;
  return (sundayBased + 6) % 7;
}

constexpr int numberOfCalendarWeeks(int year) {
  const int januaryFirst = weekDayIndex(year, 1, 1);
  return januaryFirst == 3 || (januaryFirst == 2 && weekLeapYear(year)) ? 53 : 52;
}

struct CalendarDate {
  int year;
  int month;
  int day;
};

// The Home strip has at most six days on either side of the anchor date.
constexpr CalendarDate calendarDateInWeek(int year, int month, int day, int offset) {
  day += offset;
  if (day < 1) {
    if (--month < 1) {
      month = 12;
      --year;
    }
    day += weekMonthDays(year, month);
  } else if (day > weekMonthDays(year, month)) {
    day -= weekMonthDays(year, month);
    if (++month > 12) {
      month = 1;
      ++year;
    }
  }
  return {year, month, day};
}

constexpr CalendarWeek calendarWeek(int year, int month, int day) {
  if (year < 2020 || year > 2100 || month < 1 || month > 12 ||
      day < 1 || day > weekMonthDays(year, month)) return {};
  const int today = weekDayIndex(year, month, day);
  int ordinal = day;
  for (int i = 1; i < month; i++) ordinal += weekMonthDays(year, i);
  int number = (ordinal - today + 9) / 7;
  if (number < 1) number = numberOfCalendarWeeks(year - 1);
  else if (number > numberOfCalendarWeeks(year)) number = 1;

  CalendarWeek week{number, today, {}};
  for (int index = 0; index < 7; index++) {
    week.days[index] = calendarDateInWeek(year, month, day, index - today).day;
  }
  return week;
}
struct CalendarMonth {
  int year;
  int month;
  int today;
  int rows;
  std::array<CalendarWeek, 6> weeks;
};

constexpr CalendarMonth calendarMonth(int year, int month, int day, int monthOffset) {
  if (monthOffset < 0 || monthOffset > 2 || !calendarWeek(year, month, day).number) return {};
  month += monthOffset;
  if (month > 12) {
    month -= 12;
    ++year;
  }
  if (year > 2100) return {};

  const int first = weekDayIndex(year, month, 1);
  const int days = weekMonthDays(year, month);
  CalendarMonth result{year, month, monthOffset == 0 ? day : 0, (first + days + 6) / 7, {}};

  for (int row = 0; row < result.rows; ++row) {
    const int monday = 1 + row * 7 - first;
    auto& week = result.weeks[row];
    week.number = calendarWeek(year, month, monday < 1 ? 1 : monday).number;
    for (int column = 0; column < 7; ++column) {
      const int date = monday + column;
      if (date >= 1 && date <= days) week.days[column] = date;
    }
  }
  return result;
}
