[CmdletBinding()]
param(
    [ValidatePattern('^COM\d+$')][string]$Port = 'COM24',
    [ValidateSet('Outlook', 'Demo', 'Clear')][string]$Mode = 'Outlook',
    [switch]$ShowTitle,
    [switch]$DeviceCode,
    [switch]$Once
)

$ErrorActionPreference = 'Stop'
$utf8 = [Text.UTF8Encoding]::new($false)
$graphScope = 'Calendars.ReadBasic'
$calendarDays = 30

function Wait-DeskResponse {
    param([IO.Ports.SerialPort]$Serial, [string]$Expected)
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(5)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try {
            if ($Serial.ReadLine().Trim() -match $Expected) { return }
        } catch [TimeoutException] {}
    }
    throw 'Desk Gadget did not respond on the selected COM port.'
}

function Send-DeskMeeting {
    param([long]$Start, [string]$Title, [bool]$Demo)
    $serial = [IO.Ports.SerialPort]::new($Port, 115200)
    $serial.DtrEnable = $true
    $serial.ReadTimeout = 300
    $serial.WriteTimeout = 2000
    $serial.Encoding = $utf8
    try {
        $serial.Open()
        $serial.Write("`n?")
        Wait-DeskResponse $serial '^Desk gadget 0\.[45](?:\.\d+)? \|'
        $payload = @{start = $Start; title = $Title; demo = $Demo} | ConvertTo-Json -Compress
        $serial.WriteLine('@meeting ' + $payload)
        Wait-DeskResponse $serial '^@meeting-ok$'
    } finally {
        $serial.Dispose()
    }
}

function Get-NextMeeting {
    $now = [DateTimeOffset]::UtcNow
    $until = $now.AddDays($calendarDays)
    $fields = 'start,isAllDay,isCancelled,responseStatus'
    if ($ShowTitle) { $fields += ',subject,sensitivity' }
    $query = 'https://graph.microsoft.com/v1.0/me/calendarView?startDateTime={0}&endDateTime={1}&$select={2}&$top=100' -f `
        [Uri]::EscapeDataString($now.ToString('o')), [Uri]::EscapeDataString($until.ToString('o')), $fields
    $headers = @{Prefer = 'outlook.timezone="UTC"'}
    $culture = [Globalization.CultureInfo]::InvariantCulture
    $style = [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal
    $selectedStart = $until
    $selectedTitle = ''
    while ($query) {
        $uri = [Uri]$query
        if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'graph.microsoft.com' -or !$uri.IsDefaultPort -or $uri.UserInfo) {
            throw 'Microsoft Graph returned an unexpected continuation URL.'
        }
        $result = Invoke-MgGraphRequest -Method GET -Uri $query -Headers $headers -OutputType Hashtable
        foreach ($event in $result.value) {
            if ($event.isCancelled -or $event.isAllDay -or $event.responseStatus.response -eq 'declined') { continue }
            $start = [DateTimeOffset]::MinValue
            if (![DateTimeOffset]::TryParse([string]$event.start.dateTime, $culture, $style, [ref]$start)) { continue }
            if ($start -le $now -or $start -ge $selectedStart) { continue }
            $selectedStart = $start
            $selectedTitle = 'Sch' + [char]0x00f4 + 'dzka'
            if ($ShowTitle -and $event.sensitivity -eq 'normal' -and $event.subject) {
                $title = ([string]$event.subject -replace '[\x00-\x1f\x7f]', ' ').Trim()
                while ($utf8.GetByteCount($title) -gt 170) { $title = $title.Substring(0, $title.Length - 1) }
                if ($title -and [char]::IsHighSurrogate($title[$title.Length - 1])) { $title = $title.Substring(0, $title.Length - 1) }
                if ($title) { $selectedTitle = $title }
            }
        }
        $query = [string]$result['@odata.nextLink']
    }
    if (!$selectedTitle) { return @{Start = 0L; Title = ''} }
    return @{Start = $selectedStart.ToUnixTimeSeconds(); Title = $selectedTitle}
}

if ($Mode -eq 'Demo') {
    Send-DeskMeeting ([DateTimeOffset]::UtcNow.AddMinutes(12).ToUnixTimeSeconds()) 'TEST Outlook' $true
    Write-Output 'Desk Gadget accepted a synthetic event starting in 12 minutes.'
    return
}
if ($Mode -eq 'Clear') {
    Send-DeskMeeting 0 '' $true
    Write-Output 'Desk Gadget cleared its test event.'
    return
}

$manifest = Join-Path $env:LOCALAPPDATA 'PalaDesk\Modules\Microsoft.Graph.Authentication\2.41.2\Microsoft.Graph.Authentication.psd1'
if (Test-Path -LiteralPath $manifest) { Import-Module -Name $manifest }
else { Import-Module -Name Microsoft.Graph.Authentication }

$connect = @{Scopes = @($graphScope); ContextScope = 'Process'; NoWelcome = $true}
if ($DeviceCode) { $connect.UseDeviceCode = $true }
Connect-MgGraph @connect | Out-Null
$context = Get-MgContext
if (!$context -or $context.Scopes -notcontains $graphScope) { throw 'Authorize Calendars.ReadBasic for the intended Microsoft account.' }
Write-Host 'Reading the default calendar for the chosen Microsoft account. Ctrl+C stops the bridge.'

do {
    try {
        $appointment = Get-NextMeeting
        Send-DeskMeeting $appointment.Start $appointment.Title $false
        if ($Once) { Write-Output 'Desk Gadget accepted the current calendar status.' }
    } catch {
        if ($Once) { throw }
        # Keep event content and OAuth material out of logs.
        Write-Warning 'Calendar or USB update failed. The bridge will retry in one minute.'
    }
    if (!$Once) { Start-Sleep -Seconds 60 }
} while (!$Once)
