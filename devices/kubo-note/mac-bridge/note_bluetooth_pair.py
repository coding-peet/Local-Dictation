"""Windows LE enrollment that accepts only user-confirmed Numeric Comparison."""
import asyncio
import re
import time

from note_settings import SettingsError


async def windows_numeric_pair(device, job):
    from winrt.windows.devices.bluetooth import BluetoothLEDevice
    from winrt.windows.devices.enumeration import (
        DeviceInformation, DevicePairingKinds, DevicePairingProtectionLevel, DevicePairingResultStatus,
    )

    address = device.address.replace(":", "")
    if re.fullmatch(r"[0-9A-Fa-f]{12}", address) is None:
        raise SettingsError("Windows could not identify the Bluetooth Note.")
    native = await BluetoothLEDevice.from_bluetooth_address_async(int(address, 16))
    if native is None:
        raise SettingsError("Windows could not open the Bluetooth Note.")
    try:
        info = await DeviceInformation.create_from_id_async(native.device_information.id)
        if info.pairing.is_paired:
            raise SettingsError("Remove this exact Note in Windows Bluetooth settings and retry, or pair over USB.")
        if not info.pairing.can_pair:
            raise SettingsError("Windows could not start pairing with this Note.")
        loop = asyncio.get_running_loop()
        custom = info.pairing.custom
        protection = DevicePairingProtectionLevel.ENCRYPTION_AND_AUTHENTICATION
        ceremony = DevicePairingKinds.CONFIRM_PIN_MATCH

        async def verify(args, deferral, code):
            try:
                if job.show_code(code):
                    await asyncio.to_thread(job.decision.wait, max(0, job.deadline - time.monotonic()))
                    if job.accepted and job.pending():
                        args.accept()
            finally:
                deferral.complete()

        def pairing_requested(_sender, args):
            if args.pairing_kind != ceremony or re.fullmatch(r"[0-9]{6}", args.pin or "") is None:
                return
            asyncio.run_coroutine_threadsafe(verify(args, args.get_deferral(), args.pin), loop)

        token = custom.add_pairing_requested(pairing_requested)
        try:
            result = await custom.pair_with_protection_level_async(ceremony, protection)
        finally:
            custom.remove_pairing_requested(token)
        if (not job.accepted or not job.pending() or result.status != DevicePairingResultStatus.PAIRED
                or result.protection_level_used != protection):
            raise SettingsError("Bluetooth Numeric Comparison was not confirmed on both devices.")
    finally:
        native.close()
