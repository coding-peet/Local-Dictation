#include "../pala_note/src/app/button_gesture.h"
#include "../pala_note/src/app/display_pages.h"

static_assert(validDisplayPages("TECN", 4));
static_assert(validDisplayPages("MENtc", 5));
static_assert(validDisplayPages("TECNm", 5));
static_assert(!validDisplayPages("TECMM", 5));
static_assert(!validDisplayPages("TECM", 4));
static_assert(nextDisplayPageIndex("tencM", -1) == 4);
static_assert(nextDisplayPageIndex("Tencm", 0) == -1);
static_assert(validDisplayPages("NetC", 4));
static_assert(!validDisplayPages("TECc", 4));
static_assert(!validDisplayPages("TECX", 4));
static_assert(!validDisplayPages("TCN", 3));

// Disabled entries retain their order but do not consume a button press.
static_assert(nextDisplayPageIndex("NetCm", -1) == 0);
static_assert(nextDisplayPageIndex("NetCm", 0) == 3);
static_assert(nextDisplayPageIndex("NetCm", 3) == -1);
static_assert(nextDisplayPageIndex("nectm", -1) == -1);

constexpr bool displayPressHeldDuringRefreshDoesNotTurnPage() {
  ButtonGesture upper;
  upper.edge(true, 100, 1);
  if (upper.edge(false, 450, 1) != ButtonAction::Hold) return false;
  if (upper.poll(550, 1) != ButtonAction::None) return false;
  upper.edge(true, 600, 1);
  upper.edge(false, 660, 1);
  return upper.poll(700, 1) == ButtonAction::Single;
}

static_assert(displayPressHeldDuringRefreshDoesNotTurnPage());
