# SnapTranslate 아이콘(app.ico)을 그려서 만든다.
# 사용: powershell -File tools\make-icon.ps1 -Out app.ico [-Preview preview.png]
param(
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$Preview
)

Add-Type -AssemblyName System.Drawing

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $d = $r * 2
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

# 한 변이 $s 픽셀인 아이콘 그림 한 장
function New-IconBitmap([int]$s) {
    $bmp = New-Object Drawing.Bitmap $s, $s, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([Drawing.Color]::Transparent)

    # 바탕: 둥근 사각형에 밝은 파랑 → 짙은 파랑 대각선 그라데이션
    [single]$m = [Math]::Max(0.5, $s * 0.03)
    [single]$side = $s - 2 * $m
    [single]$radius = $side * 0.235
    $body = New-RoundedPath $m $m $side $side $radius
    $rect = New-Object Drawing.RectangleF $m, $m, $side, $side
    $top = [Drawing.Color]::FromArgb(255, 86, 172, 255)
    $bottom = [Drawing.Color]::FromArgb(255, 24, 72, 214)
    $fill = New-Object Drawing.Drawing2D.LinearGradientBrush $rect, $top, $bottom, ([single]60)
    $g.FillPath($fill, $body)

    # 위쪽 절반에 은은한 광택
    $g.SetClip($body)
    $glossRect = New-Object Drawing.RectangleF ($m - $side * 0.25), ($m - $side * 0.62), ($side * 1.5), ($side * 1.12)
    $gloss = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(34, 255, 255, 255))
    $g.FillEllipse($gloss, $glossRect)
    $g.ResetClip()

    # 가장자리 안쪽의 밝은 테두리(입체감)
    if ($s -ge 32) {
        $edge = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(70, 255, 255, 255)), ([single][Math]::Max(1, $s * 0.012))
        $g.DrawPath($edge, $body)
    }

    # 네 모서리의 캡처 영역 표시(꺾쇠). 작은 크기에서는 뭉개지므로 뺀다
    if ($s -ge 24) {
        [single]$inset = $m + $side * 0.17
        [single]$len = $side * 0.15
        [single]$far = $s - $inset
        $pen = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(235, 255, 255, 255)), ([single][Math]::Max(1.5, $s * 0.05))
        $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
        $corners = @(
            @($inset, ($inset + $len), $inset, $inset, ($inset + $len), $inset),
            @(($far - $len), $inset, $far, $inset, $far, ($inset + $len)),
            @($far, ($far - $len), $far, $far, ($far - $len), $far),
            @(($inset + $len), $far, $inset, $far, $inset, ($far - $len))
        )
        foreach ($c in $corners) {
            $pts = [Drawing.PointF[]]@(
                (New-Object Drawing.PointF ([single]$c[0]), ([single]$c[1])),
                (New-Object Drawing.PointF ([single]$c[2]), ([single]$c[3])),
                (New-Object Drawing.PointF ([single]$c[4]), ([single]$c[5])))
            $g.DrawLines($pen, $pts)
        }
    }

    # 가운데 글자 "가"(U+AC00): 번역 결과가 한글이라는 뜻. 윤곽선으로 만들어 정확히 가운데에 놓는다
    $glyph = New-Object Drawing.Drawing2D.GraphicsPath
    $family = New-Object Drawing.FontFamily 'Malgun Gothic'
    [single]$em = if ($s -ge 24) { $side * 0.5 } else { $side * 0.72 }
    $format = [Drawing.StringFormat]::GenericTypographic
    $glyph.AddString([string][char]0xAC00, $family, [int][Drawing.FontStyle]::Bold, $em, (New-Object Drawing.PointF 0, 0), $format)
    $bounds = $glyph.GetBounds()
    $move = New-Object Drawing.Drawing2D.Matrix
    $move.Translate(($s - $bounds.Width) / 2 - $bounds.X, ($s - $bounds.Height) / 2 - $bounds.Y)
    $glyph.Transform($move)

    if ($s -ge 32) {
        # 글자 아래 옅은 그림자
        $shadowPath = $glyph.Clone()
        $down = New-Object Drawing.Drawing2D.Matrix
        $down.Translate(0, [single]($s * 0.02))
        $shadowPath.Transform($down)
        $shadow = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(70, 10, 30, 110))
        $g.FillPath($shadow, $shadowPath)
    }
    $g.FillPath([Drawing.Brushes]::White, $glyph)

    $g.Dispose()
    return $bmp
}

# 32비트 그림을 ICO 안에 넣는 DIB 형식(BITMAPINFOHEADER + 아래에서 위 순서 픽셀 + 빈 AND 마스크)으로 바꾼다
function ConvertTo-IconDib([Drawing.Bitmap]$bmp) {
    $s = $bmp.Width
    $data = $bmp.LockBits((New-Object Drawing.Rectangle 0, 0, $s, $s), 'ReadOnly', 'Format32bppArgb')
    $pixels = New-Object byte[] ($s * $s * 4)
    for ($y = 0; $y -lt $s; $y++) {
        # 화면 위쪽 줄이 파일에서는 맨 뒤에 온다
        $row = [IntPtr]::Add($data.Scan0, $y * $data.Stride)
        [Runtime.InteropServices.Marshal]::Copy($row, $pixels, ($s - 1 - $y) * $s * 4, $s * 4)
    }
    $bmp.UnlockBits($data)

    $maskStride = [int]([Math]::Ceiling($s / 32.0) * 4)
    $ms = New-Object IO.MemoryStream
    $w = New-Object IO.BinaryWriter $ms
    $w.Write([int]40); $w.Write([int]$s); $w.Write([int]($s * 2))
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]0)
    $w.Write([int]($pixels.Length + $maskStride * $s))
    $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    $w.Write($pixels)
    $w.Write((New-Object byte[] ($maskStride * $s)))
    $w.Flush()
    return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    if ($s -ge 256) {
        # 256 크기는 PNG 로 넣는다(Windows 표준)
        $ms = New-Object IO.MemoryStream
        $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
        $bytes = $ms.ToArray()
        if ($Preview) { $bmp.Save($Preview, [Drawing.Imaging.ImageFormat]::Png) }
    }
    else {
        $bytes = ConvertTo-IconDib $bmp
    }
    $images += , @($s, $bytes)
    $bmp.Dispose()
}

# ICO 파일: 머리말(6바이트) + 항목 목록(크기마다 16바이트) + 그림 데이터
$file = New-Object IO.MemoryStream
$writer = New-Object IO.BinaryWriter $file
$writer.Write([int16]0); $writer.Write([int16]1); $writer.Write([int16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($image in $images) {
    $s = $image[0]; $bytes = [byte[]]$image[1]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $writer.Write([byte]$dim); $writer.Write([byte]$dim); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([int16]1); $writer.Write([int16]32)
    $writer.Write([int]$bytes.Length); $writer.Write([int]$offset)
    $offset += $bytes.Length
}
foreach ($image in $images) { $writer.Write([byte[]]$image[1]) }
$writer.Flush()
[IO.File]::WriteAllBytes($Out, $file.ToArray())
"wrote $Out ($($file.Length) bytes, sizes: $($sizes -join ', '))"
