using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TestCaseSmartDocsHard.Models;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace TestCaseSmartDocsHard.Tests;

public sealed class ApiTests : IClassFixture<InvoiceApplication>
{
    private readonly HttpClient client;

    public ApiTests(InvoiceApplication application) => client = application.CreateClient();

    [Fact]
    public async Task ProcessesTextPdfAndDeduplicatesConcurrentUploads()
    {
        var bytes = CreatePdf();
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Upload(bytes, "invoice.pdf")));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Accepted);
        Assert.Equal(7, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        var ids = new List<Guid>();
        foreach (var response in responses)
        {
            using (response)
                ids.Add((await response.Content.ReadFromJsonAsync<JobAccepted>())!.JobId);
        }
        var id = Assert.Single(ids.Distinct());
        var result = await WaitForCompletion(id);
        Assert.Equal("ready", result.GetProperty("status").GetString());
        Assert.Equal("25.00", result.GetProperty("document").GetProperty("amountDue").GetString());
        Assert.Equal(2, result.GetProperty("document").GetProperty("items").GetArrayLength());
        Assert.Equal(0, result.GetProperty("issues").GetArrayLength());
        Assert.Equal("1.0", result.GetProperty("schemaVersion").GetString());
    }

    [Fact]
    public async Task ReturnsReviewForFinancialMismatch()
    {
        using var response = await Upload(CreatePdf(total: "30.00"), "mismatch.pdf");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JobAccepted>())!.JobId;
        var result = await WaitForCompletion(id);
        Assert.Equal("needs_review", result.GetProperty("status").GetString());
        Assert.Contains(result.GetProperty("issues").EnumerateArray(), issue => issue.GetProperty("code").GetString() == "total_mismatch");
    }

    [Theory]
    [InlineData("empty.png", "")]
    [InlineData("invoice.exe", "not allowed")]
    [InlineData("invoice.png", "wrong signature")]
    [InlineData("invoice.pdf", "%PDF-1.7 damaged")]
    public async Task RejectsInvalidUploads(string name, string content)
    {
        using var response = await Upload(Encoding.UTF8.GetBytes(content), name);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RejectsPasswordProtectedPdf()
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "invoice-protected.pdf"));
        using var response = await Upload(bytes, "protected.pdf");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("pdf_encrypted", (await response.Content.ReadFromJsonAsync<ApiError>())!.Code);
    }

    [Fact]
    public async Task RejectsTooManyPdfPages()
    {
        using var response = await Upload(CreatePdf(pages: 11), "eleven.pdf");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("pdf_page_limit", (await response.Content.ReadFromJsonAsync<ApiError>())!.Code);
    }

    [Fact]
    public async Task RejectsOversizedFiles()
    {
        using var response = await Upload(new byte[20 * 1024 * 1024 + 1], "large.png");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReturnsNotFoundForUnknownJob()
    {
        using var response = await client.GetAsync($"/api/invoices/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MissingExecutableProducesSafeError()
    {
        using var response = await Upload([137, 80, 78, 71, 13, 10, 26, 10, 1], "image.png");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JobAccepted>())!.JobId;
        var result = await WaitForCompletion(id);
        Assert.Equal("error", result.GetProperty("status").GetString());
        Assert.Equal("executable_unavailable", result.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("stack", result.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SwaggerDescribesMultipartUploadAndHealthIsAvailable()
    {
        using var response = await client.GetAsync("/api/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("paths").TryGetProperty("/api/invoices", out var endpoint));
        Assert.True(endpoint.GetProperty("post").GetProperty("requestBody").GetProperty("content").TryGetProperty("multipart/form-data", out var multipart));
        Assert.Equal("binary", multipart.GetProperty("schema").GetProperty("properties").GetProperty("file").GetProperty("format").GetString());
        using var health = await client.GetAsync("/health");
        health.EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> Upload(byte[] bytes, string name)
    {
        using var body = new MultipartFormDataContent();
        body.Add(new ByteArrayContent(bytes), "file", name);
        return await client.PostAsync("/api/invoices", body);
    }

    private async Task<JsonElement> WaitForCompletion(Guid id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        JsonElement result = default;
        while (true)
        {
            try
            {
                result = await client.GetFromJsonAsync<JsonElement>($"/api/invoices/{id}", timeout.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail($"Job did not complete. Last response: {result}");
            }
            if (result.GetProperty("status").GetString() is "ready" or "needs_review" or "error")
                return result;
            if (timeout.IsCancellationRequested)
                Assert.Fail($"Job did not complete. Last response: {result}");
            await Task.Delay(20);
        }
    }

    private static byte[] CreatePdf(int pages = 1, string total = "25.00")
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (var i = 0; i < pages; i++)
        {
            var page = builder.AddPage(595, 842);
            void Text(string value, int x, int y) => page.AddText(value, 10, new PdfPoint(x, y), font);
            Text("Invoice # INV-42 dated 15.03.2026", 40, 780);
            Text("Seller: Alpha INN: 7707083893", 40, 750);
            Text("Buyer: Beta INN: 500100732259", 40, 730);
            Text("Name", 40, 690);
            Text("Qty", 300, 690);
            Text("Price", 390, 690);
            Text("Amount", 480, 690);
            Text("Support", 40, 660);
            Text("2", 300, 660);
            Text("10.00", 390, 660);
            Text("20.00", 480, 660);
            Text("Setup", 40, 630);
            Text("1", 300, 630);
            Text("5.00", 390, 630);
            Text("5.00", 480, 630);
            Text("Total: " + total, 390, 600);
        }
        return builder.Build();
    }
}

public sealed class InvoiceApplication : WebApplicationFactory<Program>
{
    private readonly string storage = Path.Combine(Path.GetTempPath(), "smartdocs-tests-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services => services.PostConfigure<ProcessingOptions>(options =>
        {
            options.StoragePath = storage;
            options.TesseractPath = Path.Combine(storage, "missing-tesseract");
        }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(storage))
            Directory.Delete(storage, true);
    }
}
