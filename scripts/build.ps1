<#
.SYNOPSIS
    Restores and builds FvmAssistant with Visual Studio's MSBuild (no .NET SDK needed).
#>
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) { throw 'vswhere.exe not found; install Visual Studio 2022 or its Build Tools' }

$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\amd64\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'MSBuild.exe not found' }

$project = Join-Path $PSScriptRoot '..\src\FvmAssistant\FvmAssistant.csproj'
& $msbuild $project -restore "-p:Configuration=$Configuration" -p:Platform=x64 -v:minimal -nologo
exit $LASTEXITCODE
