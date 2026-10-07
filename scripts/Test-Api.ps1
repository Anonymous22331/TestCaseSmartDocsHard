param([string]$BaseUrl = 'http://localhost:8091')

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$client = [Net.Http.HttpClient]::new()
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifacts = Join-Path $root 'artifacts'
[IO.Directory]::CreateDirectory($artifacts) | Out-Null

function Upload([string]$Path) {
    $body = [Net.Http.MultipartFormDataContent]::new()
    $stream = [IO.File]::OpenRead($Path)
    $body.Add([Net.Http.StreamContent]::new($stream), 'file', [IO.Path]::GetFileName($Path))
    try {
        $response = $client.PostAsync("$BaseUrl/api/invoices", $body).GetAwaiter().GetResult()
        try {
            $json = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
            return @{ Status = [int]$response.StatusCode; Body = $json }
        }
        finally { $response.Dispose() }
    }
    finally { $body.Dispose() }
}

try {
    $health = Invoke-RestMethod "$BaseUrl/health"
    if ($health.status -ne 'healthy') { throw 'Health check failed.' }
    $swagger = Invoke-RestMethod "$BaseUrl/api/swagger/v1/swagger.json"
    if (!$swagger.paths.'/api/invoices'.post.requestBody.content.'multipart/form-data') { throw 'Multipart Swagger contract missing.' }
    foreach ($name in @('invoice.png', 'invoice.jpg', 'invoice-text.pdf', 'invoice-scan.pdf', 'invoice-mixed.pdf')) {
        $path = Join-Path $root "samples/$name"
        $upload = Upload $path
        if ($upload.Status -notin @(200, 202)) { throw "Upload failed: $name returned $($upload.Status)" }
        $id = $upload.Body.jobId
        $states = [Collections.Generic.List[string]]::new()
        $deadline = [DateTime]::UtcNow.AddSeconds(60)
        do {
            $result = Invoke-RestMethod "$BaseUrl/api/invoices/$id"
            if (!$states.Contains($result.status)) { $states.Add($result.status) }
            if ($result.status -in @('ready', 'needs_review', 'error')) { break }
            Start-Sleep -Milliseconds 100
        } while ([DateTime]::UtcNow -lt $deadline)
        $expected = if ($name -eq 'invoice-mixed.pdf') { 'needs_review' } else { 'ready' }
        if ($result.status -ne $expected) { throw "$name expected $expected, got $($result | ConvertTo-Json -Depth 12 -Compress)" }
        if ($result.document.amountDue -ne '2500.00') { throw "$name has an incorrect amount due." }
        $duplicate = Upload $path
        if ($duplicate.Status -ne 200 -or $duplicate.Body.jobId -ne $id) { throw "Deduplication failed for $name" }
        $result | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $artifacts "$name.result.json") -Encoding UTF8
        Write-Output "$name POST=$($upload.Status), states=$($states -join ' -> '), duplicate=200, same jobId"
    }
    $badPath = Join-Path $artifacts 'invalid.png'
    [IO.File]::WriteAllText($badPath, 'This is not an image.')
    if ((Upload $badPath).Status -ne 400) { throw 'Invalid file was not rejected.' }
    $protectedPath = Join-Path $root 'samples/invoice-protected.pdf'
    if (Test-Path -LiteralPath $protectedPath) {
        $protected = Upload $protectedPath
        if ($protected.Status -ne 400 -or $protected.Body.code -ne 'pdf_encrypted') { throw 'Protected PDF was not rejected.' }
    }
    Write-Output 'Health, Swagger, OCR, PDF, deduplication and invalid upload checks passed.'
}
finally { $client.Dispose() }
