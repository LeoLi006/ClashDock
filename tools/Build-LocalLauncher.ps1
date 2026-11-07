$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /optimize+ /reference:System.Windows.Forms.dll "/out:$root\ClashDock.exe" (Join-Path $PSScriptRoot 'LocalLauncher.cs')
if ($LASTEXITCODE -ne 0) { throw 'Launcher compilation failed' }
