#pragma once
#include <Arduino.h>

bool handleUsbSyncLine(const String& line, bool idle);
bool recordingSyncAllowed();
bool usbSyncActive();
bool usbSyncWorking();
void cancelUsbSync();
bool takeSyncHomeChanged();

using BluetoothSyncReply = void (*)(const String&);
bool handleBluetoothSyncLine(const String& line, const String& peer, BluetoothSyncReply send);
bool bluetoothSyncActive();
bool takeBluetoothSyncDrained();
void cancelBluetoothSync();
