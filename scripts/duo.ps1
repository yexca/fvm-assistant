<#
.SYNOPSIS
    Coordinates two FvmAssistant off-screen instances in one room.

.EXAMPLE
    .\scripts\duo.ps1 -HostPort 18765 -GuestPort 18766 -HostTrack '我的轨道\host.txt' `
        -GuestTrack '我的轨道\guest.txt' -HostCards '小火炉:日光炉' `
        -GuestCards '海星:红柳烤串机' -Rounds 3

.EXAMPLE
    .\scripts\duo.ps1 -HostPort 18765 -GuestPort 18766 -Level '美味/曲奇' `
        -HostTrack '我的轨道\host.txt' -GuestTrack '我的轨道\guest.txt' `
        -HostCards '小火炉:日光炉' -GuestCards '海星:红柳烤串机' -Rounds 3
#>
param(
    [Parameter(Mandatory)][ValidateRange(1024, 65535)][int]$HostPort,
    [Parameter(Mandatory)][ValidateRange(1024, 65535)][int]$GuestPort,
    [Parameter(Mandatory)][string]$HostTrack,
    [Parameter(Mandatory)][string]$GuestTrack,
    [Parameter(Mandatory)][string]$HostCards,
    [Parameter(Mandatory)][string]$GuestCards,
    [string]$Level,
    [ValidateRange(1, 500)][int]$Rounds = 1,
    [switch]$HostShovel,
    [switch]$GuestShovel,
    [switch]$HostAutoFlame,
    [switch]$GuestAutoFlame
)

$ErrorActionPreference = 'Stop'
if ($HostPort -eq $GuestPort) { throw 'Use different control ports for the two accounts.' }

function Invoke-Duo([int]$Port, [string]$Route, [hashtable]$Query = @{}) {
    $pairs = foreach ($key in ($Query.Keys | Sort-Object)) {
        [uri]::EscapeDataString([string]$key) + '=' + [uri]::EscapeDataString([string]$Query[$key])
    }
    $uri = "http://127.0.0.1:$Port$Route"
    if ($pairs.Count -gt 0) { $uri += '?' + ($pairs -join '&') }
    try {
        Invoke-RestMethod -Uri $uri -Headers @{ 'X-Fvm-Assistant' = '1' } -TimeoutSec 20
    }
    catch {
        $status = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 'connection error' }
        throw "Duo request $Route on port $Port failed ($status). Inspect the local instance log."
    }
}

function Read-Duo {
    $a = Invoke-Duo $HostPort '/duo'
    $b = Invoke-Duo $GuestPort '/duo'
    if ($a.run -ne $hostRun -or $b.run -ne $guestRun) { throw 'A duo instance changed runs.' }
    foreach ($item in @($a, $b)) {
        if ($item.state -eq 'failed' -or $item.state -eq 'stopped') {
            throw "Account $($item.profile) ended: $($item.state). Inspect its local run directory."
        }
    }
    return @($a, $b)
}

function Wait-Duo([scriptblock]$Condition, [int]$Seconds, [string]$Description) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $states = Read-Duo
        if (& $Condition $states) { return $states }
        Start-Sleep -Milliseconds 700
    }
    throw "Timed out waiting for $Description."
}

$hostStarted = $false
$guestStarted = $false
$completed = $false
try {
    $hostStatus = Invoke-Duo $HostPort '/status'
    $guestStatus = Invoke-Duo $GuestPort '/status'
    if ($hostStatus.profile -eq $guestStatus.profile) { throw 'The two instances must use different profiles.' }
    if ($hostStatus.mode -ne 'Offscreen' -or $guestStatus.mode -ne 'Offscreen') {
        throw 'Both instances must use --mode osr.'
    }
    if ((Invoke-Duo $HostPort '/duo').running -or (Invoke-Duo $GuestPort '/duo').running) {
        throw 'Stop the existing duo run before starting a new one.'
    }
    $hostPlanQuery = @{
        state='plan'; track=$HostTrack; cards=$HostCards; shovel=[int]$HostShovel.IsPresent
    }
    $guestPlanQuery = @{
        state='plan'; track=$GuestTrack; cards=$GuestCards; shovel=[int]$GuestShovel.IsPresent
    }
    if ($Level) { $hostPlanQuery.level = $Level; $guestPlanQuery.level = $Level }
    $hostPlan = Invoke-Duo $HostPort '/duo' $hostPlanQuery
    $guestPlan = Invoke-Duo $GuestPort '/duo' $guestPlanQuery
    if ($hostPlan.player -eq $guestPlan.player) { throw 'Both tracks place the character on the same cell.' }
    $overlap = @($hostPlan.cells | Where-Object { $guestPlan.cells -contains $_ })
    if ($overlap.Count -gt 0) { throw "Both tracks place cards on: $($overlap -join ', ')." }
    if ($hostPlan.cells -contains $guestPlan.player -or $guestPlan.cells -contains $hostPlan.player) {
        throw 'One track places a card on the other character.'
    }

    $hostQuery = @{
        state='start'; role='host'; rounds=$Rounds; track=$HostTrack; cards=$HostCards
        shovel=[int]$HostShovel.IsPresent; autoflame=[int]$HostAutoFlame.IsPresent
    }
    if ($Level) {
        $hostQuery.level = $Level
        $temporaryPassword = (Get-Random -Minimum 1000 -Maximum 10000).ToString()
        $hostQuery.password = $temporaryPassword
    }
    $hostStarted = $true
    $hostSession = Invoke-Duo $HostPort '/duo' $hostQuery

    $hostRoom = 0
    if ($Level) {
        $hostRun = $hostSession.run
        $deadline = [DateTime]::UtcNow.AddSeconds(120)
        while ([DateTime]::UtcNow -lt $deadline) {
            $hostProgress = Invoke-Duo $HostPort '/duo'
            if ($hostProgress.run -ne $hostRun -or -not $hostProgress.running -or
                $hostProgress.state -eq 'failed' -or $hostProgress.state -eq 'stopped') {
                throw 'The host could not create a verified duo room.'
            }
            if ($hostProgress.room -gt 0) { $hostRoom = [int]$hostProgress.room; break }
            Start-Sleep -Milliseconds 700
        }
        if ($hostRoom -le 0) { throw 'Timed out waiting for the host room number.' }
    }

    $guestQuery = @{
        state='start'; role='guest'; rounds=$Rounds; track=$GuestTrack; cards=$GuestCards
        shovel=[int]$GuestShovel.IsPresent; autoflame=[int]$GuestAutoFlame.IsPresent
    }
    if ($Level) {
        $guestQuery.level = $Level
        $guestQuery.password = $temporaryPassword
        $guestQuery.room = $hostRoom
    }
    $guestStarted = $true
    $guestSession = Invoke-Duo $GuestPort '/duo' $guestQuery
    if (-not $hostSession.run -or -not $guestSession.run -or
        $hostSession.role -ne 'host' -or $guestSession.role -ne 'guest') {
        throw 'The two instances did not accept their assigned duo roles.'
    }
    $hostRun = $hostSession.run
    $guestRun = $guestSession.run
    Write-Host "Paired profiles: $($hostSession.profile) (host), $($guestSession.profile) (guest)."

    for ($round = 1; $round -le $Rounds; $round++) {
        $ready = Wait-Duo { param($s) $s[0].state -eq 'duo-room-ready' -and
            $s[1].state -eq 'duo-room-ready' -and $s[0].round -eq $round -and
            $s[1].round -eq $round } 120 "both room decks in round $round"
        if ($Level -and ($ready[0].room -ne $hostRoom -or $ready[1].room -ne $hostRoom)) {
            throw "The two accounts did not verify the same room in round $round."
        }

        [void](Invoke-Duo $GuestPort '/duo' @{ state='release'; phase='room'; run=$guestRun; round=$round })
        [void](Wait-Duo { param($s) $s[1].state -eq 'duo-awaiting-battle' } 15 "guest Ready in round $round")
        [void](Invoke-Duo $HostPort '/duo' @{ state='release'; phase='room'; run=$hostRun; round=$round })

        [void](Wait-Duo { param($s) $s[0].state -eq 'duo-battle-ready' -and
            $s[1].state -eq 'duo-battle-ready' } 45 "both opening frames in round $round")
        [void](Invoke-Duo $GuestPort '/duo' @{ state='release'; phase='battle'; run=$guestRun; round=$round })
        [void](Invoke-Duo $HostPort '/duo' @{ state='release'; phase='battle'; run=$hostRun; round=$round })

        [void](Wait-Duo { param($s) $s[0].roundsDone -ge $round -and
            $s[1].roundsDone -ge $round } 1200 "both settlements in round $round")
        Write-Host "Round $round/$Rounds completed by both accounts."
    }
    $completed = $true
}
finally {
    if (-not $completed) {
        if ($hostStarted) { try { [void](Invoke-Duo $HostPort '/duo' @{ state='stop' }) } catch { Write-Warning "Could not stop host: $_" } }
        if ($guestStarted) { try { [void](Invoke-Duo $GuestPort '/duo' @{ state='stop' }) } catch { Write-Warning "Could not stop guest: $_" } }
    }
}
