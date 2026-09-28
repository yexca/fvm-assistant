<#
.SYNOPSIS
    Compatibility entry point for the renamed assistant.ps1 control script.
#>
param(
    [Parameter(Mandatory)]
    [string]$Path,
    [int]$Port = 18765
)

$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'assistant.ps1') -Path $Path -Port $Port
