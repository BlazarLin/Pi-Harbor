# Created: 2026-09-07
# Function: Generate the branded multi-size Pi Harbor Windows icon.
# Purpose: Keep the application icon deterministic without third-party tools.

param(
    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot 'src\PIHarness.App\Assets\Pi-Harbor.ico'
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

Add-Type -AssemblyName PresentationCore

function New-IconBitmap([int]$size) {
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    $scale = [double]$size
    $accentBrush = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromRgb(125, 115, 234))
    $whiteBrush = [System.Windows.Media.Brushes]::White
    $drawing.DrawRoundedRectangle(
        $accentBrush,
        $null,
        [System.Windows.Rect]::new(0.025 * $scale, 0.025 * $scale, 0.95 * $scale, 0.95 * $scale),
        0.08 * $scale,
        0.08 * $scale)
    $bottom = if ($size -lt 32) { .73 } else { .62 }
    $drawing.DrawRectangle($whiteBrush, $null, [System.Windows.Rect]::new(.21 * $scale, .21 * $scale, .57 * $scale, .105 * $scale))
    $drawing.DrawRectangle($whiteBrush, $null, [System.Windows.Rect]::new(.30 * $scale, .27 * $scale, .105 * $scale, ($bottom-.27) * $scale))
    $drawing.DrawRectangle($whiteBrush, $null, [System.Windows.Rect]::new(.59 * $scale, .27 * $scale, .105 * $scale, ($bottom-.27) * $scale))
    if ($size -ge 32) {
        $drawing.PushClip([Windows.Media.RectangleGeometry]::new([Windows.Rect]::new(.025*$scale,.025*$scale,.95*$scale,.95*$scale),.08*$scale,.08*$scale))
        $band = [Windows.Media.SolidColorBrush]::new([Windows.Media.Color]::FromRgb(80,68,168))
        $drawing.DrawRectangle($band,$null,[Windows.Rect]::new(.025*$scale,.70*$scale,.95*$scale,.275*$scale))
        $drawing.Pop()
        $typeface = [Windows.Media.Typeface]::new([Windows.Media.FontFamily]::new('Segoe UI'),[Windows.FontStyles]::Italic,[Windows.FontWeights]::SemiBold,[Windows.FontStretches]::Normal)
        $label = [Windows.Media.FormattedText]::new('harbor',[Globalization.CultureInfo]::InvariantCulture,[Windows.FlowDirection]::LeftToRight,$typeface,.20*$scale,$whiteBrush,1.0)
        $drawing.DrawText($label,[Windows.Point]::new(.31*$scale,.72*$scale))
    }
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
