# Deterministic vector-drawn icon candidates. Does not replace the selected application icon.
param([string]$OutputDirectory = 'artifacts/icon-proposals')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationCore
$outputDir=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
function Brush([string]$hex) { return [Windows.Media.BrushConverter]::new().ConvertFromString($hex) }
function Label($dc, [string]$text, [double]$size, [double]$x, [double]$y, [string]$color, [bool]$italic=$false) {
    $style=if($italic){[Windows.FontStyles]::Italic}else{[Windows.FontStyles]::Normal}
    $face=[Windows.Media.Typeface]::new([Windows.Media.FontFamily]::new('Segoe UI'),$style,[Windows.FontWeights]::SemiBold,[Windows.FontStretches]::Normal)
    $formatted=[Windows.Media.FormattedText]::new($text,[Globalization.CultureInfo]::InvariantCulture,[Windows.FlowDirection]::LeftToRight,$face,$size,(Brush $color),1.0)
    $dc.DrawText($formatted,[Windows.Point]::new($x,$y))
}
function Icon([string]$variant,[int]$size) {
    $visual=[Windows.Media.DrawingVisual]::new(); $dc=$visual.RenderOpen()
    $s=[double]$size; $purple=Brush '#7D73EA'; $white=Brush '#FFFFFF'
    $radius=if($variant -eq 'B'){.08*$s}else{.18*$s}
    $dc.DrawRoundedRectangle($purple,$null,[Windows.Rect]::new(.025*$s,.025*$s,.95*$s,.95*$s),$radius,$radius)
    # Preserve the recognizable white Pi mark while removing the old dark outer frame.
    $bottom=if($size -lt 32){.73}else{.62}
    $dc.DrawRectangle($white,$null,[Windows.Rect]::new(.21*$s,.21*$s,.57*$s,.105*$s))
    $dc.DrawRectangle($white,$null,[Windows.Rect]::new(.30*$s,.27*$s,.105*$s,($bottom-.27)*$s))
    $dc.DrawRectangle($white,$null,[Windows.Rect]::new(.59*$s,.27*$s,.105*$s,($bottom-.27)*$s))
    if($size -ge 32) {
        if($variant -eq 'A') {
            Label $dc 'harbor' (.17*$s) (.39*$s) (.73*$s) '#FFFFFF' $true
        } elseif($variant -eq 'B') {
            $dc.PushClip([Windows.Media.RectangleGeometry]::new([Windows.Rect]::new(.025*$s,.025*$s,.95*$s,.95*$s),$radius,$radius))
            $dc.DrawRectangle((Brush '#5044A8'),$null,[Windows.Rect]::new(.025*$s,.70*$s,.95*$s,.275*$s))
            $dc.Pop()
            Label $dc 'harbor' (.20*$s) (.31*$s) (.72*$s) '#FFFFFF' $true
        } else {
            $dc.DrawRoundedRectangle($white,$null,[Windows.Rect]::new(.27*$s,.70*$s,.72*$s,.275*$s),.055*$s,.055*$s)
            Label $dc 'harbor' (.20*$s) (.315*$s) (.72*$s) '#6658C8' $true
        }
    }
    $dc.Close()
    $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new($size,$size,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual); $bitmap.Freeze(); return $bitmap
}
function PngBytes($bitmap) {
    $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream=[IO.MemoryStream]::new(); $encoder.Save($stream)
    $bytes=$stream.ToArray(); $stream.Dispose(); return ,$bytes
}
foreach($variant in @('A','B','C')) {
    [IO.File]::WriteAllBytes((Join-Path $outputDir "$variant.png"),(PngBytes (Icon $variant 256)))
    $sizes=@(16,24,32,48,64,128,256)
    $frames=@($sizes | ForEach-Object { PngBytes (Icon $variant $_) })
    $writer=[IO.BinaryWriter]::new([IO.File]::Create((Join-Path $outputDir "$variant.ico")))
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
        $offset=6+16*$sizes.Count
        for($i=0;$i -lt $sizes.Count;$i++) {
            $dimension=if($sizes[$i] -eq 256){0}else{$sizes[$i]}
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([int]$frames[$i].Length); $writer.Write([int]$offset)
            $offset+=$frames[$i].Length
        }
        foreach($frame in $frames){$writer.Write([byte[]]$frame)}
    } finally {$writer.Dispose()}
}
$board=[Windows.Media.DrawingVisual]::new(); $dc=$board.RenderOpen()
$dc.DrawRectangle((Brush '#F1F0F7'),$null,[Windows.Rect]::new(0,0,1200,760))
Label $dc 'Pi Harbor | Icon directions' 28 36 24 '#29243C'
Label $dc 'Purple palette / no black frame / lower-right wordmark' 16 36 66 '#686078'
$titles=@('A  Clean wordmark','B  Signature band','C  Corner badge')
for($i=0;$i -lt 3;$i++) {
    $x=24+$i*392; $variant=@('A','B','C')[$i]
    $dc.DrawRoundedRectangle((Brush '#FFFFFF'),$null,[Windows.Rect]::new($x,110,368,620),16,16)
    Label $dc $titles[$i] 22 ($x+22) 132 '#29243C'
    $dc.DrawImage((Icon $variant 256),[Windows.Rect]::new($x+76,192,216,216))
    Label $dc 'Light desktop' 14 ($x+22) 438 '#686078'
    $sizes=@(64,48,32,16); $positions=@(28,128,213,292)
    for($j=0;$j -lt 4;$j++) {
        $dc.DrawImage((Icon $variant $sizes[$j]),[Windows.Rect]::new($x+$positions[$j],477+64-$sizes[$j],$sizes[$j],$sizes[$j]))
    }
    $dc.DrawRoundedRectangle((Brush '#232222'),$null,[Windows.Rect]::new($x+12,563,344,149),10,10)
    Label $dc 'Dark desktop' 14 ($x+22) 576 '#C8C3D6'
    for($j=0;$j -lt 4;$j++) {
        $dc.DrawImage((Icon $variant $sizes[$j]),[Windows.Rect]::new($x+$positions[$j],613+64-$sizes[$j],$sizes[$j],$sizes[$j]))
    }
}
$dc.Close()
$bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new(1200,760,96,96,[Windows.Media.PixelFormats]::Pbgra32); $bitmap.Render($board)
[IO.File]::WriteAllBytes((Join-Path $outputDir 'comparison.png'),(PngBytes $bitmap))
Write-Output "Icon proposals: $outputDir"
