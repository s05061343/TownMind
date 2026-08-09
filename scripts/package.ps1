param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repository "src\AgePilot.App\AgePilot.App.csproj"
$publishDirectory = Join-Path $repository "artifacts\publish\$Runtime"
$packageDirectory = Join-Path $repository "artifacts\packages"
$archive = Join-Path $packageDirectory "AgePilot-public-alpha-$Runtime.zip"

if (-not $NoRestore) {
    dotnet restore $project -r $Runtime
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }
}

dotnet publish $project -c $Configuration -r $Runtime --self-contained true --no-restore -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

New-Item -ItemType Directory -Force -Path $packageDirectory | Out-Null
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive }
Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
Set-Content -LiteralPath "$archive.sha256" -Value "$hash  $(Split-Path -Leaf $archive)" -Encoding ascii

Write-Host "Portable package: $archive"
Write-Host "SHA256: $hash"
