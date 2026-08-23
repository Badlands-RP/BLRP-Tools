$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repoRoot 'apps\LiveryTool\Badlands.LiveryTool.csproj'
$bin = Join-Path $repoRoot 'apps\LiveryTool\bin\Release\net8.0-windows'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('BLRP-SignBatch-Test-' + [guid]::NewGuid().ToString('N'))
$source = [string](Join-Path $testRoot 'source')
$output = [string](Join-Path $testRoot 'output')

try {
    dotnet build $project -c Release | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Livery Tool build failed.' }

    New-Item -ItemType Directory -Path $source | Out-Null
    Add-Type -AssemblyName System.Drawing
    $bitmap = [Drawing.Bitmap]::new(4, 4)
    try {
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try { $graphics.Clear([Drawing.Color]::Orange) } finally { $graphics.Dispose() }
        $bitmap.Save((Join-Path $source 'sign1.png'), [Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $bitmap.Dispose()
    }

    foreach ($name in @('BCnEncoder.dll', 'StbImageSharp.dll', 'SharpDX.Mathematics.dll', 'SharpDX.dll', 'CodeWalker.Core.dll')) {
        [Reflection.Assembly]::LoadFrom((Join-Path $bin $name)) | Out-Null
    }
    $app = [Reflection.Assembly]::LoadFrom((Join-Path $bin 'Badlands.LiveryTool.dll'))
    $type = $app.GetType('Badlands.LiveryTool.SignBatchWorkflow', $true)
    $workflow = [Activator]::CreateInstance($type, $true)
    $plan = $type.GetMethod('CreatePlan').Invoke($workflow, [object[]]@($source, $output, [string]'sign', [string]'test_livery_', [int]1))
    $type.GetMethod('Build').Invoke($workflow, [object[]]@($plan, $output, $null, [string]'template')) | Out-Null

    $yftPath = Join-Path $output 'test_livery_1.yft'
    $ddsPath = Join-Path $output 'test_livery_1.dds'
    if (-not (Test-Path -LiteralPath $yftPath) -or -not (Test-Path -LiteralPath $ddsPath)) {
        throw 'Sign batch did not create the expected YFT and DDS files.'
    }

    $roundTrip = [CodeWalker.GameFiles.YftFile]::new()
    $roundTrip.Load([IO.File]::ReadAllBytes($yftPath))
    $textureName = $roundTrip.Fragment.Drawable.ShaderGroup.TextureDictionary.Textures.data_items[0].Name
    if ($textureName -ne 'test_livery_1') { throw "Unexpected compiled texture name: $textureName" }
    Write-Output 'Sign batch smoke test passed.'
}
finally {
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
