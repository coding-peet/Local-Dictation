#pragma once

#include <cstdint>

bool startBluetoothSync();
bool startBluetoothHomeDelivery();
bool bluetoothHomeDeliveryOpen();
bool bluetoothEnrollmentAvailable();
bool startBluetoothEnrollment();
bool bluetoothEnrollmentOpen();
bool bluetoothEnrollmentPrompt(uint32_t& code);
void bluetoothEnrollmentConfirm(bool accept);
bool pollBluetoothSync();
void stopBluetoothSync();
