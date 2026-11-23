#pragma once
#include <Arduino.h>

bool handleUsbSyncLine(const String& line, bool idle);
bool recordingSyncAllowed();
bool usbSyncActive();
bool usbSyncWorking();
void cancelUsbSync();
bool takeSyncHomeChanged();

using BluetoothSyncReply = void (*)(const String&);
bool handleBluetoothSyncLine(const String& line, const String& peer, BluetoothSyncReply send,
                             bool deliveryOnly, bool audioSupported);
bool readBluetoothAudio(const String& peer, uint32_t offset, uint8_t* data, size_t length);
bool bluetoothSyncActive();
bool takeBluetoothSyncDrained();
bool takeBluetoothBacklogProgress(int& completed, int& total);
bool pollBluetoothOrganization();
void cancelBluetoothSync();
