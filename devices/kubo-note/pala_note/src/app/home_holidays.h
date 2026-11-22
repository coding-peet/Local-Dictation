#pragma once

#include <Arduino.h>

void loadHomeHolidays();
bool homeHolidayFor(int year, int month, int day);
bool homeHolidaysNeedRefresh(int currentYear);
bool storeHomeHolidays(const String& snapshot, bool& changed);
bool refreshHomeHolidays();
