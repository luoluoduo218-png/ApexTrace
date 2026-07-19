$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build.ps1') | Out-Host
& (Join-Path $repoRoot '.dotnet\dotnet.exe') test (Join-Path $repoRoot 'ApexTrace.sln') --no-build --no-restore -c Debug
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
