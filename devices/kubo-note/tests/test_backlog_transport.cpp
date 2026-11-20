#include "../pala_note/src/app/backlog_transport.h"

static_assert(backlogLink(true, false, false, WifiBacklog::Unavailable) == BacklogLink::Usb);
static_assert(backlogLink(false, true, true, WifiBacklog::Unchecked) == BacklogLink::Usb);
static_assert(backlogLink(false, false, true, WifiBacklog::Unchecked) == BacklogLink::Wifi);
static_assert(backlogLink(false, true, false, WifiBacklog::Unchecked) == BacklogLink::Wifi);
static_assert(backlogLink(false, false, false, WifiBacklog::Ready) == BacklogLink::Wifi);
static_assert(backlogLink(false, false, false, WifiBacklog::Busy) == BacklogLink::Deferred);
static_assert(backlogLink(false, true, false, WifiBacklog::Unavailable) == BacklogLink::Bluetooth);
