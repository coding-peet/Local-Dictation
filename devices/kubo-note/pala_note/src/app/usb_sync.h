#pragma once
#include <Arduino.h>

bool handleUsbSyncLine(const String& line, bool idle);
bool usbSyncActive();
bool usbSyncWorking();
void cancelUsbSync();
bool takeSyncHomeChanged();

using BluetoothSyncReply = void (*)(const String&);
bool handleBluetoothSyncLine(const String& line, BluetoothSyncReply send);
bool bluetoothSyncActive();
bool takeBluetoothSyncDrained();
void cancelBluetoothSync();
