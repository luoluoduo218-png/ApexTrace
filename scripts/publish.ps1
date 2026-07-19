param(
    [string]$Version = '0.1.0'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = Join-Path $repoRoot 'artifacts'
$output = Join-Path $artifactsRoot 'ApexTrace-win-x64'
$package = Join-Path $artifactsRoot "ApexTrace-v$Version-win-x64.zip"
$project = Join-Path $repoRoot 'src\ApexTrace.App\ApexTrace.App.csproj'

& (Join-Path $PSScriptRoot 'bootstrap.ps1') | Out-Host

if (-not $output.StartsWith($artifactsRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean an output path outside the artifacts directory: $output"
}

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}
if (Test-Path -LiteralPath $package) {
    Remove-Item -LiteralPath $package -Force
}

New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null
& (Join-Path $repoRoot '.dotnet\dotnet.exe') publish $project -c Release -r win-x64 --self-contained true -p:Version=$Version -o $output
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination $output
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination $output
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md') -Destination $output
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs') -Destination (Join-Path $output 'docs') -Recurse

Compress-Archive -Path (Join-Path $output '*') -DestinationPath $package -CompressionLevel Optimal

Write-Host "Published directory: $output"
Write-Host "Portable package: $package"
