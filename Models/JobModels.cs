namespace TestCaseSmartDocsHard.Models;

public sealed record JobAccepted(Guid JobId);
public sealed record ApiError(string Code, string Message);
public sealed record InvoiceIssue(string Code, string Field, string Message);

public sealed record JobResponse(
    Guid JobId,
    string Status,
    string SchemaVersion = "1.0",
    InvoiceDocument? Document = null,
    IReadOnlyList<InvoiceIssue>? Issues = null,
    IReadOnlyDictionary<string, string>? Confidence = null,
    ApiError? Error = null);

public sealed record JobInput(Guid JobId, string Path, string Extension);
public sealed record Submission(Guid JobId, bool IsDuplicate);

public sealed class UploadRequest
{
    public IFormFile? File { get; init; }
}

public sealed class ProcessingOptions
{
    public string StoragePath { get; set; } = "artifacts/jobs";
    public string TesseractPath { get; set; } = "tesseract";
    public string PopplerPath { get; set; } = "pdftoppm";
    public int ProcessTimeoutSeconds { get; set; } = 60;
    public int JobTimeoutSeconds { get; set; } = 600;
    public int QueueCapacity { get; set; } = 100;
    public int MaxStoredJobs { get; set; } = 10000;
}

public sealed class IntakeException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class ProcessingException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
