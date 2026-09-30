param(
    [Parameter(Mandatory)][string]$Image,
    [int]$X = 0,
    [int]$Y = 0,
    [int]$Width = 0,
    [int]$Height = 0,
    [ValidateRange(1, 3)][int]$Scale = 2,
    [ValidateSet('original', 'orange', 'list', 'status')][string]$Ink = 'original'
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
Add-Type -AssemblyName System.Runtime.WindowsRuntime
Add-Type -AssemblyName System.Drawing
$null = [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
$null = [Windows.Graphics.Imaging.BitmapDecoder, Windows.Foundation, ContentType = WindowsRuntime]
$null = [Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType = WindowsRuntime]
$null = [Windows.Globalization.Language, Windows.Globalization, ContentType = WindowsRuntime]
$asyncMethod = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and
    $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
} | Select-Object -First 1
function Wait-Operation($Operation, [Type]$ResultType) {
    $task = $asyncMethod.MakeGenericMethod($ResultType).Invoke($null, @($Operation))
    if (-not $task.Wait(15000)) { throw 'Local OCR timed out.' }
    $task.GetAwaiter().GetResult()
}

$source = $null
$scaled = $null
$graphics = $null
$stream = $null
$bitmap = $null
$temporary = Join-Path ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Image))) ('ocr-' + [Guid]::NewGuid().ToString('N') + '.png')
try {
    $source = New-Object Drawing.Bitmap([IO.Path]::GetFullPath($Image))
    if ($Width -eq 0) { $Width = $source.Width - $X }
    if ($Height -eq 0) { $Height = $source.Height - $Y }
    if ($X -lt 0 -or $Y -lt 0 -or $Width -lt 1 -or $Height -lt 1 -or
        $X + $Width -gt $source.Width -or $Y + $Height -gt $source.Height) { throw 'OCR region is outside the image.' }
    $scaled = [Drawing.Bitmap]::new(($Width * $Scale), ($Height * $Scale))
    $graphics = [Drawing.Graphics]::FromImage($scaled)
    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $destination = New-Object Drawing.Rectangle(0, 0, $scaled.Width, $scaled.Height)
    $region = New-Object Drawing.Rectangle($X, $Y, $Width, $Height)
    $graphics.DrawImage($source, $destination, $region, [Drawing.GraphicsUnit]::Pixel)
    if ($Ink -ne 'original') {
        for ($pixelY = 0; $pixelY -lt $scaled.Height; $pixelY++) {
            for ($pixelX = 0; $pixelX -lt $scaled.Width; $pixelX++) {
                $color = $scaled.GetPixel($pixelX, $pixelY)
                $orange = $color.R -gt 100 -and $color.G -lt 195 -and $color.B -lt 150 -and $color.R -gt $color.G + 25
                $white = $color.R -gt 205 -and $color.G -gt 205 -and $color.B -gt 205
                $yellow = $color.R -gt 180 -and $color.G -gt 120 -and $color.G -lt 230 -and $color.B -lt 130
                $cyan = $color.B -gt 170 -and $color.G -gt 140 -and $color.R -lt 150
                $red = $color.R -gt 190 -and $color.G -lt 140 -and $color.B -lt 140
                $foreground = ($Ink -eq 'orange' -and $orange) -or ($Ink -eq 'list' -and ($white -or $yellow)) -or ($Ink -eq 'status' -and ($cyan -or $red -or $white))
                $scaled.SetPixel($pixelX, $pixelY, $(if ($foreground) { [Drawing.Color]::Black } else { [Drawing.Color]::White }))
            }
        }
    }
    $scaled.Save($temporary, [Drawing.Imaging.ImageFormat]::Png)
    $file = Wait-Operation ([Windows.Storage.StorageFile]::GetFileFromPathAsync($temporary)) ([Windows.Storage.StorageFile])
    $stream = Wait-Operation ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
    $decoder = Wait-Operation ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
    $bitmap = Wait-Operation ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
    $language = New-Object Windows.Globalization.Language('zh-Hans-CN')
    $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage($language)
    if ($null -eq $engine) { throw 'The Windows Simplified Chinese OCR language is not installed.' }
    $result = Wait-Operation ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
    $lines = @($result.Lines | ForEach-Object {
        $words = @($_.Words | ForEach-Object {
            [ordered]@{ text = $_.Text; x = $X + $_.BoundingRect.X / $Scale; y = $Y + $_.BoundingRect.Y / $Scale;
                width = $_.BoundingRect.Width / $Scale; height = $_.BoundingRect.Height / $Scale }
        })
        [ordered]@{ text = $_.Text; words = $words }
    })
    [ordered]@{ lines = $lines } | ConvertTo-Json -Depth 8 -Compress
}
finally {
    if ($null -ne $bitmap) { $bitmap.Dispose() }
    if ($null -ne $stream) { $stream.Dispose() }
    if ($null -ne $graphics) { $graphics.Dispose() }
    if ($null -ne $scaled) { $scaled.Dispose() }
    if ($null -ne $source) { $source.Dispose() }
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
}
