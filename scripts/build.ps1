$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'bootstrap.ps1') | Out-Host
& (Join-Path $repoRoot '.dotnet\dotnet.exe') restore (Join-Path $repoRoot 'ApexTrace.sln')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& (Join-Path $repoRoot '.dotnet\dotnet.exe') build (Join-Path $repoRoot 'ApexTrace.sln') --no-restore -c Debug
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
