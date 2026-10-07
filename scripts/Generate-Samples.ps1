param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../samples'))

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$output = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($output) | Out-Null
$bitmap = [Drawing.Bitmap]::new(1800, 1250)
$bitmap.SetResolution(200, 200)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$graphics.Clear([Drawing.Color]::White)
$graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$font = [Drawing.Font]::new('Arial', 25, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
$title = [Drawing.Font]::new('Arial', 34, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
$brush = [Drawing.Brushes]::Black
try {
    $graphics.DrawString('Счет на оплату № DEMO-42 от 15.03.2026', $title, $brush, 70, 70)
    $lines = @(
        'Поставщик: ООО Альфа, ИНН: 7707083893 КПП: 770701001',
        'Банк: АО Тестовый банк',
        'БИК: 044525225',
        'Р/с: 40702810900000000000',
        'К/с: 30101810400000000225',
        'Покупатель: ИП Бета, ИНН: 500100732259'
    )
    $y = 160
    foreach ($line in $lines) {
        $graphics.DrawString($line, $font, $brush, 70, $y)
        $y += 65
    }
    $rows = @(
        @('Наименование', 'Количество', 'Цена', 'Сумма'),
        @('Поддержка ПО', '2', '1000,00', '2000,00'),
        @('Настройка оборудования', '1', '500,00', '500,00')
    )
    $y = 620
    foreach ($row in $rows) {
        $graphics.DrawString($row[0], $font, $brush, 70, $y)
        $graphics.DrawString($row[1], $font, $brush, 900, $y)
        $graphics.DrawString($row[2], $font, $brush, 1200, $y)
        $graphics.DrawString($row[3], $font, $brush, 1500, $y)
        $y += 90
    }
    $graphics.DrawString('Итого: 2500,00', $title, $brush, 1180, 940)
    $graphics.DrawString('Без НДС', $font, $brush, 70, 1040)
    $bitmap.Save((Join-Path $output 'invoice.png'), [Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Save((Join-Path $output 'invoice.jpg'), [Drawing.Imaging.ImageFormat]::Jpeg)
}
finally {
    $font.Dispose()
    $title.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

function Write-Pdf([string]$Path, [bool]$Scan, [bool]$Mixed = $false) {
    $stream = [IO.MemoryStream]::new()
    $offsets = [Collections.Generic.List[long]]::new()
    function Write-Ascii([string]$Text) {
        $bytes = [Text.Encoding]::ASCII.GetBytes($Text)
        $stream.Write($bytes, 0, $bytes.Length)
    }
    function Write-Object([int]$Number, [string]$Text) {
        $offsets.Add($stream.Position)
        Write-Ascii "$Number 0 obj`n$Text`nendobj`n"
    }
    Write-Ascii "%PDF-1.4`n"
    Write-Object 1 '<< /Type /Catalog /Pages 2 0 R >>'
    $kids = if ($Mixed) { '/Kids [3 0 R 7 0 R] /Count 2' } else { '/Kids [3 0 R] /Count 1' }
    Write-Object 2 "<< /Type /Pages $kids >>"
    Write-Object 3 '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 720 500] /Resources << /Font << /F1 5 0 R >> /XObject << /Im0 6 0 R >> >> /Contents 4 0 R >>'
    $content = if ($Scan) { "q 720 0 0 500 0 0 cm /Im0 Do Q`n" } else {
        @'
BT /F1 14 Tf 30 460 Td (Invoice # TEXT-42 dated 15.03.2026) Tj ET
BT /F1 12 Tf 30 425 Td (Seller: Alpha INN: 7707083893) Tj ET
BT /F1 12 Tf 30 400 Td (Buyer: Beta INN: 500100732259) Tj ET
BT /F1 12 Tf 30 330 Td (Name) Tj ET
BT /F1 12 Tf 360 330 Td (Qty) Tj ET
BT /F1 12 Tf 480 330 Td (Price) Tj ET
BT /F1 12 Tf 600 330 Td (Amount) Tj ET
BT /F1 12 Tf 30 285 Td (Software support) Tj ET
BT /F1 12 Tf 360 285 Td (2) Tj ET
BT /F1 12 Tf 480 285 Td (1000.00) Tj ET
BT /F1 12 Tf 600 285 Td (2000.00) Tj ET
BT /F1 12 Tf 30 240 Td (Equipment setup) Tj ET
BT /F1 12 Tf 360 240 Td (1) Tj ET
BT /F1 12 Tf 480 240 Td (500.00) Tj ET
BT /F1 12 Tf 600 240 Td (500.00) Tj ET
BT /F1 14 Tf 480 170 Td (Total: 2500.00) Tj ET
'@
    }
    Write-Object 4 "<< /Length $([Text.Encoding]::ASCII.GetByteCount($content)) >>`nstream`n$content`nendstream"
    Write-Object 5 '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>'
    $jpeg = [IO.File]::ReadAllBytes((Join-Path $output 'invoice.jpg'))
    $offsets.Add($stream.Position)
    Write-Ascii "6 0 obj`n<< /Type /XObject /Subtype /Image /Width 1800 /Height 1250 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length $($jpeg.Length) >>`nstream`n"
    $stream.Write($jpeg, 0, $jpeg.Length)
    Write-Ascii "`nendstream`nendobj`n"
    if ($Mixed) {
        Write-Object 7 '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 720 500] /Resources << /XObject << /Im0 6 0 R >> >> /Contents 8 0 R >>'
        $scanContent = "q 720 0 0 500 0 0 cm /Im0 Do Q`n"
        Write-Object 8 "<< /Length $($scanContent.Length) >>`nstream`n$scanContent`nendstream"
    }
    $xref = $stream.Position
    Write-Ascii "xref`n0 $($offsets.Count + 1)`n0000000000 65535 f `n"
    foreach ($offset in $offsets) { Write-Ascii ($offset.ToString('D10') + " 00000 n `n") }
    Write-Ascii "trailer`n<< /Size $($offsets.Count + 1) /Root 1 0 R >>`nstartxref`n$xref`n%%EOF`n"
    [IO.File]::WriteAllBytes($Path, $stream.ToArray())
    $stream.Dispose()
}

Write-Pdf (Join-Path $output 'invoice-text.pdf') $false
Write-Pdf (Join-Path $output 'invoice-scan.pdf') $true
Write-Pdf (Join-Path $output 'invoice-mixed.pdf') $false $true
Write-Output "Generated synthetic invoice samples in $output"
