<#
.SYNOPSIS
    Calls a FvmAssistant control endpoint and prints the JSON reply.

.EXAMPLE
    .\scripts\assistant.ps1 /status
    .\scripts\assistant.ps1 '/sample?method=devtools&count=4'
    .\scripts\assistant.ps1 '/click?x=475&y=300&method=cef'
#>
param(
    [Parameter(Mandatory)]
    [string]$Path,
    [int]$Port = 18765
)

$ErrorActionPreference = 'Stop'
if (-not $Path.StartsWith('/')) { $Path = '/' + $Path }

try {
    $reply = Invoke-RestMethod -Uri "http://127.0.0.1:$Port$Path" -Headers @{ 'X-Fvm-Assistant' = '1' } -TimeoutSec 60
    $reply | ConvertTo-Json -Depth 8
}
catch {
    $body = $_.ErrorDetails.Message
    if ($body) { Write-Error $body } else { throw }
}
