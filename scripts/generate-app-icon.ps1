# Created: 2026-09-07
# Function: Generate the branded multi-size PI-Harness Windows icon.
# Purpose: Keep the application icon deterministic without third-party tools.

param(
    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot 'src\PIHarness.App\Assets\PI-Harness.ico'
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

Add-Type -AssemblyName PresentationCore

function New-IconBitmap([int]$size) {
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    $scale = [double]$size
    $darkBrush = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromRgb(24, 23, 26))
    $accentBrush = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromRgb(109, 96, 232))
    $whiteBrush = [System.Windows.Media.Brushes]::White
    $drawing.DrawRoundedRectangle(
        $darkBrush,
        $null,
        [System.Windows.Rect]::new(0.04 * $scale, 0.04 * $scale, 0.92 * $scale, 0.92 * $scale),
        0.18 * $scale,
        0.18 * $scale)
    $drawing.DrawRoundedRectangle(
        $accentBrush,
        $null,
        [System.Windows.Rect]::new(0.16 * $scale, 0.16 * $scale, 0.68 * $scale, 0.68 * $scale),
        0.15 * $scale,
        0.15 * $scale)
    $drawing.DrawRectangle($whiteBrush, $null, [System.Windows.Rect]::new(0.28 * $scale, 0.30 * $scale, 0.44 * $scale, 0.09 * $scale))
    $drawing.DrawRectangle($whiteBrush, $null, [System.Windows.Rect]::new(0.34 * $scale, 0.35 * $scale, 0.09 * $scale, 0.35 * $scale))
    $drawing.DrawRectangle($whiteBrush, $null, [System.Windows.Rect]::new(0.57 * $scale, 0.35 * $scale, 0.09 * $scale, 0.35 * $scale))
    $drawing.Close()

    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new(
        $size,
        $size,
        96,
        96,
        [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [System.IO.MemoryStream]::new()
    $encoder.Save($stream)
    $bytes = $stream.ToArray()
    $stream.Dispose()
    Write-Output -NoEnumerate $bytes
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = @($sizes | ForEach-Object { New-IconBitmap $_ })
$fileStream = [System.IO.File]::Create($OutputPath)
$fileWriter = [System.IO.BinaryWriter]::new($fileStream)
try {
    $fileWriter.Write([uint16]0)
    $fileWriter.Write([uint16]1)
    $fileWriter.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $sizeByte = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $fileWriter.Write([byte]$sizeByte)
        $fileWriter.Write([byte]$sizeByte)
        $fileWriter.Write([byte]0)
        $fileWriter.Write([byte]0)
        $fileWriter.Write([uint16]1)
        $fileWriter.Write([uint16]32)
        $fileWriter.Write([int]$images[$index].Length)
        $fileWriter.Write([int]$offset)
        $offset += $images[$index].Length
    }
    foreach ($image in $images) {
        $fileWriter.Write($image)
    }
}
finally {
    $fileWriter.Dispose()
    $fileStream.Dispose()
}

Write-Output "Generated icon: $OutputPath"
