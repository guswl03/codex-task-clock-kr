param([Parameter(Mandatory = $true)][string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedSquare {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc(2.0, 2.0, 26.0, 26.0, 180.0, 90.0)
    $path.AddArc(36.0, 2.0, 26.0, 26.0, 270.0, 90.0)
    $path.AddArc(36.0, 36.0, 26.0, 26.0, 0.0, 90.0)
    $path.AddArc(2.0, 36.0, 26.0, 26.0, 90.0, 90.0)
    $path.CloseFigure()
    return $path
}

function New-ClockBitmap([int]$Size) {
    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $path = New-RoundedSquare
    $fill = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(34, 35, 36))
    $edge = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(80, 83, 86), 1.0)
    $dial = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(249, 249, 247), 4.5)
    $progress = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(68, 124, 216), 5.0)
    $hand = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(249, 249, 247), 3.5)
    $center = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(68, 124, 216))
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $scale = [single]($Size / 64.0)
        $graphics.ScaleTransform($scale, $scale)
        $graphics.FillPath($fill, $path)
        $graphics.DrawPath($edge, $path)
        foreach ($pen in @($dial, $progress, $hand)) {
            $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
            $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        }
        $graphics.DrawEllipse($dial, 15, 15, 34, 34)
        $graphics.DrawArc($progress, 15, 15, 34, 34, -92, 115)
        $graphics.DrawLine($hand, 32, 32, 32, 22)
        $graphics.DrawLine($hand, 32, 32, 39, 36)
        $graphics.FillEllipse($center, 29, 29, 6, 6)
    }
    finally {
        $center.Dispose()
        $hand.Dispose()
        $progress.Dispose()
        $dial.Dispose()
        $edge.Dispose()
        $fill.Dispose()
        $path.Dispose()
        $graphics.Dispose()
    }
    return $bitmap
}

function ConvertTo-IconFrame([System.Drawing.Bitmap]$Bitmap) {
    $size = $Bitmap.Width
    $maskRowBytes = [int]([Math]::Ceiling($size / 32.0) * 4)
    $stream = [System.IO.MemoryStream]::new()
    $writer = [System.IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint32]40)
        $writer.Write([int]$size)
        $writer.Write([int]($size * 2))
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]0)
        $writer.Write([uint32]($size * $size * 4))
        $writer.Write([int]0)
        $writer.Write([int]0)
        $writer.Write([uint32]0)
        $writer.Write([uint32]0)
        for ($y = $size - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $size; $x++) {
                $pixel = $Bitmap.GetPixel($x, $y)
                $writer.Write([byte]$pixel.B)
                $writer.Write([byte]$pixel.G)
                $writer.Write([byte]$pixel.R)
                $writer.Write([byte]$pixel.A)
            }
        }
        for ($y = $size - 1; $y -ge 0; $y--) {
            for ($byteIndex = 0; $byteIndex -lt $maskRowBytes; $byteIndex++) {
                [byte]$mask = 0
                for ($bit = 0; $bit -lt 8; $bit++) {
                    $x = $byteIndex * 8 + $bit
                    if ($x -lt $size -and $Bitmap.GetPixel($x, $y).A -lt 128) {
                        $mask = [byte]($mask -bor (1 -shl (7 - $bit)))
                    }
                }
                $writer.Write($mask)
            }
        }
        return ,$stream.ToArray()
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

if (-not (Test-Path -LiteralPath $OutputDirectory -PathType Container)) {
    throw "출력 폴더를 찾을 수 없습니다: $OutputDirectory"
}

$sizes = @(16, 32, 48, 256)
$frames = [System.Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) {
    $bitmap = New-ClockBitmap $size
    try {
        $frames.Add((ConvertTo-IconFrame $bitmap))
        if ($size -eq 256) {
            $bitmap.Save((Join-Path $OutputDirectory '아이콘-미리보기.png'), [System.Drawing.Imaging.ImageFormat]::Png)
        }
    }
    finally { $bitmap.Dispose() }
}

$stream = [System.IO.File]::Create((Join-Path $OutputDirectory 'app.ico'))
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$frames.Count)
    $offset = 6 + $frames.Count * 16
    for ($index = 0; $index -lt $frames.Count; $index++) {
        $size = $sizes[$index]
        $dimension = if ($size -eq 256) { 0 } else { $size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$index].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}
