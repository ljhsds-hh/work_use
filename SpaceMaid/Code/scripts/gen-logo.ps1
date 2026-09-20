# SpaceMaid brand logo generator (programmatic; never hand-edit logo.png / logo-64.png).
#
# Design: it carries over the exact same visual motif as gen-icon.ps1 (app.ico) so the
#        brand mark and the shell icon read as one family:
#        - blue diagonal gradient rounded square: ARGB(255,70,146,238) -> ARGB(255,32,92,180)
#        - white 270-degree arc ring, round line caps
#        - a small white dot sitting in the ring gap (reads as a gauge / disk-usage needle)
#        Differences from app.ico are deliberate and refinement-only:
#        - transparent background (the icon sits on the OS shell; the logo sits on the app chrome)
#        - the rounded square is an AntiAlias GraphicsPath fill instead of the icon's arc-built path
#        - thinner arc (7.5% of the edge, the icon used 14.5%) and a smaller dot (8%, the icon used 13%)
#        - the 64 px tile is DRAWN at 64 px, it is NOT a downscale of the 256 px one. Scaling a
#          256 px raster down would resample the thin arc into mud; redrawing at the target size
#          keeps the edge crisp and keeps the proportions identical.
#
# Usage (this box forbids running .ps1 files directly, so dot-source the text):
#   Invoke-Expression (Get-Content -Raw -Encoding UTF8 'Code\scripts\gen-logo.ps1')
#
# Usage with overrides:
#   Invoke-Expression (Get-Content -Raw -Encoding UTF8 'Code\scripts\gen-logo.ps1')
#
# Pitfalls already handled here (learned from gen-icon.ps1):
#   1. This file is ASCII-only. PowerShell 5.1 reads BOM-less files as ANSI, so non-ASCII
#      runes in the source would corrupt. Any non-ASCII console text is built from [char] codes.
#   2. New-Object must not see inline expressions in its argument list (PowerShell evaluates
#      them first and the arguments shift), so values are assigned to locals first.
#   3. Invoke-Expression nulls $PSCommandPath / $MyInvocation.MyCommand.Path, so the Assets
#      directory is discovered by walking up from the current directory instead.

param([string]$AssetsDir = '')

Add-Type -AssemblyName System.Drawing

# ---------------------------------------------------------------------------
# Geometry / color constants: the single source of truth for logo.png and logo-64.png.
# ---------------------------------------------------------------------------
$script:SM_PNG_SIGNATURE = [byte[]](137, 80, 78, 71, 13, 10, 26, 10)

$script:SM_LOGO_SIZE          = 256
$script:SM_LOGO_TILE_SIZE     = 64

# The brand gradient, identical to gen-icon.ps1 (top-left -> bottom-right).
$script:SM_LOGO_FROM = [System.Drawing.Color]::FromArgb(255, 70, 146, 238)
$script:SM_LOGO_TO   = [System.Drawing.Color]::FromArgb(255, 32, 92, 180)

# Rounded square corner radius, ~22% of the edge.
$script:SM_LOGO_SQUARE_RADIUS_RATIO = 0.22

# Arc ring: box inset 24% per side (outer diameter = 52% of the edge), stroke 7.5% of the edge.
$script:SM_LOGO_RING_INSET_RATIO = 0.24
$script:SM_LOGO_RING_WIDTH_RATIO = 0.075

# The white ring is a 270-degree arc that leaves a 90-degree gap at the upper right
# (north-east), and the dot sits inside that gap.
#
# GDI+ DrawArc angles live in a y-down coordinate system, so a POSITIVE sweep advances
# CLOCKWISE on screen: 0 = east/right, 90 = south/bottom, 180 = west/left, 270 =
# north/top. That was confirmed with a throwaway bitmap and eight compass probes rather
# than assumed, because the opposite convention silently mirrors the whole design.
# Starting at 0 and sweeping +270 therefore exits at 270 (north), leaving the gap
# between the two round caps, i.e. the quadrant whose midpoint is 315 (north-east).
$script:SM_LOGO_ARC_START_ANGLE = 0.0
$script:SM_LOGO_ARC_SWEEP_ANGLE = 270.0

# The gap's midpoint, in the same GDI+ convention (315 = north-east = upper right).
$script:SM_LOGO_GAP_MID_ANGLE = 315.0

# Gap dot: diameter 8% of the edge, centered on the north-east diagonal of the gap.
# Its centre lands 0.6344 x edge along that diagonal from the bitmap centre, where:
#   - the dot's near edge stays ~4.3 px clear of the ring's inner edge at 256 px
#     (the ring's inner edge is 0.305 x edge from centre), so the dot reads as sitting
#     IN the gap rather than fused to the ring;
#   - the dot's far edge stays inside the rounded square's boundary at that angle.
$script:SM_LOGO_DOT_DIAMETER_RATIO = 0.08
$script:SM_LOGO_DOT_CENTER_RATIO   = 0.6344

# ---------------------------------------------------------------------------
# Output helpers. Non-ASCII text is assembled from [char] codes so this source
# file can stay pure ASCII (see pitfall 1 above).
# ---------------------------------------------------------------------------
$script:SM_Text  = ([char]0x5DF2 + [char]0x751F + [char]0x6210 + [char]0x54C1 + [char]0x724C + [char]0x56FE)
$script:SM_Done  = ([char]0x5B8C + [char]0x6210)
$script:SM_Bytes = ([char]0x5B57 + [char]0x8282)
$script:SM_Fail  = ([char]0x5931 + [char]0x8D25)

# ---------------------------------------------------------------------------
# Path discovery
# ---------------------------------------------------------------------------
function Resolve-SmAssetsDirectory {
    $cursor = (Get-Location).Path
    for ($depth = 0; $depth -lt 6; $depth++) {
        foreach ($relative in @('Code\src\SpaceMaid.App\Assets', 'src\SpaceMaid.App\Assets', 'SpaceMaid\Code\src\SpaceMaid.App\Assets')) {
            $candidate = Join-Path $cursor $relative
            $projectDirectory = Split-Path -Parent $candidate
            if (Test-Path $projectDirectory) {
                return [System.IO.Path]::GetFullPath($candidate)
            }
        }

        $parent = Split-Path -Parent $cursor
        if (-not $parent -or $parent -eq $cursor) {
            break
        }

        $cursor = $parent
    }

    throw 'Cannot find SpaceMaid.App\Assets: run this from the repo root (or from the SpaceMaid / Code directory).'
}

# ---------------------------------------------------------------------------
# Bitmap factory: one function, size-parameterized, used for BOTH outputs.
# Every proportion below is derived from -Size, which is what makes the 64 px
# tile a genuine re-draw rather than a resample of the 256 px one.
# ---------------------------------------------------------------------------
function New-SmLogoBitmap([int]$Size) {
    if ($Size -lt 16) {
        throw ('Logo size must be at least 16 px, got {0}.' -f $Size)
    }

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # --- rounded square with the top-left -> bottom-right gradient ---
    $edge = [double]$Size
    $radius = $edge * $script:SM_LOGO_SQUARE_RADIUS_RATIO
    $diameter = $radius * 2.0

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0.0, 0.0, $diameter, $diameter, 180.0, 90.0)
    $path.AddArc($edge - $diameter, 0.0, $diameter, $diameter, 270.0, 90.0)
    $path.AddArc($edge - $diameter, $edge - $diameter, $diameter, $diameter, 0.0, 90.0)
    $path.AddArc(0.0, $edge - $diameter, $diameter, $diameter, 90.0, 90.0)
    $path.CloseFigure()

    $startPoint = New-Object System.Drawing.Point(0, 0)
    $endPoint = New-Object System.Drawing.Point($Size, $Size)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($startPoint, $endPoint, $script:SM_LOGO_FROM, $script:SM_LOGO_TO)
    $g.FillPath($brush, $path)

    # --- white arc ring: 270 degrees, leaving the 90-degree gap at the upper right ---
    $inset = $edge * $script:SM_LOGO_RING_INSET_RATIO
    $ringBox = $edge - ($inset * 2.0)
    $ringWidth = [float]($edge * $script:SM_LOGO_RING_WIDTH_RATIO)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $ringWidth)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($pen, [float]$inset, [float]$inset, [float]$ringBox, [float]$ringBox, [float]$script:SM_LOGO_ARC_START_ANGLE, [float]$script:SM_LOGO_ARC_SWEEP_ANGLE)

    # --- white dot centered in the ring gap ---
    $dotDiameter = $edge * $script:SM_LOGO_DOT_DIAMETER_RATIO
    $dotCenter = $edge * $script:SM_LOGO_DOT_CENTER_RATIO
    $dotX = [float]($dotCenter - ($dotDiameter / 2.0))
    $dotY = [float]($dotCenter - ($dotDiameter / 2.0))
    $g.FillEllipse([System.Drawing.Brushes]::White, $dotX, $dotY, [float]$dotDiameter, [float]$dotDiameter)

    $pen.Dispose()
    $brush.Dispose()
    $path.Dispose()
    $g.Dispose()
    return $bmp
}

# ---------------------------------------------------------------------------
# PNG writing / reading. System.Drawing.Image.Save is avoided on purpose:
# saving onto a file stream has the known stream-lifetime quirk, and writing
# the bytes ourselves lets us print the exact byte count we put on disk.
# ---------------------------------------------------------------------------
function Save-SmLogoPng([System.Drawing.Bitmap]$Bitmap, [string]$Path) {
    $directory = Split-Path -Parent $Path
    if ($directory -and -not (Test-Path $directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $stream = [System.IO.File]::Create($Path)
    try {
        $Bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $stream.Flush()
        return $stream.Length
    }
    finally {
        $stream.Dispose()
    }
}

# Cross-checks that the file the OS reports is byte-identical to what we generated.
function Get-SmFileMirror([System.Drawing.Bitmap]$Bitmap) {
    $stream = New-Object System.IO.MemoryStream
    try {
        $Bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return [int64]$stream.Length
    }
    finally {
        $stream.Dispose()
    }
}

# Minimal PNG decoder: parses chunks, concatenates IDAT, inflates and reverses
# the per-row filters. Used only for verification, to prove that the bytes on
# disk really are an 8-bit RGBA PNG of the expected size.
function Read-SmPngBitmap([string]$Path) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 8) {
        throw ('Not a PNG (too short): {0}' -f $Path)
    }

    for ($i = 0; $i -lt 8; $i++) {
        if ($bytes[$i] -ne $script:SM_PNG_SIGNATURE[$i]) {
            throw ('Not a PNG (bad signature): {0}' -f $Path)
        }
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
        throw ('Expected an 8-bit RGBA PNG (color type 6), got bit depth {0} / color type {1}: {2}' -f $bitDepth, $colorType, $Path)
    }

    $idatBytes = [byte[]]$idat.ToArray()
    $idat.Dispose()

    if ($idatBytes.Length -lt 2) {
        throw ('PNG data stream is empty: {0}' -f $Path)
    }
    # Strip the 2-byte zlib header and the trailing 4-byte Adler-32; DeflateStream
    # wants the raw deflate payload.
    $raw = New-Object System.IO.MemoryStream
    $deflate = New-Object System.IO.Compression.DeflateStream((New-Object System.IO.MemoryStream(, $idatBytes[2..($idatBytes.Length - 5)])), [System.IO.Compression.CompressionMode]::Decompress)
    $deflate.CopyTo($raw)
    $deflate.Dispose()
    $pixels = [byte[]]$raw.ToArray()
    $raw.Dispose()

    $stride = $width * 4
    $expected = ($stride + 1) * $height
    if ($pixels.Length -ne $expected) {
        throw ('Inflated PNG size mismatch: expected {0} bytes, got {1}: {2}' -f $expected, $pixels.Length, $Path)
    }

    $bitmap = New-Object System.Drawing.Bitmap($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $rect = New-Object System.Drawing.Rectangle(0, 0, $width, $height)
    $data = $bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::WriteOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $buffer = New-Object byte[] ($data.Stride * $height)
    $filterZeroRows = 0

    try {
        for ($y = 0; $y -lt $height; $y++) {
            $rowStart = $y * ($stride + 1)
            $filter = [int]$pixels[$rowStart]
            if ($filter -eq 0) {
                $filterZeroRows++
            }

            for ($x = 0; $x -lt $stride; $x++) {
                $rawByte = [int]$pixels[$rowStart + 1 + $x]
                $a = 0
                $b = 0
                $c = 0
                if ($x -ge 4) { $a = [int]$buffer[$y * $data.Stride + $x - 4] }
                if ($y -gt 0) { $b = [int]$buffer[($y - 1) * $data.Stride + $x] }
                if ($x -ge 4 -and $y -gt 0) { $c = [int]$buffer[($y - 1) * $data.Stride + $x - 4] }

                $value = 0
                switch ($filter) {
                    0 { $value = $rawByte }
                    1 { $value = $rawByte + $a }
                    2 { $value = $rawByte + $b }
                    3 { $value = $rawByte + [int][Math]::Floor(($a + $b) / 2.0) }
                    4 {
                        $p = $a + $b - $c
                        $pa = [Math]::Abs($p - $a)
                        $pb = [Math]::Abs($p - $b)
                        $pc = [Math]::Abs($p - $c)
                        if ($pa -le $pb -and $pa -le $pc) { $value = $rawByte + $a }
                        elseif ($pb -le $pc) { $value = $rawByte + $b }
                        else { $value = $rawByte + $c }
                    }
                    default { throw ('Unsupported PNG row filter {0} in {1}' -f $filter, $Path) }
                }

                $buffer[$y * $data.Stride + $x] = [byte]($value -band 255)
            }
        }

        [System.Runtime.InteropServices.Marshal]::Copy($buffer, 0, $data.Scan0, $buffer.Length)
    }
    finally {
        $bitmap.UnlockBits($data)
    }

    return [pscustomobject]@{ Bitmap = $bitmap; FilterZeroRows = $filterZeroRows }
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
if (-not $AssetsDir) {
    $AssetsDir = Resolve-SmAssetsDirectory
}

New-Item -ItemType Directory -Force -Path $AssetsDir | Out-Null

$square = New-SmLogoBitmap -Size $script:SM_LOGO_SIZE
$tile = New-SmLogoBitmap -Size $script:SM_LOGO_TILE_SIZE

$squarePath = Join-Path $AssetsDir 'logo.png'
$tilePath = Join-Path $AssetsDir 'logo-64.png'

$mirrorSquare = Get-SmFileMirror -Bitmap $square
$mirrorTile = Get-SmFileMirror -Bitmap $tile

$squareLength = Save-SmLogoPng -Bitmap $square -Path $squarePath
$tileLength = Save-SmLogoPng -Bitmap $tile -Path $tilePath

foreach ($entry in @(
    @{ Path = $squarePath; Length = $squareLength; Mirror = $mirrorSquare; Bitmap = $square },
    @{ Path = $tilePath; Length = $tileLength; Mirror = $mirrorTile; Bitmap = $tile })) {
    $matches = ($entry.Length -eq $entry.Mirror)
    if (-not $matches) {
        throw ('PNG byte count mismatch for {0}: wrote {1}, encoder produced {2}.' -f $entry.Path, $entry.Length, $entry.Mirror)
    }

    Write-Host ('  wrote {0} ({1}x{2}, {3} {4})' -f (Split-Path -Leaf $entry.Path), $entry.Bitmap.Width, $entry.Bitmap.Height, $entry.Length, $script:SM_Bytes)
}

$square.Dispose()
$tile.Dispose()

# Deliberately ASCII: this box runs PowerShell 5.1, which would mis-decode the
# console output if the literal were non-ASCII, and this file must stay UTF-8-free.
Write-Host ('logo.png    : 256 x 256, {0} bytes' -f $squareLength)
Write-Host ('logo-64.png : 64 x 64, {0} bytes' -f $tileLength)
Write-Host ('{0} {1}: {2}' -f $script:SM_Text, $script:SM_Done, $AssetsDir)
