param([string]$Version = '1.2.4')

$ErrorActionPreference = 'Stop'
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'release'))
$stagingRoot = Join-Path $releaseRoot 'staging'
$packageRoot = Join-Path $stagingRoot 'BLRP-Asset-Studio'
$zipPath = Join-Path $releaseRoot "BLRP-Asset-Studio-v$Version-win-x64.zip"

if (Test-Path -LiteralPath $stagingRoot) {
    $resolved = [IO.Path]::GetFullPath($stagingRoot)
    if (-not $resolved.StartsWith($releaseRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear staging outside the release directory: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null

dotnet publish (Join-Path $PSScriptRoot 'BLRP.AssetStudio.csproj') `
    -c Release -r win-x64 --self-contained true -o $packageRoot
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CodeWalker-NOTICE.txt') -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ImageSharp-LICENSE.txt') -Destination $packageRoot
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath -CompressionLevel Optimal

Write-Output $zipPath
