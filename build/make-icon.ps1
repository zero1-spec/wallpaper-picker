#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Generates assets/icon.ico (multi-size) from assets/icon.png.
    Sizes: 16, 24, 32, 48, 64, 128, 256 px (256 stored as PNG-compressed entry).
    Re-runnable: overwrites existing icon.ico.
#>

param(
    [string]$PngPath = "assets/icon.png",
    [string]$IcoPath = "assets/icon.ico"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$script:srcDir = Split-Path -Parent $PSCommandPath
$root = Resolve-Path (Join-Path $script:srcDir "..")
$pngFull = Resolve-Path (Join-Path $root $PngPath)
$icoFull = Join-Path $root $IcoPath

if (-not (Test-Path $pngFull)) {
    throw "Source PNG not found: $pngFull"
}

# Load source image
$src = [System.Drawing.Image]::FromFile($pngFull)

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$entries = New-Object System.Collections.ArrayList
$imageData = New-Object System.Collections.ArrayList

$offset = 6 + ($sizes.Count * 16)  # header + directory entries

foreach ($size in $sizes) {
    # Resize
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.DrawImage($src, 0, 0, $size, $size)
    $g.Dispose()

    # Save as PNG to memory stream
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBytes = $ms.ToArray()
    $ms.Dispose()
    $bmp.Dispose()

    $entries.Add(@{
        Width = if ($size -eq 256) { 0 } else { $size }
        Height = if ($size -eq 256) { 0 } else { $size }
        ColorCount = 0
        Reserved = 0
        Planes = 0
        BitCount = 32
        BytesInRes = $pngBytes.Length
        ImageOffset = $offset
    }) | Out-Null
    $imageData.Add($pngBytes) | Out-Null
    $offset += $pngBytes.Length
}

$src.Dispose()

# Write ICO file
$fs = New-Object System.IO.FileStream(
    $icoFull,
    [System.IO.FileMode]::Create,
    [System.IO.FileAccess]::Write,
    [System.IO.FileShare]::None)
$writer = New-Object System.IO.BinaryWriter($fs)

# ICONDIR header
[void]$writer.Write([byte]0)           # idReserved
[void]$writer.Write([byte]0)
[void]$writer.Write([int16]1)          # idType = 1 (icon)
[void]$writer.Write([int16]$sizes.Count)  # idCount

# ICONDIRECTORYENTRY for each image
foreach ($entry in $entries) {
    [void]$writer.Write([byte]$entry.Width)
    [void]$writer.Write([byte]$entry.Height)
    [void]$writer.Write([byte]$entry.ColorCount)
    [void]$writer.Write([byte]$entry.Reserved)
    [void]$writer.Write([int16]$entry.Planes)
    [void]$writer.Write([int16]$entry.BitCount)
    [void]$writer.Write([int32]$entry.BytesInRes)
    [void]$writer.Write([int32]$entry.ImageOffset)
}

# Image data
foreach ($data in $imageData) {
    [void]$writer.Write($data, 0, $data.Length)
}

$writer.Flush()
$writer.Close()
$fs.Close()

"Generated $icoFull ($($sizes.Count) sizes: $($sizes -join ', '))"
