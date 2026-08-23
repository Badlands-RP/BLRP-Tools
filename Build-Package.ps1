param([string]$Version = '1.0.10')

$ErrorActionPreference = 'Stop'
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'release'))
$stagingRoot = Join-Path $releaseRoot 'staging'
$packageRoot = Join-Path $stagingRoot 'BLRP-Tools'
$zipPath = Join-Path $releaseRoot "BLRP-Tools-v$Version-win-x64.zip"

if (Test-Path -LiteralPath $stagingRoot) {
    $resolved = [IO.Path]::GetFullPath($stagingRoot)
    if (-not $resolved.StartsWith($releaseRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear staging outside the release directory: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
$projects = @(
    @{ Project = 'apps\ToolsHub\BLRP.ToolsHub.csproj'; Output = ''; SelfContained = $true },
    @{ Project = 'apps\AssetStudio\BLRP.AssetStudio.csproj'; Output = 'tools\AssetStudio'; SelfContained = $false },
    @{ Project = 'apps\ClothingLocator\BLRP.ClothingLocator.csproj'; Output = 'tools\ClothingLocator'; SelfContained = $false },
    @{ Project = 'apps\LiveryTool\Badlands.LiveryTool.csproj'; Output = 'tools\LiveryTool'; SelfContained = $false },
    @{ Project = 'apps\MappingDeconflicter\YmapDeconflicter.csproj'; Output = 'tools\MappingDeconflicter'; SelfContained = $false }
)

foreach ($item in $projects) {
    $output = if ($item.Output) { Join-Path $packageRoot $item.Output } else { $packageRoot }
    $selfContained = $item.SelfContained.ToString().ToLowerInvariant()
    $publishArguments = @('publish', (Join-Path $PSScriptRoot $item.Project), '-c', 'Release', '-r', 'win-x64', '--self-contained', $selfContained, "-p:UseAppHost=$selfContained")
    if (-not $item.Output) { $publishArguments += "-p:Version=$Version" }
    $publishArguments += @('-o', $output)
    dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $($item.Project)." }
}

$grzyRoot = Join-Path $PSScriptRoot 'external\grzyClothTool'
$grzyProject = Join-Path $grzyRoot 'grzyClothTool\grzyClothTool.csproj'
if (-not (Test-Path -LiteralPath $grzyProject)) {
    throw 'grzyClothTool is missing. Run: git submodule update --init --recursive'
}
$grzyPackages = Join-Path $grzyRoot 'packages'
New-Item -ItemType Directory -Path $grzyPackages -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'shared\grzy\CodeWalker.dll') -Destination $grzyPackages -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'shared\grzy\CodeWalker.Core.dll') -Destination $grzyPackages -Force
$grzyOutput = Join-Path $packageRoot 'tools\grzyClothTool-outfit'
dotnet publish $grzyProject -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=false -o $grzyOutput
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed for grzyClothTool.' }
Copy-Item -LiteralPath (Join-Path $grzyRoot 'LICENSE') -Destination (Join-Path $grzyOutput 'LICENSE-grzyClothTool.txt')
$grzyCommit = (git -C $grzyRoot rev-parse HEAD).Trim()
@("grzyClothTool is distributed under GPL-3.0.", '', 'Source: https://github.com/Badlands-RP/grzyClothTool', "Commit: $grzyCommit") |
    Set-Content -LiteralPath (Join-Path $grzyOutput 'SOURCE.txt') -Encoding utf8

$sharedRoot = Join-Path $packageRoot 'shared'
New-Item -ItemType Directory -Path $sharedRoot -Force | Out-Null
foreach ($name in @('CodeWalker.Core.dll', 'SharpDX.dll', 'SharpDX.Mathematics.dll')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "shared\lib\$name") -Destination $sharedRoot
    foreach ($tool in @('AssetStudio', 'ClothingLocator', 'LiveryTool')) {
        $duplicate = Join-Path $packageRoot "tools\$tool\$name"
        if (Test-Path -LiteralPath $duplicate) { Remove-Item -LiteralPath $duplicate -Force }
    }
}
foreach ($tool in @('AssetStudio', 'ClothingLocator', 'LiveryTool', 'MappingDeconflicter')) {
    $duplicateLogo = Join-Path $packageRoot "tools\$tool\BLRP_Logo.png"
    if (Test-Path -LiteralPath $duplicateLogo) { Remove-Item -LiteralPath $duplicateLogo -Force }
}

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $packageRoot
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath -CompressionLevel Optimal
Write-Output $zipPath
