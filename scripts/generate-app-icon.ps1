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

function Test-RoundedRect([double]$x, [double]$y, [double]$left, [double]$top, [double]$right, [double]$bottom, [double]$radius) {
    if ($x -lt $left -or $x -gt $right -or $y -lt $top -or $y -gt $bottom) {
        return $false
    }
    $centerX = [Math]::Min([Math]::Max($x, $left + $radius), $right - $radius)
    $centerY = [Math]::Min([Math]::Max($y, $top + $radius), $bottom - $radius)
    $dx = $x - $centerX
    $dy = $y - $centerY
    return ($dx * $dx + $dy * $dy) -le ($radius * $radius)
}

function New-IconBitmap([int]$size) {
    $maskStride = [int](([Math]::Ceiling($size / 32.0)) * 4)
    $pixelBytes = $size * $size * 4
    $stream = [System.IO.MemoryStream]::new()
    $writer = [System.IO.BinaryWriter]::new($stream)
    $writer.Write([int]40)
    $writer.Write([int]$size)
    $writer.Write([int]($size * 2))
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([int]0)
    $writer.Write([int]$pixelBytes)
    $writer.Write([int]0)
    $writer.Write([int]0)
    $writer.Write([int]0)
    $writer.Write([int]0)

    for ($row = $size - 1; $row -ge 0; $row--) {
        for ($column = 0; $column -lt $size; $column++) {
            $x = ($column + 0.5) / $size
            $y = ($row + 0.5) / $size
            if (-not (Test-RoundedRect $x $y 0.04 0.04 0.96 0.96 0.18)) {
                $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0)
            }
            else {
                $r = 24; $g = 23; $b = 26
                if (Test-RoundedRect $x $y 0.16 0.16 0.84 0.84 0.15) {
                    $r = 109; $g = 96; $b = 232
                }
                $isTop = $x -ge 0.28 -and $x -le 0.72 -and $y -ge 0.30 -and $y -le 0.39
                $isLeft = $x -ge 0.34 -and $x -le 0.43 -and $y -ge 0.35 -and $y -le 0.70
                $isRight = $x -ge 0.57 -and $x -le 0.66 -and $y -ge 0.35 -and $y -le 0.70
                if ($isTop -or $isLeft -or $isRight) {
                    $r = 255; $g = 255; $b = 255
                }
                $writer.Write([byte]$b)
                $writer.Write([byte]$g)
                $writer.Write([byte]$r)
                $writer.Write([byte]255)
            }
        }
    }

    for ($index = 0; $index -lt ($maskStride * $size); $index++) {
        $writer.Write([byte]0)
    }
    $writer.Flush()
    $bytes = $stream.ToArray()
    $writer.Dispose()
    $stream.Dispose()
    return $bytes
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
