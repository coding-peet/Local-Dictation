[CmdletBinding()]
param([int]$Port=48125)
$ErrorActionPreference='Stop'
if ($Port -eq 8123) { throw 'This script cannot stop the existing production server.' }
$health=Invoke-RestMethod "http://127.0.0.1:$Port/health" -TimeoutSec 5
if ($health.service -ne 'local-dictation' -or $health.protocol_version -ne 1) {
    throw 'The selected port is not a verified Local Dictation session server.'
}
if ($health.busy) { throw 'The candidate server is busy. Stop/cancel its active session before retrying.' }
$listener=Get-NetTCPConnection -State Listen -LocalAddress 127.0.0.1 -LocalPort $Port
if ($listener.OwningProcess -ne $health.process_id) { throw 'Candidate listener/process identity mismatch.' }
$candidateProcess=Get-Process -Id $health.process_id
Stop-Process -Id $health.process_id
if(-not $candidateProcess.WaitForExit(10000)){throw "The candidate process has not exited yet."}
Write-Output "Stopped the idle candidate server on port $Port."
