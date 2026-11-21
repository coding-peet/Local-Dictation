#pragma once
#include <Arduino.h>

struct HomeDeliveryCounts {
  int deliveredToday = -1;
  int waiting = -1;
};

HomeDeliveryCounts homeDeliveryCounts(const String& today);
bool storeHomeDelivery(const String& payload, bool& changed);
bool homeDeliveryNeedsRefresh(uint32_t now);
bool homeDeliveryNeedsFollowup(uint32_t now);
void refreshHomeDeliveryAfterSubmission();
bool refreshHomeDelivery();
