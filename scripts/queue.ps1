<#
.SYNOPSIS
    Runs a validated sequence of ordinary FvmAssistant stage grinds through the loopback API.

.EXAMPLE
    .\scripts\queue.ps1 -Plan .\my-queue.json -Port 18765
#>
param(
    [Parameter(Mandatory)][string]$Plan,
    [ValidateRange(1024, 65535)][int]$Port = 18765,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

function Invoke-Queue([string]$Route, [hashtable]$Query = @{}) {
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
        throw "Queue request $Route on port $Port failed ($status). Inspect the local instance log."
    }
}

$planPath = (Resolve-Path -LiteralPath $Plan -ErrorAction Stop).Path
$document = Get-Content -LiteralPath $planPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($null -eq $document.tasks) { throw "Plan must contain a 'tasks' array." }
$tasks = @($document.tasks)
if ($tasks.Count -eq 0) { throw 'Plan contains no tasks.' }

$status = Invoke-Queue '/status'
if ($status.mode -ne 'Offscreen') { throw 'The queue requires --mode osr.' }
if ((Invoke-Queue '/grind').running) { throw 'Stop the existing grind before starting the queue.' }

$validated = @()
for ($i = 0; $i -lt $tasks.Count; $i++) {
    $task = $tasks[$i]
    if ($null -eq $task) { throw "Task $($i + 1) is empty." }
    foreach ($field in 'level', 'track', 'cards') {
        if ([string]::IsNullOrWhiteSpace([string]$task.$field)) {
            throw "Task $($i + 1) needs '$field'."
        }
    }
    $rounds = if ($null -eq $task.rounds) { 1 } else { [int]$task.rounds }
    if ($rounds -lt 1 -or $rounds -gt 500) { throw "Task $($i + 1) rounds must be 1-500." }
    $entry = @{
        level = [string]$task.level
        track = [string]$task.track
        cards = [string]$task.cards
        rounds = $rounds
        shovel = [int][bool]$task.shovel
        boss = [int][bool]$task.boss
        autoflame = [int][bool]$task.autoflame
    }
    $report = Invoke-Queue '/track' @{
        path = $entry.track; level = $entry.level; cards = $entry.cards; shovel = $entry.shovel
    }
    if (-not $report.compatibility.CanStart) {
        $codes = @($report.compatibility.Diagnostics | Where-Object Severity -eq 'error' |
            Select-Object -ExpandProperty Code -Unique)
        throw "Task $($i + 1) track is incompatible: $($codes -join ', ')."
    }
    $validated += ,$entry
}

Write-Host "Validated $($validated.Count) queue tasks for profile $($status.profile)."
if ($DryRun) { return }

$activeRun = $null
try {
    for ($i = 0; $i -lt $validated.Count; $i++) {
        $entry = $validated[$i]
        Write-Host "Task $($i + 1)/$($validated.Count): $($entry.level), $($entry.rounds) rounds."
        $query = @{
            state = 'start'; level = $entry.level; track = $entry.track; cards = $entry.cards
            rounds = $entry.rounds; shovel = $entry.shovel; boss = $entry.boss
            autoflame = $entry.autoflame
        }
        $started = Invoke-Queue '/grind' $query
        if (-not $started.running -or -not $started.runDirectory) {
            throw "Task $($i + 1) did not start."
        }
        $activeRun = $started.runDirectory
        $deadline = [DateTime]::UtcNow.AddSeconds(120 + $entry.rounds * 1200)
        while ([DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Seconds 1
            $progress = Invoke-Queue '/grind'
            if ($progress.runDirectory -ne $activeRun) {
                throw "Task $($i + 1) was replaced by another run."
            }
            if (-not $progress.running) {
                if ($progress.state -ne 'finished' -or $progress.roundsDone -lt $entry.rounds) {
                    throw "Task $($i + 1) ended as $($progress.state) after $($progress.roundsDone)/$($entry.rounds) rounds."
                }
                Write-Host "Task $($i + 1)/$($validated.Count) completed."
                $activeRun = $null
                break
            }
        }
        if ($activeRun) { throw "Task $($i + 1) timed out." }
    }
}
finally {
    if ($activeRun) {
        try {
            $current = Invoke-Queue '/grind'
            if ($current.running -and $current.runDirectory -eq $activeRun) {
                [void](Invoke-Queue '/grind' @{ state = 'stop' })
            }
        }
        catch { Write-Warning 'Could not stop the active queue task; inspect the local instance.' }
    }
}
