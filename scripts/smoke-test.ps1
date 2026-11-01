<#
.SYNOPSIS
  Smoke test for a running Unified Service Scheduler API (for example after `docker compose up -d`).

.EXAMPLE
  .\scripts\smoke-test.ps1
  .\scripts\smoke-test.ps1 -BaseUrl http://localhost:5080 -Date 2030-01-15

.NOTES
  Works in Windows PowerShell 5.1 and PowerShell 7+. No extra modules needed.
  If scripts are blocked: powershell -ExecutionPolicy Bypass -File .\scripts\smoke-test.ps1
  Prints PASS/FAIL per step; exit code 1 if any step fails. It cancels what it books, so it can be run again.
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = 'http://localhost:5080',
    # Dealership-local booking date (yyyy-MM-dd). Default: today + 7 days.
    [string] $Date = (Get-Date).ToUniversalTime().AddDays(7).ToString('yyyy-MM-dd'),
    [int] $CustomerId = 1,
    [int] $MaxExtraBookings = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:Failures = 0

function Pass([string] $Message) { Write-Host "PASS  $Message" -ForegroundColor Green }
function Fail([string] $Message) { Write-Host "FAIL  $Message" -ForegroundColor Red; $script:Failures++ }

function ConvertFrom-JsonSafe([string] $Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    try { return $Text | ConvertFrom-Json } catch { return $null }
}

# PowerShell 7 turns ISO date strings into [DateTime]; send them back as UTC ISO-8601 with "Z".
function Format-Utc($Value) {
    if ($Value -is [DateTime]) { return $Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") }
    return [string] $Value
}

# Returns @{ Status = <int>; Json = <object or $null>; Text = <string> }. 4xx/5xx do not throw.
function Invoke-Api([string] $Method, [string] $Path, $Body = $null, [string] $IdempotencyKey = $null) {
    $headers = @{ Accept = 'application/json' }
    if ($IdempotencyKey) { $headers['Idempotency-Key'] = $IdempotencyKey }
    $params = @{ Uri = "$BaseUrl$Path"; Method = $Method; Headers = $headers; UseBasicParsing = $true; TimeoutSec = 30 }
    if ($null -ne $Body) {
        $params.Body = ($Body | ConvertTo-Json -Compress)
        $params.ContentType = 'application/json'
    }
    try {
        $response = Invoke-WebRequest @params
        $text = [string] $response.Content
        return @{ Status = [int] $response.StatusCode; Json = (ConvertFrom-JsonSafe $text); Text = $text }
    }
    catch {
        $httpResponse = $null
        if ($_.Exception.PSObject.Properties['Response']) { $httpResponse = $_.Exception.Response }
        if ($null -eq $httpResponse) {
            return @{ Status = 0; Json = $null; Text = $_.Exception.Message }   # not reachable
        }
        $text = if ($_.ErrorDetails -and $_.ErrorDetails.Message) { $_.ErrorDetails.Message } else { '' }
        return @{ Status = [int] $httpResponse.StatusCode; Json = (ConvertFrom-JsonSafe $text); Text = $text }
    }
}

function Get-Prop($Object, [string] $Name) {
    if ($null -ne $Object -and $Object.PSObject.Properties[$Name]) { return $Object.$Name }
    return $null
}

Write-Host "Smoke test against $BaseUrl (booking date $Date)"

# 1. Health
$r = Invoke-Api GET '/health/ready'
if ($r.Status -eq 200) { Pass 'health/ready is 200' } else { Fail "health/ready returned $($r.Status)" }

# 2. Dealerships
$r = Invoke-Api GET '/api/dealerships'
$dealerships = @($r.Json)
if ($r.Status -ne 200 -or $dealerships.Count -eq 0 -or $null -eq $dealerships[0]) {
    Fail "list dealerships returned $($r.Status)"; Write-Host 'Cannot continue.'; exit 1
}
$dealershipId = $dealerships[0].id
Pass "list dealerships ($($dealerships.Count) found, using id $dealershipId)"

# Service type and vehicle for the booking.
$serviceTypes = @((Invoke-Api GET "/api/service-types?dealershipId=$dealershipId").Json)
$vehicles = @((Invoke-Api GET "/api/customers/$CustomerId/vehicles").Json)
if ($serviceTypes.Count -eq 0 -or $null -eq $serviceTypes[0] -or $vehicles.Count -eq 0 -or $null -eq $vehicles[0]) {
    Fail 'load service type / vehicle'; exit 1
}
$serviceTypeId = $serviceTypes[0].id
$vehicleId = $vehicles[0].id

# 3. Availability
$r = Invoke-Api GET "/api/dealerships/$dealershipId/availability?serviceTypeId=$serviceTypeId&date=$Date"
$slots = @(Get-Prop $r.Json 'slots')
if ($r.Status -ne 200 -or $slots.Count -eq 0 -or $null -eq $slots[0]) {
    Fail "availability returned $($r.Status) with no free slot"; exit 1
}
$start = Format-Utc $slots[0].startUtc
Pass "availability ($($slots.Count) free start times, using $start)"

$request = [ordered]@{
    customerId    = $CustomerId
    vehicleId     = $vehicleId
    dealershipId  = $dealershipId
    serviceTypeId = $serviceTypeId
    startTime     = $start
}

# 4. Book
$key = [guid]::NewGuid().ToString()
$r = Invoke-Api POST '/api/appointments' $request $key
$appointmentId = Get-Prop $r.Json 'id'
if ($r.Status -ne 201 -or $null -eq $appointmentId) { Fail "book returned $($r.Status): $($r.Text)"; exit 1 }
Pass "book (201, appointment $appointmentId, technician $($r.Json.technician.name), bay $($r.Json.serviceBay.name))"

# 5. Replay the same key + same body -> same appointment
$r = Invoke-Api POST '/api/appointments' $request $key
$replayId = Get-Prop $r.Json 'id'
if ($r.Status -eq 201 -and $replayId -eq $appointmentId) {
    Pass "replay same Idempotency-Key (201, same appointment $replayId)"
} else {
    Fail "replay returned $($r.Status) with id '$replayId' (expected 201 and $appointmentId)"
}

# 6. Same slot, new keys -> 409 NO_AVAILABILITY once every technician/bay pair is taken
$extraIds = @()
$r = $null
for ($i = 1; $i -le $MaxExtraBookings; $i++) {
    $r = Invoke-Api POST '/api/appointments' $request ([guid]::NewGuid().ToString())
    if ($r.Status -eq 201) { $extraIds += $r.Json.id } else { break }
}
$code = Get-Prop $r.Json 'code'
if ($r.Status -eq 409 -and $code -eq 'NO_AVAILABILITY') {
    $suggested = (@(Get-Prop $r.Json 'suggestedStartTimes') | ForEach-Object { Format-Utc $_ }) -join ', '
    Pass "same slot with a new key is 409 NO_AVAILABILITY (after $($extraIds.Count) extra booking(s); suggested: $suggested)"
} else {
    Fail "same slot with a new key returned $($r.Status) code '$code' (expected 409 NO_AVAILABILITY)"
}

# 7. Cancel
$r = Invoke-Api POST "/api/appointments/$appointmentId/cancel"
if ($r.Status -eq 200 -and (Get-Prop $r.Json 'status') -eq 'Cancelled') {
    Pass "cancel appointment $appointmentId (200, Cancelled)"
} else {
    Fail "cancel returned $($r.Status): $($r.Text)"
}

# Clean up the extra bookings from step 6.
foreach ($id in $extraIds) {
    $r = Invoke-Api POST "/api/appointments/$id/cancel"
    if ($r.Status -ne 200) { Fail "clean-up cancel of appointment $id returned $($r.Status)" }
}

if ($script:Failures -eq 0) { Write-Host 'ALL PASSED'; exit 0 }
Write-Host "$($script:Failures) step(s) FAILED"
exit 1
