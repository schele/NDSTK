<#
    Draws the dashboard's icon and writes it as app.ico beside this script.

    The shape is the sidebar's brand mark - index.html's <span class="brand-mark"> and its inline
    SVG - so the taskbar, the title bar and the exe in Explorer all show the thing the window shows.
    Drawn with GDI+ rather than exported from a design tool, so the icon can be regenerated from
    source when the mark changes and nothing binary of unknown origin lands in the repository.

    Sizes: 16, 20, 24, 32, 40, 48, 64, 128, 256. Every entry is stored as PNG, which Windows has
    accepted inside .ico since Vista and which keeps the file small. Below 32px the shield is a
    solid silhouette: a 1.7-unit stroke scaled to 16px is under a pixel wide and reads as noise.

    Run: powershell -File make-icon.ps1
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $here 'app.ico'

# app.css tokens, verbatim: --blue-600, --teal-600, --surface.
$blue = [System.Drawing.Color]::FromArgb(0x1D, 0x4E, 0xD8)
$teal = [System.Drawing.Color]::FromArgb(0x0F, 0x8B, 0x7A)
$white = [System.Drawing.Color]::White

function Draw-Mark([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # The rounded square. 22% radius is what Windows 11's own tiles use; the mark's --r-md on a
    # 32px box is close to it.
    $radius = [Math]::Max(2.0, $size * 0.22)
    $d = $radius * 2
    $rect = New-Object System.Drawing.Drawing2D.GraphicsPath
    $rect.AddArc(0, 0, $d, $d, 180, 90)
    $rect.AddArc($size - $d, 0, $d, $d, 270, 90)
    $rect.AddArc($size - $d, $size - $d, $d, $d, 0, 90)
    $rect.AddArc(0, $size - $d, $d, $d, 90, 90)
    $rect.CloseFigure()

    # linear-gradient(140deg, blue, teal): CSS 140deg points down-right, GDI+ angles run the other
    # way from the x axis, so 140deg CSS is 50deg here.
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Rectangle 0, 0, $size, $size), $blue, $teal, [single]50, $true)
    $g.FillPath($brush, $rect)

    # The shield, from the SVG path (viewBox 0 0 24 24), scaled so it sits at 62% of the square -
    # the mark shows an 18px glyph in a 32px box, and the icon can afford a little more. Below 32px
    # it takes 78%: at ten pixels tall the shield's straight top and pointed foot melt into a blob,
    # and it is the outline of the shape, not its proportion to the square, that has to survive.
    $glyph = if ($size -lt 32) { $size * 0.78 } else { $size * 0.62 }
    $s = $glyph / 24.0
    $o = ($size - $glyph) / 2.0
    function P([double]$x, [double]$y) { New-Object System.Drawing.PointF ([single]($o + $x * $s)), ([single]($o + $y * $s)) }

    $shield = New-Object System.Drawing.Drawing2D.GraphicsPath
    $shield.AddLine((P 12 3), (P 4.5 6))
    $shield.AddLine((P 4.5 6), (P 4.5 11.4))
    $shield.AddBezier((P 4.5 11.4), (P 4.5 15.7), (P 7.5 19.7), (P 12 21))
    $shield.AddBezier((P 12 21), (P 16.5 19.7), (P 19.5 15.7), (P 19.5 11.4))
    $shield.AddLine((P 19.5 11.4), (P 19.5 6))
    $shield.AddLine((P 19.5 6), (P 12 3))
    $shield.CloseFigure()

    if ($size -lt 32) {
        # Silhouette: a stroke this thin would not survive.
        $g.FillPath((New-Object System.Drawing.SolidBrush $white), $shield)
    } else {
        $pen = New-Object System.Drawing.Pen $white, ([single](1.7 * $s))
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $g.DrawPath($pen, $shield)

        # The three dots - the crumbs that make the shield a cookie's.
        $dot = New-Object System.Drawing.SolidBrush $white
        foreach ($c in @(@(10, 10.2), @(13.9, 12.4), @(10.6, 14.4))) {
            $r = 1.0 * $s
            $p = P $c[0] $c[1]
            $g.FillEllipse($dot, [single]($p.X - $r), [single]($p.Y - $r), [single](2 * $r), [single](2 * $r))
        }
    }

    $g.Dispose()
    return $bitmap
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($size in $sizes) {
    $bmp = Draw-Mark $size
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    [pscustomobject]@{ Size = $size; Bytes = $ms.ToArray() }
}

# ICONDIR + ICONDIRENTRY per image + the PNG payloads, in that order. Width/height are bytes, so
# 256 is written as 0, which is what the format specifies.
$stream = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $stream
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
foreach ($png in $pngs) {
    $dim = if ($png.Size -ge 256) { 0 } else { $png.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim)
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$png.Bytes.Length); $w.Write([uint32]$offset)
    $offset += $png.Bytes.Length
}
foreach ($png in $pngs) { $w.Write($png.Bytes) }
$w.Flush(); $stream.Close()

"wrote $out - $($pngs.Count) sizes ($($sizes -join ', ')), $((Get-Item $out).Length) bytes"
