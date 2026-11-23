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

constexpr bool followsBluetoothDeliveryAfterTheRecordingLinkCloses() {
  HomeDeliveryRefresh refresh;
  if (refresh.needsBluetoothFollowup(10000) || refresh.keepAwakeForBluetooth(10000)) return false;
  refresh.received(1000, 1);
  if (refresh.needsBluetoothFollowup(8999) || !refresh.needsBluetoothFollowup(9000)) return false;
  refresh.bluetoothAttempted(9000);
  refresh.received(9200, 1);
  if (refresh.needsBluetoothFollowup(20999) || !refresh.needsBluetoothFollowup(21000)) return false;
  if (!refresh.keepAwakeForBluetooth(45999) || refresh.keepAwakeForBluetooth(46000)) return false;
  refresh.received(24000, 0);
  return !refresh.needsBluetoothFollowup(90000) && !refresh.keepAwakeForBluetooth(24000);
}

constexpr bool backsOffAndRecoversFromAnOfflineBridge() {
  HomeDeliveryRefresh refresh;
  refresh.received(0xfffffff0u, 1);
  if (!refresh.needsBluetoothFollowup(7984)) return false;
  refresh.bluetoothAttempted(7984);
  if (refresh.needsBluetoothFollowup(19983) || !refresh.needsBluetoothFollowup(19984)) return false;
  refresh.bluetoothAttempted(39984);
  return !refresh.keepAwakeForBluetooth(44984) && !refresh.needsBluetoothFollowup(99983) &&
         refresh.needsBluetoothFollowup(99984);
}

static_assert(followsBluetoothDeliveryAfterTheRecordingLinkCloses());
static_assert(backsOffAndRecoversFromAnOfflineBridge());
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
