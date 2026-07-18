$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build.ps1') | Out-Host
& (Join-Path $repoRoot 'src\ApexTrace.App\bin\Debug\net10.0-windows\win-x64\ApexTrace.App.exe')
