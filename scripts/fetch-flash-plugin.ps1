<#
.SYNOPSIS
    Copies pepflashplayer.dll from a local CefFlashBrowser install into vendor\pepflash\.

.DESCRIPTION
    The Flash plugin is not stored in this repo. CefFlashBrowser (MIT, github.com/Mzying2001/CefFlashBrowser)
    ships a patched PPAPI Flash build under Assets\Plugins; the build copies it next to FvmAssistant.exe.

.EXAMPLE
    .\scripts\fetch-flash-plugin.ps1 -CefFlashBrowserDir "$HOME\Documents\App\FlashBrowser_x64_v1.1.7"
#>
param(
    [Parameter(Mandatory)]
    [string]$CefFlashBrowserDir
)

$ErrorActionPreference = 'Stop'

$source = Join-Path $CefFlashBrowserDir 'Assets\Plugins\pepflashplayer.dll'
if (-not (Test-Path $source)) {
    throw "pepflashplayer.dll not found at $source (point -CefFlashBrowserDir at the x64 CefFlashBrowser folder)"
}

$targetDir = Join-Path $PSScriptRoot '..\vendor\pepflash'
New-Item -ItemType Directory -Force $targetDir | Out-Null
Copy-Item $source $targetDir -Force

$target = Join-Path $targetDir 'pepflashplayer.dll'
$info = Get-Item $target
[pscustomobject]@{
    Path    = (Resolve-Path $target).Path
    Version = $info.VersionInfo.FileVersion
    Bytes   = $info.Length
    SHA256  = (Get-FileHash $target -Algorithm SHA256).Hash
}
