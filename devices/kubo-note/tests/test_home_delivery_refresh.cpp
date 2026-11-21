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
         !refresh.needsRoutineRefresh(317999) && refresh.needsRoutineRefresh(318000);
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
