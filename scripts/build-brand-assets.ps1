param(
    [string]$Source = "assets/branding/agepilot-logo-chroma-source.png",
    [string]$OutputDirectory = "assets/branding"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$sourcePath = (Resolve-Path -LiteralPath $Source).Path
$outputPath = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputDirectory))
[System.IO.Directory]::CreateDirectory($outputPath) | Out-Null

$sourceImage = [System.Drawing.Bitmap]::new($sourcePath)
$master = [System.Drawing.Bitmap]::new($sourceImage.Width, $sourceImage.Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

try {
    for ($y = 0; $y -lt $sourceImage.Height; $y++) {
        for ($x = 0; $x -lt $sourceImage.Width; $x++) {
            $pixel = $sourceImage.GetPixel($x, $y)
            $distance = [Math]::Sqrt(
                [Math]::Pow(255 - $pixel.R, 2) +
                [Math]::Pow($pixel.G, 2) +
                [Math]::Pow(255 - $pixel.B, 2))

            # The generated chroma field contains light compression/noise. A wider
            # transparent band removes those pixels while preserving the emblem,
            # whose nearest brand colours are far outside this range.
            if ($distance -le 60) {
                $alpha = 0
            }
            elseif ($distance -ge 180) {
                $alpha = $pixel.A
            }
            else {
                $ratio = ($distance - 60) / (180 - 60)
                $ratio = $ratio * $ratio * (3 - (2 * $ratio))
                $alpha = [Math]::Round($pixel.A * $ratio)
            }

            $magentaDominance = [Math]::Min($pixel.R, $pixel.B) - $pixel.G
            if ($magentaDominance -gt 35) {
                $despillAlpha = [Math]::Round(255 * [Math]::Max(0, 1 - (($magentaDominance - 35) / 180)))
                $alpha = [Math]::Min($alpha, $despillAlpha)
            }

            if ($alpha -eq 0) {
                $master.SetPixel($x, $y, [System.Drawing.Color]::Transparent)
            }
            else {
                $master.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($alpha, $pixel.R, $pixel.G, $pixel.B))
            }
        }
    }

    # Replace semi-transparent chroma fringe colours with the closest solid
    # emblem colour. This keeps the edge clean on both light and dark surfaces.
    for ($y = 0; $y -lt $master.Height; $y++) {
        for ($x = 0; $x -lt $master.Width; $x++) {
            $edge = $master.GetPixel($x, $y)
            if ($edge.A -le 0 -or $edge.A -ge 255) { continue }
            $replacement = $null
            for ($radius = 1; $radius -le 4 -and $null -eq $replacement; $radius++) {
                $candidates = @(
                    ,@(($x - $radius), $y)
                    ,@(($x + $radius), $y)
                    ,@($x, ($y - $radius))
                    ,@($x, ($y + $radius)))
                foreach ($candidate in $candidates) {
                    if ($candidate[0] -lt 0 -or $candidate[0] -ge $sourceImage.Width -or
                        $candidate[1] -lt 0 -or $candidate[1] -ge $sourceImage.Height) { continue }
                    $nearby = $sourceImage.GetPixel($candidate[0], $candidate[1])
                    if (([Math]::Min($nearby.R, $nearby.B) - $nearby.G) -le 20) {
                        $replacement = $nearby
                        break
                    }
                }
            }
            if ($null -ne $replacement) {
                $master.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(
                    $edge.A, $replacement.R, $replacement.G, $replacement.B))
            }
        }
    }

    $masterPath = Join-Path $outputPath "agepilot-logo-master.png"
    $master.Save($masterPath, [System.Drawing.Imaging.ImageFormat]::Png)

    $sizes = @(16, 24, 32, 48, 64, 128, 256, 512)
    $iconPayloads = [System.Collections.Generic.List[byte[]]]::new()
    foreach ($size in $sizes) {
        $resized = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($resized)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.DrawImage($master, [System.Drawing.Rectangle]::new(0, 0, $size, $size))
            }
            finally {
                $graphics.Dispose()
            }

            $pngPath = Join-Path $outputPath "agepilot-logo-$size.png"
            $resized.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
            if ($size -le 256) {
                $stream = [System.IO.MemoryStream]::new()
                $resized.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
                $iconPayloads.Add($stream.ToArray())
                $stream.Dispose()
            }
        }
        finally {
            $resized.Dispose()
        }
    }

    $iconPath = Join-Path $outputPath "agepilot.ico"
    $file = [System.IO.File]::Create($iconPath)
    $writer = [System.IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$iconPayloads.Count)
        $offset = 6 + (16 * $iconPayloads.Count)
        for ($index = 0; $index -lt $iconPayloads.Count; $index++) {
            $size = $sizes[$index]
            $dimension = if ($size -eq 256) { 0 } else { $size }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$iconPayloads[$index].Length)
            $writer.Write([uint32]$offset)
            $offset += $iconPayloads[$index].Length
        }
        foreach ($payload in $iconPayloads) {
            $writer.Write($payload)
        }
    }
    finally {
        $writer.Dispose()
        $file.Dispose()
    }

    Write-Output "Generated brand assets in $outputPath"
}
finally {
    $master.Dispose()
    $sourceImage.Dispose()
}
