# SpaceMaid brand logo verifier (independent check, deliberately kept out of the generator).
#
# It loads the geometry constants and the PNG decoder from gen-logo.ps1, then reads
# logo.png / logo-64.png back from disk and decodes them row-by-row. So the numbers
# it prints describe the SAME design the generator draws, but they are measured from
# the bytes that actually landed on disk, not from the in-memory bitmaps.
#
# The decoder hands back RAW RGBA byte rows (PNG channel order). It deliberately does
# NOT go through System.Drawing's 32-bit pixels: GDI+ stores 32bppArgb as BGRA in
# memory on little-endian hosts, so a naive copy silently transposes the R and B
# channels and every "is this pixel blue" test then reads backwards. Keeping the
# bytes in PNG order removes that trap, and the generator's own in-memory bitmap is
# used to cross-check the raw numbers.
#
# Usage (this box forbids running .ps1 files directly, so dot-source the text):
#   Invoke-Expression (Get-Content -Raw -Encoding UTF8 'Code\scripts\check-logo.ps1')
#
# It never writes anything.

param([string]$AssetsDir = '')

# ---------------------------------------------------------------------------
# PNG decoding, straight to RGBA bytes.
# ---------------------------------------------------------------------------
function Test-SmPngSignature([byte[]]$Bytes) {
    if ($Bytes.Length -lt 8) { return $false }
    for ($i = 0; $i -lt 8; $i++) {
        if ($Bytes[$i] -ne $script:SM_PNG_SIGNATURE[$i]) { return $false }
    }
    return $true
}

function Read-SmPngRgba([string]$Path) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if (-not (Test-SmPngSignature -Bytes $bytes)) {
        throw ('Not a PNG (bad signature): {0}' -f $Path)
    }

    $width = 0
    $height = 0
    $bitDepth = 0
    $colorType = 0
    $sawIhdr = $false
    $idat = New-Object System.IO.MemoryStream
    $offset = 8

    while ($offset + 8 -le $bytes.Length) {
        $chunkLength = [int]$bytes[$offset] * 16777216 + [int]$bytes[$offset + 1] * 65536 + [int]$bytes[$offset + 2] * 256 + [int]$bytes[$offset + 3]
        $chunkType = [System.Text.Encoding]::ASCII.GetString($bytes, $offset + 4, 4)
        $dataStart = $offset + 8

        if ($chunkType -eq 'IHDR') {
            $width = [int]$bytes[$dataStart] * 16777216 + [int]$bytes[$dataStart + 1] * 65536 + [int]$bytes[$dataStart + 2] * 256 + [int]$bytes[$dataStart + 3]
            $height = [int]$bytes[$dataStart + 4] * 16777216 + [int]$bytes[$dataStart + 5] * 65536 + [int]$bytes[$dataStart + 6] * 256 + [int]$bytes[$dataStart + 7]
            $bitDepth = [int]$bytes[$dataStart + 8]
            $colorType = [int]$bytes[$dataStart + 9]
            $sawIhdr = $true
        }
        elseif ($chunkType -eq 'IDAT') {
            $idat.Write($bytes, $dataStart, $chunkLength)
        }
        elseif ($chunkType -eq 'IEND') {
            break
        }

        $offset = $dataStart + $chunkLength + 4
    }

    if (-not $sawIhdr) {
        throw ('PNG has no IHDR chunk: {0}' -f $Path)
    }
    if ($bitDepth -ne 8 -or $colorType -ne 6) {
        throw ('Expected an 8-bit RGBA PNG (colour type 6), got bit depth {0} / colour type {1}: {2}' -f $bitDepth, $colorType, $Path)
    }

    $idatBytes = [byte[]]$idat.ToArray()
    $idat.Dispose()

    if ($idatBytes.Length -lt 6) {
        throw ('PNG data stream is empty: {0}' -f $Path)
    }

    # Strip the 2-byte zlib header and the trailing 4-byte Adler-32: DeflateStream
    # wants the raw deflate payload.
    $deflateInput = New-Object System.IO.MemoryStream(, [byte[]]$idatBytes[2..($idatBytes.Length - 5)])
    $deflateStream = New-Object System.IO.Compression.DeflateStream($deflateInput, [System.IO.Compression.CompressionMode]::Decompress)
    $inflated = New-Object System.IO.MemoryStream
    try {
        $deflateStream.CopyTo($inflated)
    }
    finally {
        $deflateStream.Dispose()
        $deflateInput.Dispose()
    }

    $filtered = [byte[]]$inflated.ToArray()
    $inflated.Dispose()

    $stride = $width * 4
    $expected = ($stride + 1) * $height
    if ($filtered.Length -ne $expected) {
        throw ('Inflated PNG size mismatch: expected {0} bytes, got {1}: {2}' -f $expected, $filtered.Length, $Path)
    }

    $pixels = New-Object byte[] ($stride * $height)
    $filterZeroRows = 0

    for ($y = 0; $y -lt $height; $y++) {
        $filteredStart = $y * ($stride + 1)
        $filter = [int]$filtered[$filteredStart]
        if ($filter -eq 0) { $filterZeroRows++ }

        for ($x = 0; $x -lt $stride; $x++) {
            $rawByte = [int]$filtered[$filteredStart + 1 + $x]
            $left = if ($x -ge 4) { [int]$pixels[($y * $stride) + $x - 4] } else { 0 }
            $up = if ($y -gt 0) { [int]$pixels[(($y - 1) * $stride) + $x] } else { 0 }
            $upLeft = if ($x -ge 4 -and $y -gt 0) { [int]$pixels[(($y - 1) * $stride) + $x - 4] } else { 0 }

            $value = 0
            switch ($filter) {
                0 { $value = $rawByte }
                1 { $value = $rawByte + $left }
                2 { $value = $rawByte + $up }
                3 { $value = $rawByte + [int][Math]::Floor(($left + $up) / 2.0) }
                4 {
                    $p = $left + $up - $upLeft
                    $pa = [Math]::Abs($p - $left)
                    $pb = [Math]::Abs($p - $up)
                    $pc = [Math]::Abs($p - $upLeft)
                    if ($pa -le $pb -and $pa -le $pc) { $value = $rawByte + $left }
                    elseif ($pb -le $pc) { $value = $rawByte + $up }
                    else { $value = $rawByte + $upLeft }
                }
                default { throw ('Unsupported PNG row filter {0} in {1}' -f $filter, $Path) }
            }

            $pixels[($y * $stride) + $x] = [byte]($value -band 255)
        }
    }

    return [pscustomobject]@{
        Width = $width
        Height = $height
        Stride = $stride
        Pixels = $pixels
        FilterZeroRows = $filterZeroRows
    }
}

# ---------------------------------------------------------------------------
# Pixel statistics, computed straight off the RGBA byte rows.
# ---------------------------------------------------------------------------
function Get-SmRgba([byte[]]$Pixels, [int]$Stride, [int]$X, [int]$Y) {
    $index = ($Y * $Stride) + ($X * 4)
    return [pscustomobject]@{
        R = [int]$Pixels[$index]
        G = [int]$Pixels[$index + 1]
        B = [int]$Pixels[$index + 2]
        A = [int]$Pixels[$index + 3]
    }
}

function Format-SmRgba([object]$Pixel) {
    return ('ARGB({0},{1},{2},{3})' -f $Pixel.A, $Pixel.R, $Pixel.G, $Pixel.B)
}

function Get-SmPixelStats([object]$Image, [string]$Label) {
    $width = $Image.Width
    $height = $Image.Height
    $stride = $Image.Stride
    $pixels = $Image.Pixels

    $white = 0
    $blue = 0
    $transparent = 0
    $other = 0

    for ($i = 0; $i -lt ($stride * $height); $i += 4) {
        $r = [int]$pixels[$i]
        $g = [int]$pixels[$i + 1]
        $b = [int]$pixels[$i + 2]
        $a = [int]$pixels[$i + 3]

        if ($a -eq 0) { $transparent++; continue }
        if ($r -gt 240 -and $g -gt 240 -and $b -gt 240 -and $a -gt 200) { $white++; continue }
        if ($b -gt $r -and $a -gt 200) { $blue++; continue }
        $other++
    }

    $cornerPoints = @(
        @{ Name = 'TL'; X = 0;          Y = 0 },
        @{ Name = 'TR'; X = $width - 1; Y = 0 },
        @{ Name = 'BL'; X = 0;          Y = $height - 1 },
        @{ Name = 'BR'; X = $width - 1; Y = $height - 1 }
    )

    $cornerTexts = New-Object 'System.Collections.Generic.List[string]'
    $cornersOk = $true
    foreach ($corner in $cornerPoints) {
        $pixel = Get-SmRgba -Pixels $pixels -Stride $stride -X $corner.X -Y $corner.Y
        $cornerTexts.Add(('{0}={1}' -f $corner.Name, (Format-SmRgba $pixel)))
        if ($pixel.A -ne 0) { $cornersOk = $false }
    }

    # --- angular scan of the white ring ---
    # Angles use the GDI+ convention (y grows downward): 0 = east/right, 90 = south,
    # 180 = west, 270 = north, 315 = north-east. Sampling at eighth-turns plus two
    # in-gap probes is the only way to prove WHICH side the 90-degree gap is on; a
    # cartesian probe cannot tell a mirrored design from a correct one.
    $edge = [double]$width
    $center = ($edge - 1.0) / 2.0

    function Get-SmAngleSample([object]$Image, [double]$Radius, [double]$AngleDegrees) {
        $radians = $AngleDegrees * [Math]::PI / 180.0
        $sx = [int][Math]::Round($center + ($Radius * [Math]::Cos($radians)))
        $sy = [int][Math]::Round($center + ($Radius * [Math]::Sin($radians)))
        $sx = [Math]::Min($Image.Width - 1, [Math]::Max(0, $sx))
        $sy = [Math]::Min($Image.Height - 1, [Math]::Max(0, $sy))
        return (Get-SmRgba -Pixels $Image.Pixels -Stride $Image.Stride -X $sx -Y $sy)
    }

    function Test-SmIsWhite([object]$Pixel) {
        return ($Pixel.A -gt 200 -and $Pixel.R -gt 240 -and $Pixel.G -gt 240 -and $Pixel.B -gt 240)
    }

    # 0.26 x edge sits inside the stroke for every angle (inner edge 0.305, outer 0.38),
    # so a gap sample is genuinely in the gap and not merely off the centreline.
    $ringRadius = $edge * 0.26
    $onRingAngles = @(0, 45, 90, 135, 180, 225, 270)
    $inGapAngles = @(337.5, 292.5)

    $ringTexts = New-Object 'System.Collections.Generic.List[string]'
    $ringStatus = @{}
    foreach ($angle in ($onRingAngles + $inGapAngles)) {
        $pixel = Get-SmAngleSample -Image $Image -Radius $ringRadius -AngleDegrees $angle
        $isWhite = Test-SmIsWhite -Pixel $pixel
        $ringStatus[[string]$angle] = $isWhite
        $mark = if ($isWhite) { 'white' } else { 'not-white' }
        $ringTexts.Add(('{0}deg={1}' -f $angle, $mark))
    }

    # The arc spans 0 -> 270 (clockwise on screen); the gap is the quadrant around 315.
    # 337.5 and 292.5 are comfortably inside that gap, clear of both round caps and of
    # the dot (which sits at 315).
    $gapOk = (-not $ringStatus['337.5']) -and (-not $ringStatus['292.5'])
    $ringOk = $true
    foreach ($angle in $onRingAngles) {
        if (-not $ringStatus[[string]$angle]) { $ringOk = $false }
    }

    # --- the dot sits inside that gap, on the north-east diagonal ---
    $dotRatio = $script:SM_LOGO_DOT_CENTER_RATIO
    $dotX = [int][Math]::Round($edge * $dotRatio)
    $dotY = [int][Math]::Round($edge * $dotRatio)
    $dotPixel = Get-SmRgba -Pixels $pixels -Stride $stride -X $dotX -Y $dotY
    $dotOk = Test-SmIsWhite -Pixel $dotPixel

    # --- diagonal gradient probes ---
    # The only pixels guaranteed to be solid square are the ones inside |u| < ~0.24 of
    # the centre: the ring's blob reaches roughly 0.26 x edge from the centre along the
    # diagonal (inner circle 0.305 minus the 0.0375 half-stroke, times cos 45) and the
    # rounded corners' anti-aliasing reaches ~0.238, so these probes sit well inside both.
    # Off-diagonal probes are deliberately avoided: a probe at, say, (0.60, 0.24) looks
    # safe on paper but lands on the ring or the corner rim, as this verifier found early on.
    $probeCoordinates = @(
        @{ X = [int][Math]::Round($edge * 0.10); Y = [int][Math]::Round($edge * 0.10) },
        @{ X = [int][Math]::Round($edge * 0.15); Y = [int][Math]::Round($edge * 0.15) },
        @{ X = [int][Math]::Round($edge * 0.20); Y = [int][Math]::Round($edge * 0.20) }
    )

    $probeTexts = New-Object 'System.Collections.Generic.List[string]'
    $probeSamples = New-Object 'System.Collections.Generic.List[object]'
    foreach ($coordinate in $probeCoordinates) {
        $x = [Math]::Min($width - 1, [Math]::Max(0, $coordinate.X))
        $y = [Math]::Min($height - 1, [Math]::Max(0, $coordinate.Y))
        $pixel = Get-SmRgba -Pixels $pixels -Stride $stride -X $x -Y $y
        $probeTexts.Add(('({0},{1})={2}' -f $x, $y, (Format-SmRgba $pixel)))
        $probeSamples.Add($pixel)
    }

    # All three probes sit in the solid square, so all three must be opaque blue and
    # must not read as white (the white channels are all > 240).
    $blueCoreOk = $true
    foreach ($sample in $probeSamples) {
        if (-not ($sample.B -gt $sample.R -and $sample.B -gt 180 -and $sample.R -lt 120 -and $sample.A -gt 200)) {
            $blueCoreOk = $false
        }
    }

    # Gradient direction: the brush runs ARGB(70,146,238) -> ARGB(32,92,180) from the
    # top-left corner to the bottom-right corner, so along this diagonal R, G and B all
    # fall. B stays far above R everywhere, which is what "it is still blue" means.
    $gradientOk = $true
    $gradientDetail = 'n/a'
    if ($blueCoreOk) {
        for ($i = 0; $i -lt ($probeSamples.Count - 1); $i++) {
            if (-not ($probeSamples[$i].R -gt $probeSamples[$i + 1].R)) { $gradientOk = $false }
            if (-not ($probeSamples[$i].G -gt $probeSamples[$i + 1].G)) { $gradientOk = $false }
            if (-not ($probeSamples[$i].B -gt $probeSamples[$i + 1].B)) { $gradientOk = $false }
        }

        $gradientDetail = ('R {0}->{1}->{2}, G {3}->{4}->{5}, B {6}->{7}->{8}' -f `
            $probeSamples[0].R, $probeSamples[1].R, $probeSamples[2].R, `
            $probeSamples[0].G, $probeSamples[1].G, $probeSamples[2].G, `
            $probeSamples[0].B, $probeSamples[1].B, $probeSamples[2].B)
    }

    $ringCenterRadius = ($edge * (1.0 - 2.0 * $script:SM_LOGO_RING_INSET_RATIO)) / 2.0
    $ringWidth = $edge * $script:SM_LOGO_RING_WIDTH_RATIO
    $dotRadius = ($edge * $script:SM_LOGO_DOT_DIAMETER_RATIO) / 2.0

    Write-Host ''
    Write-Host ('--- {0} ---' -f $Label)
    Write-Host ('  raster           : {0} x {1}' -f $width, $height)
    Write-Host ('  corners          : {0}' -f ($cornerTexts -join '  '))
    Write-Host ('  corners alpha==0 : {0}' -f $cornersOk)
    Write-Host ('  diagonal probes  : {0}' -f ($probeTexts -join '  '))
    Write-Host ('  blue anchor ok   : {0}' -f $blueCoreOk)
    Write-Host ('  gradient rises   : {0} ({1})' -f $gradientOk, $gradientDetail)
    Write-Host ('  ring @ r={0:N2}px   : {1}' -f $ringRadius, ($ringTexts -join '  '))
    Write-Host ('  gap at 315/NE    : {0} (337.5deg and 292.5deg are not white)' -f $gapOk)
    Write-Host ('  arc covers rest  : {0}' -f $ringOk)
    Write-Host ('  gap dot          : ({0},{1})={2} isWhite={3}' -f $dotX, $dotY, (Format-SmRgba $dotPixel), $dotOk)
    Write-Host ('  white pixels     : {0}' -f $white)
    Write-Host ('  blue pixels      : {0}' -f $blue)
    Write-Host ('  A=0 pixels       : {0}' -f $transparent)
    Write-Host ('  edge/AA pixels   : {0}' -f $other)
    Write-Host ('  ring geometry    : centreline r={0:N2}px  stroke={1:N2}px  dotR={2:N2}px  arc start=0 sweep=+270 (GDI+ clockwise, gap at 315 = upper right)' -f $ringCenterRadius, $ringWidth, $dotRadius)

    return [pscustomobject]@{
        Label = $Label
        Width = $width
        Height = $height
        White = $white
        Blue = $blue
        Transparent = $transparent
        Other = $other
        CornersOk = $cornersOk
        BlueCoreOk = $blueCoreOk
        GradientOk = $gradientOk
        GradientDetail = $gradientDetail
        GapOk = $gapOk
        RingOk = $ringOk
        DotOk = $dotOk
        RingText = ($ringTexts -join '  ')
        DotText = ('({0},{1})={2}' -f $dotX, $dotY, (Format-SmRgba $dotPixel))
        CornerText = ($cornerTexts -join '  ')
        ProbeText = ($probeTexts -join '  ')
        RingCenterRadius = $ringCenterRadius
        RingWidth = $ringWidth
        DotRadius = $dotRadius
        FilterZeroRows = $Image.FilterZeroRows
    }
}

# ---------------------------------------------------------------------------
# Load the generator. Two steps, in this order:
#   1. evaluate the DEFINITIONS-only prefix of gen-logo.ps1 (everything above its
#      "# Main" banner) so its helpers - including Resolve-SmAssetsDirectory and the
#      PNG reader - exist;
#   2. evaluate the whole file against a throwaway Assets directory, which proves the
#      real entry point still runs end-to-end without touching the tracked assets.
# The source is read as TEXT and run through Invoke-Expression because this box has
# script execution disabled; dot-sourcing the file path is refused outright.
# ---------------------------------------------------------------------------
$genSource = ''
$cursor = (Get-Location).Path
for ($depth = 0; $depth -lt 6 -and -not $genSource; $depth++) {
    foreach ($relative in @('Code\scripts\gen-logo.ps1', 'scripts\gen-logo.ps1', 'SpaceMaid\Code\scripts\gen-logo.ps1')) {
        $candidate = Join-Path $cursor $relative
        if (Test-Path $candidate) {
            $genSource = $candidate
            break
        }
    }

    $parent = Split-Path -Parent $cursor
    if (-not $parent -or $parent -eq $cursor) {
        break
    }

    $cursor = $parent
}

if (-not $genSource) {
    throw 'Cannot find gen-logo.ps1: run this from the repo root (or from the SpaceMaid / Code directory).'
}

$genText = Get-Content -Raw -Encoding UTF8 $genSource
# Normalize line endings so the split below is independent of CRLF vs LF.
$genText = $genText.Replace("`r`n", "`n")

$mainBanner = '# Main'
$bannerIndex = $genText.IndexOf($mainBanner)
if ($bannerIndex -lt 0) {
    throw ('Could not find the main-block banner in {0}; the verifier cannot separate definitions from execution.' -f $genSource)
}

$bannerLineStart = $genText.LastIndexOf([char]10, $bannerIndex)
if ($bannerLineStart -lt 0) {
    throw ('Could not locate the line start of the main-block banner in {0}.' -f $genSource)
}

# Neutralize the param() block in the definitions prefix; we bind $AssetsDir ourselves.
$definitionsOnly = $genText.Substring(0, $bannerLineStart)
$definitionsOnly = $definitionsOnly -replace '(?m)^\s*param\(\[string\]\$AssetsDir\s*=\s*''''\)', ''

Invoke-Expression $definitionsOnly -ErrorAction Stop

if (-not (Get-Command New-SmLogoBitmap -ErrorAction SilentlyContinue)) {
    throw 'Could not load the shared helpers from gen-logo.ps1.'
}

# Resolve the REAL assets directory before the probe run: the probe assigns to
# $AssetsDir at global scope and would otherwise clobber our parameter.
$targetDir = $AssetsDir
if (-not $targetDir) {
    $targetDir = Resolve-SmAssetsDirectory
}

$probeDir = Join-Path ([System.IO.Path]::GetTempPath()) ('spacemaid-check-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $probeDir | Out-Null

$probeText = $genText -replace '(?m)^\s*param\(\[string\]\$AssetsDir\s*=\s*''''\)', ''
$probeText = ('$AssetsDir = ' + "'" + $probeDir + "'" + [Environment]::NewLine) + $probeText

try {
    Invoke-Expression $probeText -ErrorAction Stop
}
finally {
    Remove-Item -Recurse -Force -Path $probeDir -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host '=== verifying the tracked assets (the probe run above wrote to a temp directory) ==='

# ---------------------------------------------------------------------------
# Verify the tracked assets.
# ---------------------------------------------------------------------------
$results = New-Object 'System.Collections.Generic.List[object]'
foreach ($spec in @(
    @{ File = 'logo.png';    Size = 256 },
    @{ File = 'logo-64.png'; Size = 64 })) {
    $path = Join-Path $targetDir $spec.File
    if (-not (Test-Path $path)) {
        throw ('Missing asset: {0}' -f $path)
    }

    $fileInfo = Get-Item $path
    $image = Read-SmPngRgba -Path $path
    if ($image.Width -ne $spec.Size -or $image.Height -ne $spec.Size) {
        throw ('Unexpected raster for {0}: {1}x{2}' -f $spec.File, $image.Width, $image.Height)
    }

    $stats = Get-SmPixelStats -Image $image -Label $spec.File
    $stats | Add-Member -NotePropertyName 'Length' -NotePropertyValue ([int64]$fileInfo.Length)
    $stats | Add-Member -NotePropertyName 'Path' -NotePropertyValue $path
    $stats | Add-Member -NotePropertyName 'ExpectedSize' -NotePropertyValue $spec.Size

    # Cross-check against the generator's own in-memory bitmap: same counts means the
    # on-disk PNG really is what the drawing code produced (no encode/scale surprise).
    $drawn = New-SmLogoBitmap -Size $spec.Size
    $drawnWhite = 0
    $drawnBlue = 0
    for ($y = 0; $y -lt $spec.Size; $y++) {
        for ($x = 0; $x -lt $spec.Size; $x++) {
            $pixel = $drawn.GetPixel($x, $y)
            if ($pixel.A -eq 0) { continue }
            if ($pixel.R -gt 240 -and $pixel.G -gt 240 -and $pixel.B -gt 240 -and $pixel.A -gt 200) { $drawnWhite++ }
            elseif ($pixel.B -gt $pixel.R -and $pixel.A -gt 200) { $drawnBlue++ }
        }
    }
    $drawn.Dispose()

    $stats | Add-Member -NotePropertyName 'DrawnWhite' -NotePropertyValue $drawnWhite
    $stats | Add-Member -NotePropertyName 'DrawnBlue' -NotePropertyValue $drawnBlue

    Write-Host ('  file bytes       : {0} (on disk {1})' -f $stats.Length, $fileInfo.Length)
    Write-Host ('  PNG decode       : {0} of {1} rows use filter 0, 8-bit RGBA (colour type 6)' -f $stats.FilterZeroRows, $spec.Size)
    Write-Host ('  drawn-vs-disk    : white {0}/{1}, blue {2}/{3}' -f $stats.White, $drawnWhite, $stats.Blue, $drawnBlue)
    $results.Add($stats)
}

Write-Host ''
Write-Host '=== checks ==='

$checks = New-Object 'System.Collections.Generic.List[object]'

foreach ($result in $results) {
    $checks.Add(@{
        Name = ('{0}: four corners have Alpha == 0' -f $result.Label)
        Ok = $result.CornersOk
        Detail = $result.CornerText
    })
}

foreach ($result in $results) {
    $checks.Add(@{
        Name = ('{0}: raster is {1}x{1}' -f $result.Label, $result.ExpectedSize)
        Ok = ($result.Width -eq $result.ExpectedSize -and $result.Height -eq $result.ExpectedSize)
        Detail = ('{0}x{1}' -f $result.Width, $result.Height)
    })
}

foreach ($result in $results) {
    $checks.Add(@{
        Name = ('{0}: upper-left interior is blue (B > R, not pure white)' -f $result.Label)
        Ok = $result.BlueCoreOk
        Detail = $result.ProbeText
    })
}

foreach ($result in $results) {
    $checks.Add(@{
        Name = ('{0}: gradient runs top-left -> bottom-right' -f $result.Label)
        Ok = $result.GradientOk
        Detail = $result.GradientDetail
    })
}

foreach ($result in $results) {
    $checks.Add(@{
        Name = ('{0}: 270-degree arc covers everything except the upper-right gap' -f $result.Label)
        Ok = ($result.RingOk -and $result.GapOk)
        Detail = $result.RingText
    })
}

foreach ($result in $results) {
    $checks.Add(@{
        Name = ('{0}: white dot sits in the ring gap' -f $result.Label)
        Ok = $result.DotOk
        Detail = $result.DotText
    })
}

foreach ($result in $results) {
    $checks.Add(@{
        Name = ('{0}: white pixels exist (>= 300)' -f $result.Label)
        Ok = ($result.White -ge 300)
        Detail = ('{0} white px' -f $result.White)
    })
}

$white256Low = 2000
$white256High = 20000
$checks.Add(@{
    Name = ('logo.png white pixel count within [{0}, {1}]' -f $white256Low, $white256High)
    Ok = ($results[0].White -ge $white256Low -and $results[0].White -le $white256High)
    Detail = ('{0} white px' -f $results[0].White)
})

# A genuine 64 px re-draw of the same motif covers 1/16 of the area, so its white
# count should land near 1/16 of the 256 px count. The band is deliberately wide:
# at 64 px the 4.8 px stroke is only a handful of pixels thick, so the anti-aliased
# boundary dominates and the integer count is coarse.
$white64Low = 125
$white64High = 1250
$checks.Add(@{
    Name = ('logo-64.png white pixel count within [{0}, {1}]' -f $white64Low, $white64High)
    Ok = ($results[1].White -ge $white64Low -and $results[1].White -le $white64High)
    Detail = ('{0} white px' -f $results[1].White)
})

$white64Expected = [Math]::Round($results[0].White / 16.0, 1)
$white64Ratio = [Math]::Round($results[1].White / [double]$results[0].White, 4)
$checks.Add(@{
    Name = 'logo-64.png white count tracks the 256 px count at ~1/16 (re-draw, not a resample)'
    Ok = ($white64Ratio -ge 0.03 -and $white64Ratio -le 0.09)
    Detail = ('{0}/{1} = {2} (ideal {3})' -f $results[1].White, $results[0].White, $white64Ratio, $white64Expected)
})

$opaque256 = 256 * 256 - $results[0].Transparent
$blueShare = [Math]::Round(100.0 * $results[0].Blue / $opaque256, 2)
$checks.Add(@{
    Name = 'the rounded square is the dominant colour (blue > 70% of opaque pixels)'
    Ok = ($blueShare -gt 70.0)
    Detail = ('{0}% blue ({1} of {2} opaque px)' -f $blueShare, $results[0].Blue, $opaque256)
})

$propOk = $true
$propDetail = New-Object 'System.Collections.Generic.List[string]'
foreach ($result in $results) {
    $unitRing = [Math]::Round($result.RingWidth / $result.Width, 4)
    $unitDot = [Math]::Round(($result.DotRadius * 2.0) / $result.Width, 4)
    $unitInset = [Math]::Round((($result.Width / 2.0) - $result.RingCenterRadius) / $result.Width, 4)
    $propDetail.Add(('{0}: ring={1} dot={2} inset={3}' -f $result.Label, $unitRing, $unitDot, $unitInset))
    if ([Math]::Abs($unitRing - 0.075) -gt 0.0001) { $propOk = $false }
    if ([Math]::Abs($unitDot - 0.08) -gt 0.0001) { $propOk = $false }
    if ([Math]::Abs($unitInset - 0.24) -gt 0.0001) { $propOk = $false }
}
$checks.Add(@{
    Name = 'label proportions are edge-relative identical at both sizes (ring 7.5%, dot 8%, inset 24%)'
    Ok = $propOk
    Detail = ($propDetail -join '; ')
})

$parityOk = ($results[0].White -eq $results[0].DrawnWhite -and $results[0].Blue -eq $results[0].DrawnBlue -and
            $results[1].White -eq $results[1].DrawnWhite -and $results[1].Blue -eq $results[1].DrawnBlue)
$checks.Add(@{
    Name = 'on-disk PNG pixel counts match the drawing code byte-for-byte'
    Ok = $parityOk
    Detail = ('white {0}/{1} {2}/{3}, blue {4}/{5} {6}/{7}' -f `
        $results[0].White, $results[0].DrawnWhite, $results[1].White, $results[1].DrawnWhite, `
        $results[0].Blue, $results[0].DrawnBlue, $results[1].Blue, $results[1].DrawnBlue)
})

$failed = 0
foreach ($check in $checks) {
    $state = if ($check.Ok) { 'PASS' } else { 'FAIL' }
    if (-not $check.Ok) { $failed++ }
    Write-Host ('  [{0}] {1} :: {2}' -f $state, $check.Name, $check.Detail)
}

Write-Host ''
if ($failed -eq 0) {
    Write-Host ('[PASS] all {0} logo checks passed' -f $checks.Count)
}
else {
    Write-Host ('[FAIL] {0} of {1} logo checks failed' -f $failed, $checks.Count)
}
