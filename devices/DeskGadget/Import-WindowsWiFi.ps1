[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$ProfileName,
    [ValidatePattern('^COM\d+$')][string]$Port = 'COM24',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
$utf8 = [Text.UTF8Encoding]::new($false)

if (-not ('PalaDesk.WindowsWifi' -as [type])) {
    $nativeSource = @'
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Xml;

namespace PalaDesk {
    public static class WindowsWifi {
        private const uint ProfileGetPlaintextKey = 4;
        private const uint ProfileGroupPolicy = 1;
        private const uint ProfileNotFound = 1168;

        public sealed class PersonalProfile {
            public string Ssid;
            public string Key;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct InterfaceInfo {
            public Guid Id;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description;
            public uint State;
        }

        [DllImport("wlanapi.dll", ExactSpelling = true)]
        private static extern uint WlanOpenHandle(uint version, IntPtr reserved, out uint negotiatedVersion, out IntPtr client);

        [DllImport("wlanapi.dll", ExactSpelling = true)]
        private static extern uint WlanCloseHandle(IntPtr client, IntPtr reserved);

        [DllImport("wlanapi.dll", ExactSpelling = true)]
        private static extern uint WlanEnumInterfaces(IntPtr client, IntPtr reserved, out IntPtr list);

        [DllImport("wlanapi.dll", ExactSpelling = true)]
        private static extern void WlanFreeMemory(IntPtr memory);

        [DllImport("wlanapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern uint WlanGetProfile(IntPtr client, ref Guid id, string profileName, IntPtr reserved,
            out IntPtr xml, ref uint flags, out uint grantedAccess);

        private static XmlDocument ParsedProfile(IntPtr xml) {
            XmlDocument document = new XmlDocument { XmlResolver = null };
            using (var text = new StringReader(Marshal.PtrToStringUni(xml)))
            using (var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit })) {
                document.Load(reader);
            }
            return document;
        }

        private static string Value(XmlDocument document, string path) {
            XmlNamespaceManager ns = new XmlNamespaceManager(document.NameTable);
            ns.AddNamespace("w", "http://www.microsoft.com/networking/WLAN/profile/v1");
            XmlNode node = document.SelectSingleNode(path, ns);
            return node == null ? "" : node.InnerText;
        }

        private static string EligibleSsid(XmlDocument profile, uint flags) {
            if ((flags & ProfileGroupPolicy) != 0) {
                throw new InvalidOperationException("Windows group policy profiles cannot be imported.");
            }
            const string auth = "/w:WLANProfile/w:MSM/w:security/w:authEncryption";
            string authentication = Value(profile, auth + "/w:authentication");
            if (authentication != "WPA2PSK" && authentication != "WPA3SAE") {
                throw new InvalidOperationException("Only personal WPA2 or WPA3 profiles are supported.");
            }
            if (Value(profile, auth + "/w:useOneX") == "true") {
                throw new InvalidOperationException("Enterprise Wi-Fi credentials cannot be imported.");
            }
            string ssid = Value(profile, "/w:WLANProfile/w:SSIDConfig/w:SSID/w:name");
            if (String.IsNullOrEmpty(ssid)) {
                throw new InvalidOperationException("The selected Windows profile has no text SSID.");
            }
            return ssid;
        }

        public static PersonalProfile Read(string name, bool includeKey) {
            uint version;
            IntPtr client, list = IntPtr.Zero;
            uint result = WlanOpenHandle(2, IntPtr.Zero, out version, out client);
            if (result != 0) throw new Win32Exception((int)result, "Cannot open Windows Wi-Fi service.");
            try {
                result = WlanEnumInterfaces(client, IntPtr.Zero, out list);
                if (result != 0) throw new Win32Exception((int)result, "Cannot enumerate Wi-Fi interfaces.");
                int count = Marshal.ReadInt32(list);
                int size = Marshal.SizeOf(typeof(InterfaceInfo));
                for (int i = 0; i < count; i++) {
                    var info = (InterfaceInfo)Marshal.PtrToStructure(IntPtr.Add(list, 8 + i * size), typeof(InterfaceInfo));
                    uint flags = 0, access;
                    IntPtr xml = IntPtr.Zero;
                    result = WlanGetProfile(client, ref info.Id, name, IntPtr.Zero, out xml, ref flags, out access);
                    if (result == ProfileNotFound) continue;
                    if (result != 0) throw new Win32Exception((int)result, "Windows refused access to the selected profile.");
                    string ssid;
                    try { ssid = EligibleSsid(ParsedProfile(xml), flags); }
                    finally { WlanFreeMemory(xml); }
                    if (!includeKey) return new PersonalProfile { Ssid = ssid };

                    flags = ProfileGetPlaintextKey;
                    xml = IntPtr.Zero;
                    result = WlanGetProfile(client, ref info.Id, name, IntPtr.Zero, out xml, ref flags, out access);
                    if (result != 0) throw new Win32Exception((int)result, "Windows did not release the selected Wi-Fi key.");
                    try {
                        XmlDocument profile = ParsedProfile(xml);
                        if (EligibleSsid(profile, flags) != ssid) throw new InvalidOperationException("The selected profile changed during import.");
                        const string shared = "/w:WLANProfile/w:MSM/w:security/w:sharedKey";
                        string key = Value(profile, shared + "/w:keyMaterial");
                        if (Value(profile, shared + "/w:protected") != "false" || String.IsNullOrEmpty(key)) {
                            throw new InvalidOperationException("Windows kept the Wi-Fi key encrypted for this process.");
                        }
                        return new PersonalProfile { Ssid = ssid, Key = key };
                    } finally { WlanFreeMemory(xml); }
                }
                throw new InvalidOperationException("The Windows Wi-Fi profile name was not found.");
            } finally {
                if (list != IntPtr.Zero) WlanFreeMemory(list);
                WlanCloseHandle(client, IntPtr.Zero);
            }
        }
    }
}
'@
    if ($PSVersionTable.PSEdition -eq 'Desktop') {
        Add-Type -TypeDefinition $nativeSource -ReferencedAssemblies 'System.dll','System.Xml.dll'
    } else {
        Add-Type -TypeDefinition $nativeSource
    }
}

function Read-DeskReply {
    param([IO.Ports.SerialPort]$Serial, [string]$Pattern)
    $until = [DateTimeOffset]::UtcNow.AddSeconds(8)
    while ([DateTimeOffset]::UtcNow -lt $until) {
        try {
            $line = $Serial.ReadLine().Trim()
            if ($line -match $Pattern) { return $line }
        } catch [TimeoutException] {}
    }
    throw 'Desk Gadget did not reply on this COM port.'
}

# Inspect only the named profile. -Check stops before requesting any key.
$profile = [PalaDesk.WindowsWifi]::Read($ProfileName, $false)
if ($Check) {
    Write-Output 'The selected personal Wi-Fi profile is eligible. No password was requested.'
    return
}

$serial = [IO.Ports.SerialPort]::new($Port, 115200)
$serial.DtrEnable = $true
$serial.ReadTimeout = 250
$serial.WriteTimeout = 2000
$serial.Encoding = $utf8
$secret = $null
$payload = $null
try {
    $serial.Open()
    $serial.Write("`n?")
    $null = Read-DeskReply $serial '^Desk gadget 0\.5(?:\.\d+)? \|'
    $serial.WriteLine('@wifi-status')
    if ((Read-DeskReply $serial '^@wifi-(ready|locked)$') -ne '@wifi-ready') {
        throw 'Hold BOOT on the gadget for three seconds, release it, and retry within one minute.'
    }

    $secret = [PalaDesk.WindowsWifi]::Read($ProfileName, $true)
    if ($secret.Ssid -cne $profile.Ssid -or $utf8.GetByteCount($secret.Ssid) -gt 32 -or
        ($secret.Key -cnotmatch '^[\x20-\x7e]{8,63}$' -and $secret.Key -cnotmatch '^[a-fA-F0-9]{64}$')) {
        throw 'The selected profile uses an unsupported Wi-Fi name or key format.'
    }
    $payload = '@wifi ' + (@{ssid = $secret.Ssid; key = $secret.Key} | ConvertTo-Json -Compress)
    $serial.WriteLine($payload)
    switch (Read-DeskReply $serial '^@wifi-(saved-sd|saved|invalid|full|storage-error|locked)$') {
        '@wifi-saved-sd' { Write-Output 'Wi-Fi profile saved on Desk Gadget and backed up to encrypted SD storage.' }
        '@wifi-saved' { Write-Output 'Wi-Fi profile saved on Desk Gadget. The SD backup could not be updated.' }
        '@wifi-invalid' { throw 'Desk Gadget rejected the Wi-Fi profile format.' }
        '@wifi-full' { throw 'Desk Gadget already stores five Wi-Fi profiles.' }
        '@wifi-storage-error' { throw 'Desk Gadget could not save the Wi-Fi profile.' }
        '@wifi-locked' { throw 'The physical import window expired. Hold BOOT again and retry.' }
    }
} finally {
    if ($secret) { $secret.Key = $null }
    $payload = $null
    $serial.Dispose()
}
