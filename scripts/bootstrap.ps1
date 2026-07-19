$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $repoRoot '.dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $installer = Join-Path $env:TEMP 'ApexTrace-dotnet-install.ps1'
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
    & $installer -Version '10.0.302' -InstallDir (Join-Path $repoRoot '.dotnet')
}
& $dotnet --info
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
