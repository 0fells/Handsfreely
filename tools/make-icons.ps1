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
    $box = $side * $(if ($side -lt 40) { 0.74 } else { 0.62 })
    $g.TranslateTransform([float]($x + ($side - $box) / 2), [float]($y + ($side - $box) / 2))
    $g.ScaleTransform([float]($box / 24), [float]($box / 24))
    Draw-Glyph $g $glyphColor
    $g.Dispose()
    return $bmp
}

$handset = "M6.62,10.79c1.44,2.83 3.76,5.14 6.59,6.59l2.2,-2.2c0.27,-0.27 0.67,-0.36 1.02,-0.24 1.12,0.37 2.33,0.57 3.57,0.57 0.55,0 1,0.45 1,1V20c0,0.55 -0.45,1 -1,1 -9.39,0 -17,-7.61 -17,-17 0,-0.55 0.45,-1 1,-1h3.5c0.55,0 1,0.45 1,1 0,1.25 0.2,2.45 0.57,3.57 0.11,0.35 0.03,0.74 -0.25,1.02l-2.2,2.2z"

function Draw-Glyph([System.Drawing.Graphics]$g, [System.Drawing.Color]$color) {
    $pen = New-Object System.Drawing.Pen $color, 2
    $pen.StartCap = "Round"
    $pen.EndCap = "Round"
    $pen.LineJoin = "Round"
    $frame = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = 2.5
    $frame.AddArc(2.5, 3.5, 2 * $r, 2 * $r, 180, 90)
    $frame.AddArc(21.5 - 2 * $r, 3.5, 2 * $r, 2 * $r, 270, 90)
    $frame.AddArc(21.5 - 2 * $r, 16.5 - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $frame.AddArc(2.5, 16.5 - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $frame.CloseFigure()
    $g.DrawPath($pen, $frame)
    $g.DrawLine($pen, 12, 16.5, 12, 20)
    $g.DrawLine($pen, 8.5, 20.5, 15.5, 20.5)
    $phone = ConvertFrom-SvgPath $handset
    $m = New-Object System.Drawing.Drawing2D.Matrix
    $m.Translate(12, 10)
    $m.Scale(0.44, 0.44)
    $m.Translate(-12, -12)
    $phone.Transform($m)
    $g.FillPath((New-Object System.Drawing.SolidBrush $color), $phone)
}

function ConvertFrom-SvgPath([string]$d) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tokens = @([regex]::Matches($d, '[A-Za-z]|-?(?:\d+\.?\d*|\.\d+)') | ForEach-Object { $_.Value })
    $i = 0; $cmd = ""; $cx = 0.0; $cy = 0.0; $sx = 0.0; $sy = 0.0
    function Num { $script:i++; return [double]::Parse($tokens[$script:i - 1], [Globalization.CultureInfo]::InvariantCulture) }
    $script:i = 0
    while ($script:i -lt $tokens.Count) {
        if ($tokens[$script:i] -match '^[A-Za-z]$') { $cmd = $tokens[$script:i]; $script:i++ }
        $rel = $cmd -ceq $cmd.ToLower()
        $ox = if ($rel) { $cx } else { 0.0 }
        $oy = if ($rel) { $cy } else { 0.0 }
        switch ($cmd.ToUpper()) {
            "M" { $cx = $ox + (Num); $cy = $oy + (Num); $sx = $cx; $sy = $cy; $p.StartFigure(); $cmd = if ($rel) { "l" } else { "L" } }
            "L" { $nx = $ox + (Num); $ny = $oy + (Num); $p.AddLine([float]$cx, [float]$cy, [float]$nx, [float]$ny); $cx = $nx; $cy = $ny }
            "H" { $nx = $ox + (Num); $p.AddLine([float]$cx, [float]$cy, [float]$nx, [float]$cy); $cx = $nx }
            "V" { $ny = $oy + (Num); $p.AddLine([float]$cx, [float]$cy, [float]$cx, [float]$ny); $cy = $ny }
            "C" {
                $x1 = $ox + (Num); $y1 = $oy + (Num); $x2 = $ox + (Num); $y2 = $oy + (Num); $nx = $ox + (Num); $ny = $oy + (Num)
                $p.AddBezier([float]$cx, [float]$cy, [float]$x1, [float]$y1, [float]$x2, [float]$y2, [float]$nx, [float]$ny)
                $cx = $nx; $cy = $ny
            }
            "Z" { $p.CloseFigure(); $cx = $sx; $cy = $sy }
        }
    }
    return $p
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
