<#
.SYNOPSIS
    Smoke-tests both scanner scopes in a disposable Git repository.
#>
$ErrorActionPreference = 'Stop'
$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
$testRoot = Join-Path $tempBase ('fvm-secret-check-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $testRoot 'scripts') -Force | Out-Null

function Run-Check([switch]$Staged) {
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $testRoot 'scripts\check-secrets.ps1'))
    if ($Staged) { $arguments += '-Staged' }
    $message = & powershell.exe @arguments 2>&1 | Out-String
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Message = $message }
}

try {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'check-secrets.ps1') -Destination (Join-Path $testRoot 'scripts\check-secrets.ps1')
    & git.exe -C $testRoot init -q
    if ($LASTEXITCODE -ne 0) { throw 'Could not create test repository.' }

    $fake = 'ghp_' + ('A' * 32)
    $probe = Join-Path $testRoot 'probe.txt'
    [IO.File]::WriteAllText($probe, $fake, (New-Object System.Text.UTF8Encoding($false)))
    & git.exe -C $testRoot add -- probe.txt
    if ($LASTEXITCODE -ne 0) { throw 'Could not stage test probe.' }

    [IO.File]::WriteAllText($probe, 'safe text', (New-Object System.Text.UTF8Encoding($false)))
    $staged = Run-Check -Staged
    if ($staged.ExitCode -eq 0 -or $staged.Message -notmatch 'GitHub token' -or $staged.Message.Contains($fake)) {
        throw 'Staged scanner did not detect the staged token safely.'
    }
    $working = Run-Check
    if ($working.ExitCode -ne 0) { throw "Working-tree scanner rejected clean content: $($working.Message)" }

    & git.exe -C $testRoot add -- probe.txt
    if ($LASTEXITCODE -ne 0) { throw 'Could not update test index.' }
    $clean = Run-Check -Staged
    if ($clean.ExitCode -ne 0) { throw "Staged scanner rejected clean content: $($clean.Message)" }

    $private = Join-Path $testRoot '.fvm-data\controller.json'
    New-Item -ItemType Directory -Path (Split-Path -Parent $private) | Out-Null
    [IO.File]::WriteAllText($private, '{}', (New-Object System.Text.UTF8Encoding($false)))
    & git.exe -C $testRoot add -f -- .fvm-data/controller.json
    if ($LASTEXITCODE -ne 0) { throw 'Could not stage private-path probe.' }
    $privateCheck = Run-Check -Staged
    if ($privateCheck.ExitCode -eq 0 -or $privateCheck.Message -notmatch 'private or generated path') {
        throw 'Staged scanner did not reject forced runtime data.'
    }
    Write-Host 'Sensitive-data scanner tests passed.'
}
finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    if ($resolved.StartsWith($tempBase + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolved) -like 'fvm-secret-check-*') {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
