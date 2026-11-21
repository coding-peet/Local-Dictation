[CmdletBinding()]
param([string]$OutputDirectory=(Join-Path (Split-Path $PSScriptRoot -Parent) 'src\LocalDictation.App\Assets'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

# Keep the same silhouette at every size. Values specify the full pill height.
$waveHeights = @(3, 8, 15, 7, 12, 5, 3)
$wavePeak = 15
$waveHeight = .60
$barStep = .125
$barWidth = .0625
$tileColor = '#172234'
$barColor = '#FFA197'

function RoundPath([float]$x,[float]$y,[float]$w,[float]$h,[float]$r) {
    $path=[System.Drawing.Drawing2D.GraphicsPath]::new();$d=2*$r
    $path.AddArc($x,$y,$d,$d,180,90);$path.AddArc(($x+$w-$d),$y,$d,$d,270,90)
    $path.AddArc(($x+$w-$d),($y+$h-$d),$d,$d,0,90);$path.AddArc($x,($y+$h-$d),$d,$d,90,90)
    $path.CloseFigure();return ,$path
}
function Draw-Wave([int]$size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = 'AntiAlias'
    $graphics.PixelOffsetMode = 'Half'

    $background = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml($tileColor))
    $tile = RoundPath ($size * .03) ($size * .03) ($size * .94) ($size * .94) ($size * .20)
    $graphics.FillPath($background, $tile)
    $tile.Dispose()
    $background.Dispose()

    $brush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml($barColor))
    $thickness = $size * $barWidth
    for ($i = 0; $i -lt $waveHeights.Count; $i++) {
        $x = $size * (.5 + ($i - 3) * $barStep)
        # At 16 px put each one-pixel bar on a single pixel column with a full gap.
        if ($size -eq 16) { $x = [Math]::Floor($x) + .5 }
        $height = $size * $waveHeight * $waveHeights[$i] / $wavePeak
        $pill = RoundPath ($x - $thickness / 2) (($size - $height) / 2) $thickness $height ($thickness / 2)
        $graphics.FillPath($brush, $pill)
        $pill.Dispose()
    }
    $brush.Dispose()
    $graphics.Dispose()
    return ,$bitmap
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
function Format-SvgNumber([double]$value) {
    $value.ToString('0.##', [Globalization.CultureInfo]::InvariantCulture)
}
$svg = @(
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">'
    ('  <rect x="7.68" y="7.68" width="240.64" height="240.64" rx="51.2" fill="{0}"/>' -f $tileColor)
    ('  <g fill="{0}">' -f $barColor)
)
for ($i = 0; $i -lt $waveHeights.Count; $i++) {
    $x = 256 * (.5 + ($i - 3) * $barStep - $barWidth / 2)
    $height = 256 * $waveHeight * $waveHeights[$i] / $wavePeak
    $y = (256 - $height) / 2
    $svg += '    <rect x="{0}" y="{1}" width="{2}" height="{3}" rx="{4}"/>' -f (
        (Format-SvgNumber $x), (Format-SvgNumber $y), (Format-SvgNumber (256 * $barWidth)),
        (Format-SvgNumber $height), (Format-SvgNumber (128 * $barWidth)))
}
$svg += '  </g>', '</svg>'
[System.IO.File]::WriteAllText((Join-Path $OutputDirectory 'waveform.svg'), ($svg -join [Environment]::NewLine) + [Environment]::NewLine)
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
