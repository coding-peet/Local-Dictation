"""Statutory nationwide holidays that are days of rest."""

from datetime import date, timedelta


HOLIDAY_COUNTRIES = ("SK", "CZ")
DEFAULT_HOLIDAY_COUNTRY = "SK"


def _easter_sunday(year):
    # Gregorian computus (Meeus/Jones/Butcher).
    golden_number = year % 19
    century, remainder = divmod(year, 100)
    leap_centuries, century_remainder = divmod(century, 4)
    lunar_correction = (century - (century + 8) // 25 + 1) // 3
    epact = (19 * golden_number + century - leap_centuries - lunar_correction + 15) % 30
    leap_years, year_remainder = divmod(remainder, 4)
    weekday = (32 + 2 * century_remainder + 2 * leap_years - epact - year_remainder) % 7
    correction = (golden_number + 11 * epact + 22 * weekday) // 451
    month, day_index = divmod(epact + weekday - 7 * correction + 114, 31)
    return date(year, month, day_index + 1)


def statutory_holidays(country, year):
    if country == "SK":
        # Act 241/1993 Coll., section 2 and the temporary provision in section 4b.
        # https://www.slov-lex.sk/ezbierky/pravne-predpisy/SK/ZZ/1993/241/20261029
        fixed = [(1, 1), (1, 6), (5, 1), (7, 5), (8, 29), (11, 1), (12, 24), (12, 25), (12, 26)]
        if year != 2026:
            fixed += [(5, 8), (9, 15)]
        if year < 2024:
            fixed.append((9, 1))
        if year < 2025:  # The removal took effect on November 1, 2025.
            fixed.append((11, 17))
    elif country == "CZ":
        # Act 245/2000 Coll., sections 1, 2 and 3.
        # https://e-sbirka.gov.cz/sb/2000/245
        fixed = [(1, 1), (5, 1), (5, 8), (7, 5), (7, 6), (9, 28),
                 (10, 28), (11, 17), (12, 24), (12, 25), (12, 26)]
    else:
        raise ValueError("Select a supported holiday country.")
    easter = _easter_sunday(year)
    dates = [date(year, month, day) for month, day in fixed]
    dates.extend((easter - timedelta(days=2), easter + timedelta(days=1)))
    return tuple(sorted(dates))


def holiday_snapshot(country, year):
    """Encode the calendar year groups for the Note's bounded sync frame."""
    previous_december = (day for day in statutory_holidays(country, year - 1)
                         if day >= date(year - 1, 12, 26))
    groups = (previous_december, statutory_holidays(country, year), statutory_holidays(country, year + 1))
    return ",".join((country, str(year), *("".join(f"{day.month:02d}{day.day:02d}" for day in group)
                                            for group in groups)))
