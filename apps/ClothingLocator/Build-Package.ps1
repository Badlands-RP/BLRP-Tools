[CmdletBinding()]
param(
    [string]$GrzySource = 'F:\Documents\GitHub\grzyClothTool',
    [string]$CodeWalkerDependencies = 'F:\Documents\GitHub\CodeWalker\bin\Codewalker',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'release')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$utilityProject = Join-Path $PSScriptRoot 'BLRP.ClothingLocator.csproj'
$grzyProject = Join-Path $GrzySource 'grzyClothTool\grzyClothTool.csproj'
if (-not (Test-Path -LiteralPath $grzyProject)) {
    throw "grzyClothTool project not found at '$grzyProject'. Pass -GrzySource if the checkout moved."
}
if (-not (Test-Path -LiteralPath (Join-Path $CodeWalkerDependencies 'SharpDX.XInput.dll'))) {
    throw "CodeWalker runtime dependencies were not found at '$CodeWalkerDependencies'."
}

[xml]$projectXml = Get-Content -LiteralPath $utilityProject -Raw
$version = [string]$projectXml.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
$packageName = "BLRP-Clothing-Utility-$version-win-x64"
$stageRoot = Join-Path ([IO.Path]::GetTempPath()) ($packageName + '-' + [guid]::NewGuid().ToString('N'))
$packageRoot = Join-Path $stageRoot 'BLRP-Clothing-Utility'
$utilityOutput = Join-Path $packageRoot 'app'
$grzyOutput = Join-Path $packageRoot 'tools\grzyClothTool-outfit'
$zipPath = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) ($packageName + '.zip')

try {
    New-Item -ItemType Directory -Path $utilityOutput, $grzyOutput, $OutputDirectory -Force | Out-Null

    & dotnet publish $utilityProject -c Release -r win-x64 --self-contained true -o $utilityOutput
    if ($LASTEXITCODE -ne 0) { throw 'BLRP Clothing Utility publish failed.' }

    & dotnet publish $grzyProject -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=false -o $grzyOutput
    if ($LASTEXITCODE -ne 0) { throw 'grzyClothTool publish failed.' }

    Get-ChildItem -LiteralPath $CodeWalkerDependencies -Filter 'SharpDX*.dll' | ForEach-Object {
        $destination = Join-Path $grzyOutput $_.Name
        if (-not (Test-Path -LiteralPath $destination)) {
            Copy-Item -LiteralPath $_.FullName -Destination $destination
        }
    }

    Copy-Item -LiteralPath (Join-Path $GrzySource 'LICENSE') -Destination (Join-Path $grzyOutput 'LICENSE-grzyClothTool.txt')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'tools\grzyClothTool-outfit\BLRP-preview.patch') -Destination $grzyOutput
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $packageRoot
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CHANGELOG.md') -Destination $packageRoot

    $origin = (& git -C $GrzySource remote get-url origin).Trim()
    $commit = (& git -C $GrzySource rev-parse HEAD).Trim()
    $branch = (& git -C $GrzySource branch --show-current).Trim()
    $status = @(& git -C $GrzySource status --short)
    $sourceText = @(
        'grzyClothTool is distributed under GPL-3.0.'
        ''
        "Source checkout: $origin"
        "Branch: $branch"
        "Base commit: $commit"
        'The exact uncommitted source changes used for this build are in BLRP-working-tree.patch.'
        ''
        'Working tree at build time:'
        $(if ($status.Count -eq 0) { 'clean' } else { $status })
    ) -join [Environment]::NewLine
    [IO.File]::WriteAllText((Join-Path $grzyOutput 'SOURCE.txt'), $sourceText, [Text.UTF8Encoding]::new($false))

    $workingTreePatch = (@(& git -C $GrzySource diff --no-ext-diff) -join [Environment]::NewLine)
    [IO.File]::WriteAllText((Join-Path $grzyOutput 'BLRP-working-tree.patch'), $workingTreePatch, [Text.UTF8Encoding]::new($false))

    $startText = @'
BLRP Clothing Utility

Run: app\BLRP.ClothingUtility.exe

The package includes the Windows runtimes and the BLRP grzyClothTool build.
GTA V and a clothing repository are not included. On its first 3D preview,
grzyClothTool may ask for the local GTA V installation folder.
'@
    [IO.File]::WriteAllText((Join-Path $packageRoot 'START HERE.txt'), $startText, [Text.UTF8Encoding]::new($false))

    Compress-Archive -Path $packageRoot -DestinationPath $zipPath -CompressionLevel Optimal -Force

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $entryNames = @($archive.Entries.FullName | ForEach-Object { $_.Replace('\', '/') })
        foreach ($required in @(
            'BLRP-Clothing-Utility/app/BLRP.ClothingUtility.exe',
            'BLRP-Clothing-Utility/CHANGELOG.md',
            'BLRP-Clothing-Utility/tools/grzyClothTool-outfit/grzyClothTool.exe',
            'BLRP-Clothing-Utility/tools/grzyClothTool-outfit/SOURCE.txt'
        )) {
            if ($entryNames -notcontains $required) { throw "Package is missing '$required'." }
        }
    }
    finally {
        $archive.Dispose()
    }

    Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath
}
finally {
    if (Test-Path -LiteralPath $stageRoot) {
        Remove-Item -LiteralPath $stageRoot -Recurse -Force
    }
}
