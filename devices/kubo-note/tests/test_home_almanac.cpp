#include "../pala_note/src/app/home_almanac.h"

// Dates of primary phases of the Moon, U.S. Naval Observatory.
static_assert(moonPhaseAt(0) == MoonPhase::Unknown);
static_assert(moonPhaseAt(1794207720) == MoonPhase::New);
static_assert(moonPhaseAt(1794916080) == MoonPhase::FirstQuarter);
static_assert(moonPhaseAt(1795531980) == MoonPhase::Full);
static_assert(moonPhaseAt(1796105280) == MoonPhase::LastQuarter);
static_assert(moonPhaseAt(1795176000) == MoonPhase::WaxingGibbous);
static_assert(validForecastDate("2028-02-29") && !validForecastDate("2026-02-29"));
static_assert(!validForecastDate("2026/11/20") && !validForecastDate("2026-11-20T00:00"));
static_assert(weatherGlyph(0) == WeatherGlyph::Sun);
static_assert(weatherGlyph(2) == WeatherGlyph::PartlyCloudy);
static_assert(weatherGlyph(45) == WeatherGlyph::Fog);
static_assert(weatherGlyph(80) == WeatherGlyph::Rain);
static_assert(weatherGlyph(85) == WeatherGlyph::Snow);
static_assert(weatherGlyph(96) == WeatherGlyph::Thunder);
