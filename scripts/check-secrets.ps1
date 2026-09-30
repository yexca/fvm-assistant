<#
.SYNOPSIS
    Checks Git candidates, or the exact staged blobs, for likely secrets and private files.
.DESCRIPTION
    Prints file names, line numbers, and rule names only. It never prints matched values.
    This is a high-confidence guardrail, not a replacement for human review.
#>
param([switch]$Staged)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Invoke-GitBytes([string]$arguments) {
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = 'git.exe'
    $start.Arguments = $arguments
    $start.WorkingDirectory = $root
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::Start($start)
    $output = New-Object System.IO.MemoryStream
    try {
        $process.StandardOutput.BaseStream.CopyTo($output)
        $errorText = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) { throw "git $arguments failed: $errorText" }
        return ,$output.ToArray()
    }
    finally {
        $output.Dispose()
        $process.Dispose()
    }
}

if (-not (Test-Path (Join-Path $root '.git'))) {
    throw 'Git repository not initialized. Run git init in the project root first.'
}

$utf8 = New-Object System.Text.UTF8Encoding($false, $true)
$entries = New-Object System.Collections.Generic.List[object]
if ($Staged) {
    $listing = $utf8.GetString((Invoke-GitBytes 'ls-files --stage -z'))
    foreach ($item in $listing.Split([char]0)) {
        if (-not $item) { continue }
        if ($item -notmatch '^\d+ ([0-9a-fA-F]{40,64}) ([0-3])\t(.+)$') { throw 'Could not parse staged Git entry.' }
        if ($Matches[2] -ne '0') { throw "Unmerged index entry: $($Matches[3])" }
        $entries.Add([pscustomobject]@{ Path = $Matches[3]; Oid = $Matches[1] })
    }
}
else {
    $listing = $utf8.GetString((Invoke-GitBytes 'ls-files --cached --others --exclude-standard -z'))
    foreach ($path in $listing.Split([char]0)) {
        if ($path) { $entries.Add([pscustomobject]@{ Path = $path; Oid = $null }) }
    }
}

$rules = @(
    @{ Name = 'private key block'; Regex = '-----BEGIN (?:[A-Z0-9 ]+ )?PRIVATE KEY-----' },
    @{ Name = 'GitHub token'; Regex = '(?:github_pat_|gh[pousr]_)[A-Za-z0-9_]{20,}' },
    @{ Name = 'OpenAI-style secret key'; Regex = 'sk-[A-Za-z0-9_-]{20,}' },
    @{ Name = 'Slack token'; Regex = 'xox[baprs]-[A-Za-z0-9-]{20,}' },
    @{ Name = 'AWS access key'; Regex = '(?:AKIA|ASIA)[0-9A-Z]{16}' },
    @{ Name = 'Google API key'; Regex = 'AIza[0-9A-Za-z_-]{35}' },
    @{ Name = 'Bearer token'; Regex = '(?i)Authorization\s*:\s*Bearer\s+[A-Za-z0-9._~+/-]{20,}' },
    @{ Name = 'URL with embedded password'; Regex = '(?i)https?://[^\s/:@]+:[^\s/@]+@' }
)
$textExtensions = @('.cs', '.ps1', '.md', '.txt', '.json', '.xml', '.config', '.yml', '.yaml', '.toml', '.ini', '.html', '.py', '.sh', '.props', '.targets')
$findings = New-Object System.Collections.Generic.List[string]
$checked = 0

foreach ($entry in $entries) {
    $path = $entry.Path.Replace('\', '/')
    $isPrivateEnv = $path -match '(?i)(^|/)\.env(?:\..*)?$' -and $path -notmatch '(?i)(^|/)\.env\.example$'
    if ($path -match '(?i)(^|/)(\.fvm-data|vendor|sample|bin|obj|profiles|captures|recordings|logs|cards)(/|$)' -or
        $isPrivateEnv -or
        $path -match '(?i)(^|/)(?:id_rsa|id_ed25519|.*\.(?:pem|key|pfx|p12|kdbx|log|har|sqlite|sqlite3)|.*credentials.*\.json|.*secrets.*\.json)$') {
        $findings.Add("$path : private or generated path")
        continue
    }

    if ($entry.Oid) { $bytes = Invoke-GitBytes "cat-file blob $($entry.Oid)" }
    else {
        $fullPath = Join-Path $root ($path.Replace('/', [IO.Path]::DirectorySeparatorChar))
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { continue }
        $bytes = [IO.File]::ReadAllBytes($fullPath)
    }

    $name = [IO.Path]::GetFileName($path)
    $extension = [IO.Path]::GetExtension($path).ToLowerInvariant()
    if ($textExtensions -notcontains $extension -and $name -notin @('Makefile', '.gitignore', '.gitattributes', 'pre-commit')) { continue }
    $checked++
    try { $content = $utf8.GetString($bytes) }
    catch {
        $findings.Add("$path : text is not valid UTF-8")
        continue
    }
    $lines = $content -split "`r?`n"
    for ($i = 0; $i -lt $lines.Length; $i++) {
        foreach ($rule in $rules) {
            if ([regex]::IsMatch($lines[$i], $rule.Regex)) {
                $findings.Add("${path}:$($i + 1) : $($rule.Name)")
            }
        }
    }
}

if ($findings.Count -gt 0) {
    Write-Host "Sensitive-data check failed ($($findings.Count) finding(s)):" -ForegroundColor Red
    $findings | Sort-Object -Unique | ForEach-Object { Write-Host "  $_" }
    exit 1
}

$scope = if ($Staged) { 'staged files' } else { 'Git candidates' }
Write-Host "Sensitive-data check passed: $checked text files scanned in $scope."
