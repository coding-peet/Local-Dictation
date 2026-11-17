[CmdletBinding()]
param([string]$OutputDirectory=(Join-Path (Split-Path $PSScriptRoot -Parent) 'src\LocalDictation.App\Assets'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
function RoundPath([float]$x,[float]$y,[float]$w,[float]$h,[float]$r) {
    $path=[System.Drawing.Drawing2D.GraphicsPath]::new();$d=2*$r
    $path.AddArc($x,$y,$d,$d,180,90);$path.AddArc(($x+$w-$d),$y,$d,$d,270,90)
    $path.AddArc(($x+$w-$d),($y+$h-$d),$d,$d,0,90);$path.AddArc($x,($y+$h-$d),$d,$d,90,90)
    $path.CloseFigure();return ,$path
}
function Draw-Wave([int]$size) {
    $bitmap=[System.Drawing.Bitmap]::new($size,$size)
    $g=[System.Drawing.Graphics]::FromImage($bitmap);$g.SmoothingMode='AntiAlias';$g.PixelOffsetMode='HighQuality'
    $bg=[System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#202631'))
    $tile=RoundPath ($size*.03) ($size*.03) ($size*.94) ($size*.94) ($size*.20);$g.FillPath($bg,$tile);$tile.Dispose();$bg.Dispose()
    $heights=if($size -le 24){@(7,17,28,19,30,15,6)}elseif($size -le 48){@(6,16,28,17,25,32,18,28,16,10,5)}else{@(7,12,20,29,16,24,31,18,25,14,21,32,23,15,25,18,11,6)}
    $width=$size*.80;$step=$width/$heights.Count;$thickness=[Math]::Max(1.05,$step*.58)
    $pen=[System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#FF858D'),[float]$thickness);$pen.StartCap='Round';$pen.EndCap='Round'
    for($i=0;$i -lt $heights.Count;$i++) {
        $height=$size*.40*$heights[$i]/32
        $x=$size*.10+$step*($i+.5)
        $g.DrawLine($pen,[float]$x,[float]($size/2-$height/2),[float]$x,[float]($size/2+$height/2))
    }
    $pen.Dispose();$g.Dispose();return ,$bitmap
}
$sizes=@(16,24,32,48,64,128,256)
$images=[System.Collections.Generic.List[byte[]]]::new()
foreach($size in $sizes) {
    $bitmap=Draw-Wave $size;$stream=[System.IO.MemoryStream]::new()
    $bitmap.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png);$images.Add($stream.ToArray())
    $bitmap.Save((Join-Path $OutputDirectory ('waveform-'+$size+'.png')),[System.Drawing.Imaging.ImageFormat]::Png)
    if($size -eq 256){$bitmap.Save((Join-Path $OutputDirectory 'waveform.png'),[System.Drawing.Imaging.ImageFormat]::Png)}
    $stream.Dispose();$bitmap.Dispose()
}
$stream=[System.IO.File]::Create((Join-Path $OutputDirectory 'waveform.ico'));$writer=[System.IO.BinaryWriter]::new($stream)
$writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$sizes.Count);$offset=6+$sizes.Count*16
for($i=0;$i -lt $sizes.Count;$i++) {
    $edge=if($sizes[$i] -eq 256){0}else{$sizes[$i]};$writer.Write([byte]$edge);$writer.Write([byte]$edge);$writer.Write([byte]0);$writer.Write([byte]0)
    $writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$images[$i].Length);$writer.Write([uint32]$offset);$offset+=$images[$i].Length
}
foreach($bytes in $images){$writer.Write($bytes)};$writer.Dispose()
$svg='<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256"><rect x="7.68" y="7.68" width="240.64" height="240.64" rx="51.2" fill="#202631"/><g stroke="#ff858d" stroke-width="6.6" stroke-linecap="round">'
$heights=@(7,12,20,29,16,24,31,18,25,14,21,32,23,15,25,18,11,6)
for($i=0;$i -lt $heights.Count;$i++) {
    $x=(25.6+204.8/18*($i+.5)).ToString('0.##',[Globalization.CultureInfo]::InvariantCulture)
    $y1=(128-102.4*$heights[$i]/64).ToString('0.##',[Globalization.CultureInfo]::InvariantCulture)
    $y2=(128+102.4*$heights[$i]/64).ToString('0.##',[Globalization.CultureInfo]::InvariantCulture)
    $svg+='<path d="M'+$x+' '+$y1+'V'+$y2+'"/>'
}
[System.IO.File]::WriteAllText((Join-Path $OutputDirectory 'waveform.svg'),$svg+'</g></svg>')
$sheet=[System.Drawing.Bitmap]::new(760,320);$g=[System.Drawing.Graphics]::FromImage($sheet);$g.Clear([System.Drawing.ColorTranslator]::FromHtml('#F3F5F8'))
$font=[System.Drawing.Font]::new('Segoe UI',13);$titleFont=[System.Drawing.Font]::new('Segoe UI Semibold',20)
$g.DrawString('Local Dictation',$titleFont,[System.Drawing.Brushes]::Black,[float]270,[float]28)
$g.DrawString('Waveform icon',$font,[System.Drawing.Brushes]::DimGray,[float]270,[float]68)
$large=Draw-Wave 224;$g.DrawImage($large,20,26);$large.Dispose()
$g.FillRectangle([System.Drawing.Brushes]::White,266,112,470,80)
$g.FillRectangle([System.Drawing.Brushes]::Black,266,202,470,80)
$x=286
foreach($size in @(16,24,32,48,64)) {
    $icon=Draw-Wave $size;$g.DrawImage($icon,$x,(152-$size/2),$size,$size);$g.DrawImage($icon,$x,(242-$size/2),$size,$size)
    $g.DrawString([string]$size,$font,[System.Drawing.Brushes]::DimGray,[float]$x,[float]286)
    $x+=88;$icon.Dispose()
}
$sheet.Save((Join-Path $OutputDirectory 'waveform-preview.png'),[System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose();$sheet.Dispose();$font.Dispose();$titleFont.Dispose()
Write-Output (Join-Path $OutputDirectory 'waveform-preview.png')
