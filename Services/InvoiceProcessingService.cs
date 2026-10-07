using TestCaseSmartDocsHard.Models;
using TestCaseSmartDocsHard.Validation;

namespace TestCaseSmartDocsHard.Services;

public sealed class InvoiceProcessingService(
    DocumentRecognitionService recognition,
    InvoiceExtractionService extraction,
    InvoiceValidator validator)
{
    public async Task<JobResponse> ProcessAsync(JobInput input, CancellationToken cancellationToken)
    {
        var recognized = await recognition.RecognizeAsync(input, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var extracted = extraction.Extract(recognized);
        var issues = extracted.Issues.Concat(validator.Validate(extracted.Document)).ToArray();
        var confidence = BuildConfidence(extracted, issues);
        return new JobResponse(input.JobId, issues.Length == 0 ? "ready" : "needs_review",
            Document: extracted.Document, Issues: issues, Confidence: confidence);
    }

    public static IReadOnlyDictionary<string, string> BuildConfidence(ExtractedInvoice extracted, IReadOnlyList<InvoiceIssue> issues)
    {
        var result = new Dictionary<string, string>();
        foreach (var (field, confidence) in extracted.FieldConfidence)
        {
            var failed = issues.Any(issue => issue.Field == field || field.StartsWith(issue.Field + ".", StringComparison.Ordinal)
                || issue.Field == "items" && field.StartsWith("items[", StringComparison.Ordinal));
            var validated = field is "date" or "seller.inn" or "buyer.inn"
                || field is "seller.account" or "seller.corrAccount" && extracted.Document.Seller.Bik is not null
                || field == "amountDue" && extracted.Document.Items.Count > 0
                || field.StartsWith("items[", StringComparison.Ordinal) && !field.EndsWith(".name", StringComparison.Ordinal);
            result[field] = failed || confidence < 70 ? "low" : validated && confidence >= 85 ? "high" : "medium";
        }
        foreach (var issue in issues)
            result[issue.Field] = "low";
        return result;
    }
}
