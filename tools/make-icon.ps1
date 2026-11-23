# Gera o ícone do Editor de MP3 (EditorMP3\Assets\app.ico) em vários tamanhos.
# Uso: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1 [-Preview caminho.png]
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\EditorMP3\Assets\app.ico'),
    [string]$Preview
)

Add-Type -AssemblyName System.Drawing

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Get-IconPng([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Fundo: quadrado arredondado com degradê azul → roxo.
    $m = [Math]::Max(0.5, $s * 0.04)
    $bg = New-RoundedRect $m $m ($s - 2 * $m) ($s - 2 * $m) ($s * 0.22)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 0)), (New-Object System.Drawing.PointF($s, $s)),
        [System.Drawing.Color]::FromArgb(255, 0x2B, 0x6C, 0xF0), [System.Drawing.Color]::FromArgb(255, 0x7A, 0x3C, 0xE6))
    $g.FillPath($grad, $bg)

    # Barras da forma de onda; as das pontas ficam apagadas (parte cortada).
    if ($s -ge 48)     { $heights = 0.30, 0.55, 0.80, 0.45, 0.95, 0.60, 0.78, 0.42, 0.28 }
    elseif ($s -ge 24) { $heights = 0.35, 0.70, 0.50, 0.95, 0.60, 0.80, 0.35 }
    else               { $heights = 0.35, 0.75, 1.00, 0.65, 0.35 }
    $n = $heights.Count
    $left = $s * 0.17; $right = $s * 0.83
    $slot = ($right - $left) / $n
    $barW = [Math]::Max(1.0, $slot * 0.58)
    $mid = $s / 2
    $maxHalf = $s * 0.30

    for ($i = 0; $i -lt $n; $i++) {
        $cut = ($i -eq 0 -or $i -eq $n - 1)
        $alpha = if ($cut) { 95 } else { 255 }
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb($alpha, 255, 255, 255), $barW)
        $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
        $x = $left + $slot * ($i + 0.5)
        $half = [Math]::Max($barW / 2 + 0.5, $heights[$i] * $maxHalf - $barW / 2)
        $g.DrawLine($pen, [float]$x, [float]($mid - $half), [float]$x, [float]($mid + $half))
        $pen.Dispose()
    }

    # Marcadores de corte: verde (início) e vermelho (fim).
    if ($s -ge 24) {
        $mw = [Math]::Max(1.5, $s * 0.035)
        $top = $s * 0.16; $bottom = $s * 0.84
        $green = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 0x3D, 0xE6, 0x96), $mw)
        $red = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 0xFF, 0x5F, 0x5F), $mw)
        $green.StartCap = 'Round'; $green.EndCap = 'Round'; $red.StartCap = 'Round'; $red.EndCap = 'Round'
        $xs = $left + $slot; $xe = $right - $slot
        $g.DrawLine($green, [float]$xs, [float]$top, [float]$xs, [float]$bottom)
        $g.DrawLine($red, [float]$xe, [float]$top, [float]$xe, [float]$bottom)
        if ($s -ge 48) {
            # Alças no topo, como no editor.
            $hw = $s * 0.07; $hh = $s * 0.09
            $g.FillRectangle($green.Brush, [float]$xs, [float]($top - $mw / 2), [float]$hw, [float]$hh)
            $g.FillRectangle($red.Brush, [float]($xe - $hw), [float]($top - $mw / 2), [float]$hw, [float]$hh)
        }
        $green.Dispose(); $red.Dispose()
    }

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($s in $sizes) { , (Get-IconPng $s) }

# Monta o arquivo .ico (entradas em PNG, suportado desde o Windows Vista).
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()

$Output = [System.IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force (Split-Path $Output) | Out-Null
[System.IO.File]::WriteAllBytes($Output, $out.ToArray())
Write-Output "Ícone gerado: $Output"

if ($Preview) {
    # Folha de prévia com todos os tamanhos lado a lado, sobre fundo claro e escuro.
    $sheet = New-Object System.Drawing.Bitmap(760, 560)
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $g.FillRectangle([System.Drawing.Brushes]::White, 0, 0, 760, 280)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(32, 32, 36))), 0, 280, 760, 280)
    foreach ($row in 0, 280) {
        $x = 10
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $img = [System.Drawing.Image]::FromStream((New-Object System.IO.MemoryStream(, $images[$i])))
            $g.DrawImageUnscaled($img, $x, $row + 12)
            $x += $sizes[$i] + 10
        }
    }
    $g.Dispose()
    $sheet.Save([System.IO.Path]::GetFullPath($Preview), [System.Drawing.Imaging.ImageFormat]::Png)
    $sheet.Dispose()
}
