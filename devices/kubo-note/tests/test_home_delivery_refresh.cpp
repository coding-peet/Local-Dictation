#include "../pala_note/src/app/home_delivery_refresh.h"

namespace {
constexpr bool followsAsynchronousDelivery() {
  HomeDeliveryRefresh refresh;
  if (!refresh.needsRoutineRefresh(1) || refresh.needsFollowup(1)) return false;
  refresh.received(100, 0);
  refresh.submitted(1000);
  refresh.received(1200, 1);
  if (refresh.needsRoutineRefresh(16000) || refresh.needsFollowup(15999)) return false;
  if (!refresh.needsFollowup(16000)) return false;
  refresh.received(18000, 0);
  return !refresh.needsFollowup(33000) &&
         !refresh.needsRoutineRefresh(137999) && refresh.needsRoutineRefresh(138000);
}

constexpr bool stopsFastChecksAfterTheDeliveryWindow() {
  HomeDeliveryRefresh refresh;
  refresh.received(80, 0);
  refresh.submitted(0xfffffff0u);
  if (refresh.needsFollowup(14983) || !refresh.needsFollowup(14984)) return false;
  return refresh.needsFollowup(59983) && !refresh.needsFollowup(59984);
}

static_assert(followsAsynchronousDelivery());
static_assert(stopsFastChecksAfterTheDeliveryWindow());
}

#include "../pala_note/src/app/home_dashboard.h"

constexpr bool readsDashboard() {
  HomeDeliveryCounts counts;
  if (!parseHomeDashboard("h2,17,2,19:32", counts) || counts.generalOpen != 17 ||
      counts.waiting != 2 || std::string_view(counts.lastDelivery) != "19:32") return false;
  return parseHomeDashboard("h2,-1,0,-", counts) && counts.generalOpen == -1 &&
         counts.waiting == 0 && counts.lastDelivery[0] == 0 &&
         !parseHomeDashboard("h2,3,0,24:99", counts) &&
         !parseHomeDashboard("h2,-2,0,-", counts) &&
         !parseHomeDashboard("h2,3,-1,00:00", counts);
}
static_assert(readsDashboard());

constexpr bool readsFreshCodexQuota() {
  HomeDeliveryCounts counts;
  if (!parseHomeDashboard("h3,17,2,19:32,29,1795000200", counts) ||
      counts.codexAt(1795000199, false).remaining != 29 ||
      counts.codexAt(1795000200, false).remaining != -1 ||
      counts.codexAt(1795000199, true).remaining != -1 ||
      counts.codexAt(0, false).remaining != -1 || counts.waiting != 2 ||
      std::string_view(counts.lastDelivery) != "19:32") return false;
  if (!parseHomeDashboard("h3,-1,0,-,0,1795000200", counts) ||
      counts.codexAt(1795000100, false).remaining != 0) return false;
  return !parseHomeDashboard("h3,-1,0,-,101,1795000200", counts) &&
         !parseHomeDashboard("h3,-1,0,-,29,garbage", counts) &&
         parseHomeDashboard("h2,17,0,-", counts) && counts.codexAt(1795000100, false).remaining == -1;
}
static_assert(readsFreshCodexQuota());

constexpr bool selectsCodexWindowAndShowsTimeUntilReset() {
  HomeDeliveryCounts counts;
  if (!parseHomeDashboard("h4,17,2,19:32,1795000900,32,1795003600,29,1795259200", counts)) return false;
  if (counts.codexAt(1795000000, false) != HomeCodexDisplay{32, 5, 1} ||
      counts.codexAt(1795000000, true) != HomeCodexDisplay{29, 7, 3} ||
      counts.codexAt(1795003600, false) != HomeCodexDisplay{29, 7, 3} ||
      counts.codexAt(1795172801, true) != HomeCodexDisplay{29, 7, 1} ||
      counts.codexAt(1795259200, true) != HomeCodexDisplay{}) return false;
  if (!parseHomeDashboard("h4,17,0,-,1795000900,100,0,-1,0", counts) ||
      counts.codexAt(1795000000, false) != HomeCodexDisplay{100, 0, 0} ||
      counts.codexAt(1795000900, false) != HomeCodexDisplay{} ||
      counts.codexAt(1795000000, true) != HomeCodexDisplay{}) return false;
  return !parseHomeDashboard("h4,17,0,-,1795000900,-1,1795003600,29,0", counts) &&
         !parseHomeDashboard("h4,17,0,-,1795000900,32,0,29,9999999999", counts);
}
static_assert(selectsCodexWindowAndShowsTimeUntilReset());

constexpr bool boundsUnsuccessfulRefreshes() {
  HomeDeliveryRefresh refresh;
  refresh.attempted(123);
  return !refresh.needsRoutineRefresh(120122) && refresh.needsRoutineRefresh(120123);
}
static_assert(boundsUnsuccessfulRefreshes());
