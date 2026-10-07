namespace TestCaseSmartDocsHard.Models;

public sealed record RecognizedWord(
    string Text, decimal X, decimal Y, decimal Width, decimal Height,
    decimal Confidence, int Block = 0, int Line = 0);

public sealed record RecognizedLine(IReadOnlyList<RecognizedWord> Words)
{
    public string Text => string.Join(" ", Words.Select(word => word.Text));
    public decimal Confidence => Words.Count == 0 ? 0 : Words.Average(word => word.Confidence);
    public decimal Y => Words.Count == 0 ? 0 : Words.Min(word => word.Y);
}

public sealed record RecognizedPage(int Number, IReadOnlyList<RecognizedLine> Lines);
public sealed record RecognizedDocument(IReadOnlyList<RecognizedPage> Pages);

public sealed class ExtractedInvoice
{
    public InvoiceDocument Document { get; } = new();
    public Dictionary<string, decimal> FieldConfidence { get; } = [];
    public List<InvoiceIssue> Issues { get; } = [];
}
