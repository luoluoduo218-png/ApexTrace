$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build.ps1') | Out-Host
$output = Join-Path $repoRoot 'screenshots'
New-Item -ItemType Directory -Force -Path $output | Out-Null
& (Join-Path $repoRoot '.dotnet\dotnet.exe') (Join-Path $repoRoot 'src\ApexTrace.App\bin\Debug\net10.0-windows\win-x64\ApexTrace.App.dll') "--capture-ui=$output"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Get-ChildItem -LiteralPath $output -Filter '*.png'
