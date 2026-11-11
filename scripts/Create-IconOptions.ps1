[CmdletBinding()]
param([string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'src\LocalDictation.App\Assets'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
function RoundPath([float]$x,[float]$y,[float]$w,[float]$h,[float]$r) {
    $p=[System.Drawing.Drawing2D.GraphicsPath]::new();$d=2*$r
    $p.AddArc($x,$y,$d,$d,180,90);$p.AddArc(($x+$w-$d),$y,$d,$d,270,90)
    $p.AddArc(($x+$w-$d),($y+$h-$d),$d,$d,0,90);$p.AddArc($x,($y+$h-$d),$d,$d,90,90);$p.CloseFigure();return ,$p
}
$names=@('microphone','speech-wave','local-signal','dictation-cursor')
$titles=@('A  Microphone','B  Speech wave','C  Local signal','D  Dictation cursor')
$colors=@('#1977D2','#7354D6','#16856C','#B95728')
$sheet=[System.Drawing.Bitmap]::new(1120,430)
$sg=[System.Drawing.Graphics]::FromImage($sheet);$sg.Clear([System.Drawing.ColorTranslator]::FromHtml('#F3F5F8'))
$font=[System.Drawing.Font]::new('Segoe UI',17)
for($i=0;$i -lt 4;$i++) {
    $bitmap=[System.Drawing.Bitmap]::new(256,256)
    $g=[System.Drawing.Graphics]::FromImage($bitmap);$g.SmoothingMode='AntiAlias'
    $bg=[System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml($colors[$i]));$white=[System.Drawing.Brushes]::White
    $pen=[System.Drawing.Pen]::new([System.Drawing.Color]::White,12);$pen.StartCap='Round';$pen.EndCap='Round';$pen.LineJoin='Round'
    $shape=RoundPath 8 8 240 240 52;$g.FillPath($bg,$shape);$shape.Dispose()
    if($i -eq 0) {
        $shape=RoundPath 101 52 54 106 27;$g.FillPath($white,$shape);$shape.Dispose()
        $g.DrawArc($pen,77,95,102,91,0,180);$g.DrawLine($pen,128,188,128,210);$g.DrawLine($pen,104,210,152,210)
        $svg='<rect x="101" y="52" width="54" height="106" rx="27" fill="white"/><path d="M77 140a51 46 0 0 0 102 0M128 186v24m-24 0h48"/>'
    } elseif($i -eq 1) {
        $shape=RoundPath 49 58 158 130 30;$g.DrawPath($pen,$shape);$shape.Dispose()
        $g.DrawLine($pen,89,189,78,211);$g.DrawLine($pen,78,211,119,188)
        foreach($bar in @(@(85,109,145),@(113,92,161),@(141,103,149),@(169,115,139))){$g.DrawLine($pen,[float]$bar[0],[float]$bar[1],[float]$bar[0],[float]$bar[2])}
        $svg='<path d="M80 58h96a30 30 0 0 1 30 30v70a30 30 0 0 1-30 30h-57l-41 23 11-23h-9a30 30 0 0 1-30-30V88a30 30 0 0 1 30-30Zm5 51v36m28-53v69m28-58v46m28-34v24"/>'
    } elseif($i -eq 2) {
        $g.DrawEllipse($pen,51,51,154,154)
        $points=[System.Drawing.PointF[]]@([System.Drawing.PointF]::new(74,133),[System.Drawing.PointF]::new(101,133),[System.Drawing.PointF]::new(116,92),[System.Drawing.PointF]::new(139,166),[System.Drawing.PointF]::new(154,120),[System.Drawing.PointF]::new(181,120))
        $g.DrawLines($pen,$points)
        $svg='<circle cx="128" cy="128" r="77"/><path d="M74 133h27l15-41 23 74 15-46h27"/>'
    } else {
        $g.DrawLine($pen,63,67,63,190);$g.DrawLine($pen,48,67,78,67);$g.DrawLine($pen,48,190,78,190)
        $shape=RoundPath 119 58 46 89 23;$g.FillPath($white,$shape);$shape.Dispose()
        $g.DrawArc($pen,98,100,88,69,0,180);$g.DrawLine($pen,142,170,142,189);$g.DrawLine($pen,110,208,191,208)
        $svg='<rect x="119" y="58" width="46" height="89" rx="23" fill="white"/><path d="M63 67v123M48 67h30M48 190h30M98 135a44 35 0 0 0 88 0m-44 35v19m-32 19h81"/>'
    }
    $g.Dispose();$pen.Dispose();$bg.Dispose()
    $bitmap.Save((Join-Path $OutputDirectory ($names[$i]+'.png')),[System.Drawing.Imaging.ImageFormat]::Png)
    $svgText='<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256"><rect x="8" y="8" width="240" height="240" rx="52" fill="'+$colors[$i]+'"/><g fill="none" stroke="white" stroke-width="12" stroke-linecap="round" stroke-linejoin="round">'+$svg+'</g></svg>'
    [System.IO.File]::WriteAllText((Join-Path $OutputDirectory ($names[$i]+'.svg')),$svgText)
    $sg.DrawImage($bitmap,(32+$i*280),25,216,216)
    $sg.DrawString($titles[$i],$font,[System.Drawing.Brushes]::Black,[float](26+$i*280),[float]263)
    $sg.FillRectangle([System.Drawing.Brushes]::White,(28+$i*280),310,100,70)
    $sg.FillRectangle([System.Drawing.Brushes]::Black,(138+$i*280),310,100,70)
    $sg.DrawImage($bitmap,(46+$i*280),329,32,32);$sg.DrawImage($bitmap,(156+$i*280),329,32,32)
    if($i -eq 0) {
        $images=[System.Collections.Generic.List[byte[]]]::new()
        foreach($size in @(16,24,32,48,64,128,256)) {
            $small=[System.Drawing.Bitmap]::new($bitmap,$size,$size);$stream=[System.IO.MemoryStream]::new()
            $small.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png);$images.Add($stream.ToArray());$stream.Dispose();$small.Dispose()
        }
        $stream=[System.IO.File]::Create((Join-Path $OutputDirectory 'microphone.ico'));$writer=[System.IO.BinaryWriter]::new($stream)
        $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]7);$offset=6+7*16;$j=0
        foreach($size in @(16,24,32,48,64,128,256)) {
            $edge=if($size -eq 256){0}else{$size};$writer.Write([byte]$edge);$writer.Write([byte]$edge);$writer.Write([byte]0);$writer.Write([byte]0)
            $writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$images[$j].Length);$writer.Write([uint32]$offset);$offset+=$images[$j].Length;$j++
        }
        foreach($bytes in $images){$writer.Write($bytes)};$writer.Dispose()
    }
    $bitmap.Dispose()
}
$sheet.Save((Join-Path $OutputDirectory 'icon-options.png'),[System.Drawing.Imaging.ImageFormat]::Png)
$sg.Dispose();$sheet.Dispose();$font.Dispose()
Write-Output (Join-Path $OutputDirectory 'icon-options.png')
