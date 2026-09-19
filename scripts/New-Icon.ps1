$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedPath([single]$x, [single]$y, [single]$width, [single]$height, [single]$radius) {
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $radius * 2
    $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
    $path.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
    $path.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskAssetDirectory = Join-Path $taskRoot 'src\Bingyu.App\Assets'
[IO.Directory]::CreateDirectory($taskAssetDirectory) | Out-Null
$taskImages = @()

# A luminous Q-shaped orbit and a four-point core carry the AI theme. At 16 px,
# the opaque orbit stays sharp; a subtle halo is reserved for larger sizes.
foreach ($taskSize in @(16, 20, 24, 32, 40, 48, 64, 128, 256)) {
    $taskRenderSize = $taskSize * 4
    $taskLarge = [Drawing.Bitmap]::new($taskRenderSize, $taskRenderSize)
    $taskGraphics = [Drawing.Graphics]::FromImage($taskLarge)
    $taskGraphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $taskGraphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
    $taskGraphics.Clear([Drawing.Color]::Transparent)
    $taskGraphics.ScaleTransform([single]($taskRenderSize / 256.0), [single]($taskRenderSize / 256.0))

    $taskTile = New-RoundedPath 4 4 248 248 58
    $taskTileGradient = [Drawing.Drawing2D.LinearGradientBrush]::new(
        [Drawing.RectangleF]::new(4, 4, 248, 248),
        [Drawing.ColorTranslator]::FromHtml('#101933'),
        [Drawing.ColorTranslator]::FromHtml('#35205E'),
        [single]135)
    $taskGraphics.FillPath($taskTileGradient, $taskTile)
    $taskTileLine = [Drawing.Pen]::new([Drawing.Color]::FromArgb(68, 185, 167, 255), [single]2)
    $taskGraphics.DrawPath($taskTileLine, $taskTile)

    if ($taskSize -ge 48) {
        $taskHalo = [Drawing.Pen]::new([Drawing.Color]::FromArgb(44, 109, 222, 255), [single]35)
        $taskGraphics.DrawEllipse($taskHalo, [single]57, [single]57, [single]140, [single]140)
        $taskHalo.Dispose()
    }
    $taskOrbitGradient = [Drawing.Drawing2D.LinearGradientBrush]::new(
        [Drawing.RectangleF]::new(55, 55, 150, 150),
        [Drawing.ColorTranslator]::FromHtml('#5DE8F2'),
        [Drawing.ColorTranslator]::FromHtml('#A66BFF'),
        [single]55)
    $taskOrbit = [Drawing.Pen]::new($taskOrbitGradient, [single]23)
    $taskOrbit.StartCap = [Drawing.Drawing2D.LineCap]::Round
    $taskOrbit.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $taskGraphics.DrawEllipse($taskOrbit, [single]58, [single]58, [single]138, [single]138)
    $taskTail = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#A66BFF'), [single]23)
    $taskTail.StartCap = [Drawing.Drawing2D.LineCap]::Round
    $taskTail.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $taskGraphics.DrawLine($taskTail, [single]170, [single]170, [single]203, [single]203)

    $taskStarPoints = [Drawing.PointF[]]@(
        [Drawing.PointF]::new(127, 88), [Drawing.PointF]::new(139, 116),
        [Drawing.PointF]::new(167, 128), [Drawing.PointF]::new(139, 140),
        [Drawing.PointF]::new(127, 168), [Drawing.PointF]::new(115, 140),
        [Drawing.PointF]::new(87, 128), [Drawing.PointF]::new(115, 116)
    )
    $taskStar = [Drawing.Drawing2D.GraphicsPath]::new()
    $taskStar.AddPolygon($taskStarPoints)
    $taskStarFill = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#F4FBFF'))
    $taskGraphics.FillPath($taskStarFill, $taskStar)

    $taskSmall = [Drawing.Bitmap]::new($taskSize, $taskSize)
    $taskDownsample = [Drawing.Graphics]::FromImage($taskSmall)
    $taskDownsample.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $taskDownsample.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $taskDownsample.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
    $taskDownsample.DrawImage($taskLarge, 0, 0, $taskSize, $taskSize)

    if ($taskSize -eq 256) {
        $taskPreview = Join-Path $taskRoot 'artifacts\app-icon-preview.png'
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($taskPreview)) | Out-Null
        $taskSmall.Save($taskPreview, [Drawing.Imaging.ImageFormat]::Png)
    }
    $taskStream = [IO.MemoryStream]::new()
    $taskSmall.Save($taskStream, [Drawing.Imaging.ImageFormat]::Png)
    $taskImages += ,@($taskSize, $taskStream.ToArray())
    $taskStream.Dispose(); $taskDownsample.Dispose(); $taskSmall.Dispose()
    $taskStarFill.Dispose(); $taskStar.Dispose(); $taskTail.Dispose(); $taskOrbit.Dispose(); $taskOrbitGradient.Dispose()
    $taskTileLine.Dispose(); $taskTileGradient.Dispose(); $taskTile.Dispose()
    $taskGraphics.Dispose(); $taskLarge.Dispose()
}

$taskFile = [IO.File]::Create((Join-Path $taskAssetDirectory 'Bingyu.ico'))
$taskWriter = [IO.BinaryWriter]::new($taskFile)
$taskWriter.Write([uint16]0); $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]$taskImages.Count)
$taskOffset = 6 + 16 * $taskImages.Count
foreach ($taskImage in $taskImages) {
    $taskEncodedSize = if ($taskImage[0] -eq 256) { 0 } else { $taskImage[0] }
    $taskWriter.Write([byte]$taskEncodedSize); $taskWriter.Write([byte]$taskEncodedSize)
    $taskWriter.Write([byte]0); $taskWriter.Write([byte]0); $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]32)
    $taskWriter.Write([uint32]$taskImage[1].Length); $taskWriter.Write([uint32]$taskOffset)
    $taskOffset += $taskImage[1].Length
}
foreach ($taskImage in $taskImages) { $taskWriter.Write([byte[]]$taskImage[1]) }
$taskWriter.Dispose()
Write-Output "Updated $taskAssetDirectory\Bingyu.ico"
