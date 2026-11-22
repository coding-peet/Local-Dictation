#pragma once
#include <Arduino.h>

#include "home_dashboard.h"

HomeDeliveryCounts homeDeliveryCounts();
void homeDeliveryRefreshAttempted(uint32_t now);
bool storeHomeDelivery(const String& payload, bool& changed);
bool homeDeliveryNeedsRefresh(uint32_t now);
bool homeDeliveryNeedsFollowup(uint32_t now);
void refreshHomeDeliveryAfterSubmission();
bool refreshHomeDelivery();
