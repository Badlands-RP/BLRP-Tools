# Offline format checks only; these do not establish GTA/FiveM pathfinding behavior.
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
foreach ($name in @('SharpDX.dll', 'SharpDX.Mathematics.dll', 'CodeWalker.Core.dll')) {
    [Reflection.Assembly]::LoadFrom((Join-Path $repoRoot "shared/lib/$name")) | Out-Null
}

function Add-Quad($builder, [float]$x, [float]$y, [float]$width, [float]$height) {
    $poly = $builder.AddPoly([SharpDX.Vector3[]]@(
        [SharpDX.Vector3]::new($x, $y, 5),
        [SharpDX.Vector3]::new(($x + $width), $y, 5),
        [SharpDX.Vector3]::new(($x + $width), ($y + $height), 5),
        [SharpDX.Vector3]::new($x, ($y + $height), 5)
    ))
    $poly.Edges = [CodeWalker.GameFiles.YnvEdge[]]@(0..3 | ForEach-Object {
        $edge = [CodeWalker.GameFiles.YnvEdge]::new()
        $edge.AreaID1 = $edge.AreaID2 = 0x3FFF
        $edge.PolyID1 = $edge.PolyID2 = 0x3FFF
        $edge
    })
    return $poly
}

$results = foreach ($case in @('missing-links', 'linked-pair', 'x-seam', 'xy-seam')) {
    $builder = [CodeWalker.Core.GameFiles.FileTypes.Builders.YnvBuilder]::new()
    switch ($case) {
        'missing-links' {
            $a = Add-Quad $builder 10 10 2 2
            $b = Add-Quad $builder 12 10 2 2
            $a.Edges = $b.Edges = $null # Matches GenerateNavMeshPanel's unfinished output.
            $expectedTiles = 1; $expectedPolys = 2; $expectedLinks = 0
        }
        'linked-pair' {
            $a = Add-Quad $builder 10 10 2 2
            $b = Add-Quad $builder 12 10 2 2
            # Both slots refer to the neighbor, not one slot for each side.
            $a.Edges[1].Poly1 = $a.Edges[1].Poly2 = $b
            $b.Edges[3].Poly1 = $b.Edges[3].Poly2 = $a
            $expectedTiles = 1; $expectedPolys = 2; $expectedLinks = 2
        }
        'x-seam' {
            $null = Add-Quad $builder 148 10 4 2
            $expectedTiles = 2; $expectedPolys = 2; $expectedLinks = 2
        }
        'xy-seam' {
            $null = Add-Quad $builder 148 148 4 4
            $expectedTiles = 4; $expectedPolys = 4; $expectedLinks = 8
        }
    }
    $tiles = @($builder.Build($false))
    $reloaded = @{}
    $maxError = 0.0
    foreach ($tile in $tiles) {
        $bytes = $tile.Save()
        if ([Text.Encoding]::ASCII.GetString($bytes, 0, 4) -ne 'RSC7') {
            throw "$case did not produce a native resource"
        }
        $loaded = [CodeWalker.GameFiles.YnvFile]::new()
        $loaded.Load($bytes)
        $reloaded[$loaded.AreaID] = $loaded
        if ($loaded.Polys.Count -ne $tile.Polys.Count) { throw "$case polygon count changed" }
        for ($i = 0; $i -lt $tile.Polys.Count; $i++) {
            $before = $tile.Polys[$i].Vertices
            $after = $loaded.Polys[$i].Vertices
            if ($before.Length -ne $after.Length) { throw "$case vertex count changed" }
            for ($j = 0; $j -lt $before.Length; $j++) {
                $error = [SharpDX.Vector3]::Distance($before[$j], $after[$j])
                # Flat fixture: half a 16-bit quantization step on each 150m XY axis.
                $limit = [Math]::Sqrt(2) * 150 / 65535 / 2 + 0.00001
                if ([double]::IsNaN($error) -or $error -gt $limit) { throw "$case geometry error $error > $limit" }
                $maxError = [Math]::Max($maxError, $error)
            }
        }
    }
    $polyCount = 0; $linkCount = 0
    foreach ($tile in $reloaded.Values) {
        $polyCount += $tile.Polys.Count
        foreach ($poly in $tile.Polys) {
            foreach ($edge in $poly.Edges) {
                if ($edge.PolyID1 -eq 0x3FFF) { continue }
                $linkCount++
                $neighborTile = $reloaded[[int]$edge.AreaID1]
                if ($null -eq $neighborTile -or $edge.PolyID1 -ge $neighborTile.Polys.Count) {
                    throw "$case has a dangling navigation link"
                }
                if ($edge.AreaID1 -ne $edge.AreaID2 -or $edge.PolyID1 -ne $edge.PolyID2) {
                    throw "$case edge slots disagree"
                }
                $backLinks = @($neighborTile.Polys[$edge.PolyID1].Edges | Where-Object {
                    $_.AreaID1 -eq $tile.AreaID -and $_.PolyID1 -eq $poly.Index
                })
                if ($backLinks.Count -ne 1) { throw "$case link is not reciprocal" }
            }
        }
    }
    if ($tiles.Count -ne $expectedTiles -or $polyCount -ne $expectedPolys -or $linkCount -ne $expectedLinks) {
        throw "$case unexpected tiles/polygons/links: $($tiles.Count)/$polyCount/$linkCount"
    }
    [PSCustomObject]@{Case=$case; Tiles=$tiles.Count; Polygons=$polyCount; DirectedLinks=$linkCount; MaxErrorMeters=$maxError.ToString('F7')}
}
$results | Format-Table -AutoSize
Write-Output 'PASS: native serialization, reciprocal references, simple tile seams and measured geometry error. Runtime untested.'

# Characterize the existing splitter's separate limitation without treating it as a passing check.
$builder = [CodeWalker.Core.GameFiles.FileTypes.Builders.YnvBuilder]::new()
$null = Add-Quad $builder 10 10 450 2
$wideTiles = @($builder.Build($false))
$wideError = 0.0
foreach ($tile in $wideTiles) {
    $loaded = [CodeWalker.GameFiles.YnvFile]::new()
    $loaded.Load($tile.Save())
    for ($i = 0; $i -lt $tile.Polys.Count; $i++) {
        for ($j = 0; $j -lt $tile.Polys[$i].Vertices.Length; $j++) {
            $wideError = [Math]::Max($wideError, [SharpDX.Vector3]::Distance(
                $tile.Polys[$i].Vertices[$j], $loaded.Polys[$i].Vertices[$j]))
        }
    }
}
if ($wideTiles.Count -ne 4 -or $wideError -gt 0.002) {
    Write-Warning "LIMITATION reproduced: a quad from X=10 to X=460 produces $($wideTiles.Count) tiles (expected 4), with $($wideError.ToString('F6'))m maximum save/reload error. Clip at every tile boundary before serialization."
}
