<#
.SYNOPSIS
    Pair two already logged-in friends by the visible island, area, and room invitation UI.
.DESCRIPTION
    Both accounts must be at a lobby map on the same server. FriendRow is the
    guest's row on the host's in-room Friends invitation list (one-based).
#>
param(
    [Parameter(Mandatory)][int]$HostPort,
    [Parameter(Mandatory)][int]$GuestPort,
    [Parameter(Mandatory)][string]$Level,
    [ValidateRange(1, 11)][int]$Area = 11,
    [ValidateRange(1, 10)][int]$FriendRow = 1
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$headers = @{ 'X-Fvm-Assistant' = '1' }

function Resolve-AreaMenuRow([int]$Number, [int]$MenuRows) {
    if ($Number -lt 1 -or $Number -gt 11) { throw 'Area must be between 1 and 11.' }
    if ($MenuRows -eq 11) { return $Number }
    if ($MenuRows -eq 2) {
        switch ($Number) {
            1 { return 1 }
            11 { return 2 }
        }
    }
    throw 'The requested area is not supported by the observed menu. Inspect its layout manually.'
}

function Get-AreaMenuRowCount([System.Drawing.Bitmap]$Image) {
    if ($Image.Width -lt 696 -or $Image.Height -lt 342) { throw 'The area menu capture is too small.' }
    $rows = 0
    for ($index = 0; $index -lt 11; $index++) {
        $yellow = 0
        for ($pixelY = 77 + 25 * $index; $pixelY -lt 92 + 25 * $index; $pixelY++) {
            for ($pixelX = 681; $pixelX -lt 696; $pixelX++) {
                $color = $Image.GetPixel($pixelX, $pixelY)
                if ($color.R -gt 180 -and $color.G -gt 120 -and $color.B -lt 100) { $yellow++ }
            }
        }
        if ($yellow -lt 20) { break }
        $rows++
    }
    if ($rows -ne 2 -and $rows -ne 11) { throw 'The area menu does not match a verified two-row or eleven-row layout.' }
    return $rows
}

function Request([int]$Port, [string]$Route) {
    Invoke-RestMethod -Uri "http://127.0.0.1:$Port$Route" -Headers $headers -TimeoutSec 10
}

function Screen([int]$Port) { (Request $Port '/detect').screen }

function Click([int]$Port, [int]$X, [int]$Y) {
    [void](Request $Port "/click?x=$X&y=$Y&space=flash&method=cef")
}

function Capture([int]$Port) {
    (Request $Port '/capture?method=osr&crop=flash&label=friend-pair').path
}

function Wait-Screen([int]$Port, [string]$Expected, [int]$Seconds) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ((Screen $Port) -eq $Expected) { return }
        Start-Sleep -Milliseconds 250
    }
    throw "Port $Port did not reach $Expected within $Seconds seconds."
}

function Region-Diff([string]$First, [string]$Second, [int]$Left, [int]$Top, [int]$Width, [int]$Height) {
    $a = [System.Drawing.Bitmap]::new($First)
    $b = [System.Drawing.Bitmap]::new($Second)
    try {
        [long]$sum = 0
        for ($py = $Top; $py -lt $Top + $Height; $py += 3) {
            for ($px = $Left; $px -lt $Left + $Width; $px += 3) {
                $ca = $a.GetPixel($px, $py)
                $cb = $b.GetPixel($px, $py)
                $sum += [math]::Abs([int]$ca.R - [int]$cb.R) +
                    [math]::Abs([int]$ca.G - [int]$cb.G) +
                    [math]::Abs([int]$ca.B - [int]$cb.B)
            }
        }
        return $sum / (3.0 * [math]::Ceiling($Width / 3.0) * [math]::Ceiling($Height / 3.0))
    }
    finally { $a.Dispose(); $b.Dispose() }
}

$parts = $Level -split '[/\\]'
if ($parts.Count -ne 2) { throw 'Level must be 岛/关卡, for example 美味/神殿.' }
$island = $parts[0].Trim()
$stage = $parts[1].Trim()
$catalog = Join-Path $PSScriptRoot '../vendor/track/附加程序/文本/普通关卡.txt'
$islandPoint = $null
$stagePoint = $null
$currentIsland = ''
foreach ($line in (Get-Content -LiteralPath $catalog -Encoding UTF8)) {
    $fields = $line.Split("`t")
    if ($fields.Count -lt 3) { continue }
    if ($fields[0].StartsWith('##')) {
        $currentIsland = $fields[0].Substring(2).Trim()
        if ($currentIsland -eq $island) { $islandPoint = @([int]$fields[1], [int]$fields[2]) }
    }
    elseif ($currentIsland -eq $island -and $fields[0].Trim() -eq $stage) {
        $stagePoint = @([int]$fields[1], [int]$fields[2])
    }
}
if ($null -eq $islandPoint -or $null -eq $stagePoint) { throw "Stage $Level was not found in 普通关卡.txt." }

foreach ($port in @($HostPort, $GuestPort)) {
    $status = Request $port '/status'
    if ($status.mode -ne 'Offscreen') { throw "Port $port must run in off-screen mode." }
    for ($attempt = 0; $attempt -lt 8; $attempt++) {
        $screen = Screen $port
        if ($screen -eq 'HealthNotice') { Click $port 425 369 }
        elseif ($screen -eq 'EventPopup') { Click $port 922 112 }
        else { break }
        Start-Sleep -Milliseconds 500
    }
    if ((Screen $port) -ne 'Lobby') { throw "Port $port must be at a lobby map before friend pairing." }
    Click $port 866 41
    Wait-Screen $port 'WorldMap' 15
    Click $port $islandPoint[0] $islandPoint[1]
    Wait-Screen $port 'Lobby' 15
    Click $port 820 83
    Start-Sleep -Milliseconds 350
    $menuImage = [System.Drawing.Bitmap]::new((Capture $port))
    try { $areaMenuRow = Resolve-AreaMenuRow $Area (Get-AreaMenuRowCount $menuImage) }
    finally { $menuImage.Dispose() }
    Click $port 736 (84 + 25 * ($areaMenuRow - 1))
    Start-Sleep -Milliseconds 500
    if ((Screen $port) -ne 'Lobby') { throw "Port $port did not return to the island map after selecting an area." }
}

$hostArea = Capture $HostPort
$guestArea = Capture $GuestPort
if ((Region-Diff $hostArea $guestArea 838 75 55 18) -gt 8) {
    throw 'The two accounts do not show the same island area label.'
}
Write-Host "Both accounts reached $island area $Area."

$guestBefore = $guestArea
Click $HostPort $stagePoint[0] $stagePoint[1]
Wait-Screen $HostPort 'RoomList' 15
# Room lists with side tabs, including 神殿, sit 20 pixels to the right.
$offset = if ($stage -eq '神殿') { 20 } else { 0 }
Click $HostPort (587 + $offset) 490
Wait-Screen $HostPort 'Room' 15
Click $HostPort 408 547
Start-Sleep -Milliseconds 350
Click $HostPort 529 130
Start-Sleep -Milliseconds 350
Click $HostPort 601 (154 + 26 * ($FriendRow - 1))

$deadline = [DateTime]::UtcNow.AddSeconds(11)
$promptSeen = $false
while ([DateTime]::UtcNow -lt $deadline) {
    $guestNow = Capture $GuestPort
    if ((Region-Diff $guestBefore $guestNow 356 203 346 225) -gt 24) {
        $promptSeen = $true
        break
    }
    Start-Sleep -Milliseconds 200
}
if (-not $promptSeen) { throw 'The guest did not receive an invitation within the response window. Check the selected friend row.' }
Click $GuestPort 449 403
Wait-Screen $GuestPort 'Room' 10
Click $HostPort 588 490
Start-Sleep -Milliseconds 400
if ((Screen $HostPort) -ne 'Room') { throw 'The host left the room unexpectedly.' }
$hostRoom = Capture $HostPort
$guestRoom = Capture $GuestPort
if ((Region-Diff $hostRoom $guestRoom 121 20 210 24) -gt 15) {
    throw 'The room headers differ after the invitation; do not start the duo run.'
}
Write-Host 'The guest accepted the invitation and both accounts show the same room header.'
