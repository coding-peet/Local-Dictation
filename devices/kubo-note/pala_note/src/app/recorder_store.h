#pragma once

#include <Arduino.h>
#include "recorder_work.h"
#include "sync_queue.h"

void startRecorderWork();
bool scanRecorderWorkSlice();
bool recorderWorkReady();
bool reserveRecorderWork(int note, const String& utc);
bool registerRecorderWork(int note);
bool visitRecorderWork(bool newestFirst, const std::function<bool(const SyncQueueEntry&)>& visit);
SyncQueueCounts recorderWorkCounts();
bool recorderDeliveryPlan(int note, const String& proposed, String& selected);
recorder_work::Claim claimRecorderDelivery(int note, const String& digest);
bool finishRecorderDelivery(int note, const String& digest);
