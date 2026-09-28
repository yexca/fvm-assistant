<#
.SYNOPSIS
    Runs deterministic automation regressions against the current Debug build, without a browser or account.
#>
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$buildDir = Join-Path $root 'src\FvmAssistant\bin\x64\Debug'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$compiler = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (-not $compiler) { throw 'Visual Studio C# compiler was not found.' }
$app = Join-Path $buildDir 'FvmAssistant.exe'
if (-not (Test-Path -LiteralPath $app)) { throw 'Run scripts/build.ps1 before this test script.' }
$testExe = Join-Path $buildDir 'FvmAssistant.AutomationTests.exe'
& $compiler /nologo /target:exe /platform:x64 /langversion:latest "/out:$testExe" "/reference:$app" "/reference:$(Join-Path $buildDir 'CefSharp.dll')" /reference:System.Drawing.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll (Join-Path $root 'tests\AutomationTests\RegressionTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Automation regression compilation failed.' }
& $testExe
if ($LASTEXITCODE -ne 0) { throw 'Automation regressions failed.' }
