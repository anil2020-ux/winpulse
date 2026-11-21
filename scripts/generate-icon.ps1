# Generates src/WinPulse/Assets/App.ico and docs/logo.png for WinPulse.
# The icon is a rounded blue tile with a heartbeat/pulse line.
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

Add-Type -AssemblyName System.Drawing

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$assetsDir = Join-Path $RepoRoot 'src\WinPulse\Assets'
$docsDir = Join-Path $RepoRoot 'docs'
New-Item -ItemType Directory -Path $assetsDir, $docsDir -Force | Out-Null

function New-RoundedPath {
    param([float]$X, [float]$Y, [float]$W, [float]$H, [float]$R)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $R * 2
    $path.AddArc($X, $Y, $d, $d, 180, 90)
    $path.AddArc($X + $W - $d, $Y, $d, $d, 270, 90)
    $path.AddArc($X + $W - $d, $Y + $H - $d, $d, $d, 0, 90)
    $path.AddArc($X, $Y + $H - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap {
    param([int]$Size)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    $pad = [float]($Size * 0.03)
    $radius = [float]($Size * 0.24)
    $path = New-RoundedPath -X $pad -Y $pad -W ($Size - 2 * $pad) -H ($Size - 2 * $pad) -R $radius

    $rect = New-Object System.Drawing.Rectangle(0, 0, $Size, $Size)
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 27, 59, 111),
        [System.Drawing.Color]::FromArgb(255, 9, 16, 27),
        45.0)
    $g.FillPath($bg, $path)

    # subtle top highlight
    $gloss = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(70, 120, 170, 255),
        [System.Drawing.Color]::FromArgb(0, 120, 170, 255),
        90.0)
    $g.FillPath($gloss, $path)

    # pulse line
    $penWidth = [float]([math]::Max(1.2, $Size * 0.085))
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 255, 255, 255), $penWidth)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $points = @(
        (New-Object System.Drawing.PointF([float]($Size * 0.16), [float]($Size * 0.55))),
        (New-Object System.Drawing.PointF([float]($Size * 0.33), [float]($Size * 0.55))),
        (New-Object System.Drawing.PointF([float]($Size * 0.43), [float]($Size * 0.31))),
        (New-Object System.Drawing.PointF([float]($Size * 0.56), [float]($Size * 0.73))),
        (New-Object System.Drawing.PointF([float]($Size * 0.65), [float]($Size * 0.44))),
        (New-Object System.Drawing.PointF([float]($Size * 0.70), [float]($Size * 0.55))),
        (New-Object System.Drawing.PointF([float]($Size * 0.84), [float]($Size * 0.55)))
    )
    $g.DrawLines($pen, [System.Drawing.PointF[]]$points)

    # accent dot
    $dot = [float]($Size * 0.10)
    $accentBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 45, 212, 167))
    $g.FillEllipse($accentBrush, [float]($Size * 0.78), [float]($Size * 0.22), $dot, $dot)

    $pen.Dispose()
    $bg.Dispose()
    $gloss.Dispose()
    $accentBrush.Dispose()
    $path.Dispose()
    $g.Dispose()
    return $bmp
}

$pngs = @{}
foreach ($size in $sizes) {
    $bmp = New-IconBitmap -Size $size
    $stream = New-Object System.IO.MemoryStream
    $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs[$size] = $stream.ToArray()
    $bmp.Dispose()
    $stream.Dispose()
}

# Assemble a multi-resolution .ico with PNG-compressed frames.
$icoPath = Join-Path $assetsDir 'App.ico'
$fs = [System.IO.File]::Create($icoPath)
$writer = New-Object System.IO.BinaryWriter($fs)
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$sizes.Count)
$offset = 6 + (16 * $sizes.Count)
foreach ($size in $sizes) {
    $data = $pngs[$size]
    $writer.Write([Byte]$(if ($size -ge 256) { 0 } else { $size }))
    $writer.Write([Byte]$(if ($size -ge 256) { 0 } else { $size }))
    $writer.Write([Byte]0)
    $writer.Write([Byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$data.Length)
    $writer.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($size in $sizes) {
    $writer.Write($pngs[$size])
}
$writer.Flush()
$writer.Dispose()
$fs.Dispose()

# Logo for docs/README
$logoBmp = New-IconBitmap -Size 512
$logoBmp.Save((Join-Path $docsDir 'logo.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$logoBmp.Dispose()

Write-Output "Generated $icoPath ($((Get-Item $icoPath).Length) bytes) and docs/logo.png"
