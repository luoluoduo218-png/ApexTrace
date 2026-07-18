$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'bootstrap.ps1') | Out-Host
$output = Join-Path $repoRoot 'artifacts\ApexTrace-win-x64'
& (Join-Path $repoRoot '.dotnet\dotnet.exe') publish (Join-Path $repoRoot 'src\ApexTrace.App\ApexTrace.App.csproj') -c Release -r win-x64 --self-contained true -o $output
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Published: $output"
