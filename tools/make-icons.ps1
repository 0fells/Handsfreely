param([string]$Out = (Join-Path $PSScriptRoot "..\windows\Handsfreely.App\Assets"))

Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $Out | Out-Null

function New-Icon([int]$w, [int]$h, [double]$scale, [bool]$plate) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = "AntiAlias"
    $g.TextRenderingHint = "AntiAliasGridFit"
    $g.Clear([System.Drawing.Color]::Transparent)
    $side = [Math]::Min($w, $h) * $scale
    $x = ($w - $side) / 2
    $y = ($h - $side) / 2
    if ($plate) {
        $r = $side * 0.22
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddArc($x, $y, 2 * $r, 2 * $r, 180, 90)
        $path.AddArc($x + $side - 2 * $r, $y, 2 * $r, 2 * $r, 270, 90)
        $path.AddArc($x + $side - 2 * $r, $y + $side - 2 * $r, 2 * $r, 2 * $r, 0, 90)
        $path.AddArc($x, $y + $side - 2 * $r, 2 * $r, 2 * $r, 90, 90)
        $path.CloseFigure()
        $rect = New-Object System.Drawing.RectangleF $x, $y, $side, $side
        $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 47, 128, 237)), ([System.Drawing.Color]::FromArgb(255, 21, 87, 196)), 45.0
        $g.FillPath($brush, $path)
        $glyphColor = [System.Drawing.Color]::White
    } else {
        $glyphColor = [System.Drawing.Color]::FromArgb(255, 33, 107, 222)
    }
    $font = New-Object System.Drawing.Font "Segoe MDL2 Assets", ([float]($side * 0.5)), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = "Center"
    $fmt.LineAlignment = "Center"
    $glyph = [string][char]0xE717
    $g.DrawString($glyph, $font, (New-Object System.Drawing.SolidBrush $glyphColor), (New-Object System.Drawing.RectangleF $x, $y, $side, $side), $fmt)
    $g.Dispose()
    return $bmp
}

function Save([System.Drawing.Bitmap]$bmp, [string]$name) {
    $bmp.Save((Join-Path $Out $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

Save (New-Icon 44 44 1.0 $true) "Square44x44Logo.png"
Save (New-Icon 88 88 1.0 $true) "Square44x44Logo.scale-200.png"
Save (New-Icon 24 24 1.0 $true) "Square44x44Logo.targetsize-24_altform-unplated.png"
Save (New-Icon 48 48 1.0 $true) "Square44x44Logo.targetsize-48_altform-unplated.png"
Save (New-Icon 150 150 0.66 $true) "Square150x150Logo.png"
Save (New-Icon 300 300 0.66 $true) "Square150x150Logo.scale-200.png"
Save (New-Icon 310 150 0.66 $true) "Wide310x150Logo.png"
Save (New-Icon 620 300 0.66 $true) "Wide310x150Logo.scale-200.png"
Save (New-Icon 50 50 1.0 $true) "StoreLogo.png"
Save (New-Icon 620 300 0.5 $true) "SplashScreen.png"
Save (New-Icon 1240 600 0.5 $true) "SplashScreen.scale-200.png"

$sizes = 16, 24, 32, 48, 64, 256
$frames = foreach ($s in $sizes) {
    $bmp = New-Icon $s $s 1.0 $true
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}
$ico = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ico
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$frames[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $frames[$i].Length
}
foreach ($f in $frames) { $bw.Write($f) }
$bw.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $Out "Handsfreely.ico"), $ico.ToArray())
Write-Output "icons written to $Out"
