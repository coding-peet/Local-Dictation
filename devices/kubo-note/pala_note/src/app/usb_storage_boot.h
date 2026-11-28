#pragma once

// Recorder-side handoff into a dedicated boot that owns the SD controller.
void requestUsbStorageRestart();
bool usbStorageRestartPending();
void markImmediateUsbStorageBoot();
bool setNextUsbStorageBoot(bool enabled);
bool takeUsbStorageBoot();
