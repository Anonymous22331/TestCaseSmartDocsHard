using System.Globalization;
using Microsoft.Extensions.Options;
using TestCaseSmartDocsHard.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace TestCaseSmartDocsHard.Services;

public sealed class DocumentRecognitionService(LocalProcessRunner runner, IOptions<ProcessingOptions> options)
{
    public async Task<RecognizedDocument> RecognizeAsync(JobInput input, CancellationToken cancellationToken)
    {
        if (input.Extension != ".pdf")
            return new RecognizedDocument([await RecognizeImageAsync(input.Path, 1, cancellationToken)]);

        using var pdf = PdfDocument.Open(input.Path);
        var pages = new List<RecognizedPage>();
        for (var number = 1; number <= pdf.NumberOfPages; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = pdf.GetPage(number);
            var words = page.GetWords(NearestNeighbourWordExtractor.Instance)
                .Where(word => !string.IsNullOrWhiteSpace(word.Text))
                .Select(word => new RecognizedWord(word.Text.Trim(), (decimal)word.BoundingBox.Left,
                    (decimal)(page.Height - word.BoundingBox.Top), (decimal)word.BoundingBox.Width,
                    (decimal)word.BoundingBox.Height, 100)).ToList();
            if (HasUsefulText(words))
            {
                pages.Add(new RecognizedPage(number, GroupLines(words)));
                continue;
            }

            var prefix = Path.Combine(Path.GetDirectoryName(input.Path)!, "page-" + number);
            var image = prefix + ".png";
            try
            {
                var pageNumber = number.ToString(CultureInfo.InvariantCulture);
                await runner.RunAsync(options.Value.PopplerPath,
                    ["-f", pageNumber, "-l", pageNumber, "-singlefile", "-scale-to", "3500", "-png", input.Path, prefix], cancellationToken);
                pages.Add(await RecognizeImageAsync(image, number, cancellationToken));
            }
            finally
            {
                if (File.Exists(image))
                    File.Delete(image);
            }
        }
        return new RecognizedDocument(pages);
    }

    private async Task<RecognizedPage> RecognizeImageAsync(string path, int number, CancellationToken cancellationToken)
    {
        var tsv = await runner.RunAsync(options.Value.TesseractPath,
            [path, "stdout", "-l", "rus+eng", "--psm", "3", "tsv"], cancellationToken);
        return new RecognizedPage(number, GroupLines(ParseTsv(tsv)));
    }

    public static IReadOnlyList<RecognizedWord> ParseTsv(string tsv)
    {
        var words = new List<RecognizedWord>();
        foreach (var line in tsv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            var parts = line.TrimEnd('\r').Split('\t', 12);
            if (parts.Length != 12 || parts[0] != "5" || string.IsNullOrWhiteSpace(parts[11]))
                continue;
            if (!decimal.TryParse(parts[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence))
                continue;
            words.Add(new RecognizedWord(parts[11], Parse(parts[6]), Parse(parts[7]), Parse(parts[8]), Parse(parts[9]),
                Math.Clamp(confidence, 0, 100), (int)Parse(parts[2]), (int)Parse(parts[4])));
        }
        return words;
    }

    public static IReadOnlyList<RecognizedLine> GroupLines(IEnumerable<RecognizedWord> source)
    {
        var rows = new List<List<RecognizedWord>>();
        foreach (var word in source.OrderBy(word => word.Y + word.Height / 2).ThenBy(word => word.X))
        {
            var row = rows.LastOrDefault();
            if (row is null || Math.Abs(row[0].Y + row[0].Height / 2 - word.Y - word.Height / 2)
                > Math.Max(2, Math.Min(row[0].Height, word.Height) * 0.55m))
            {
                row = [];
                rows.Add(row);
            }
            row.Add(word);
        }
        return rows.Select(row => new RecognizedLine(row.OrderBy(word => word.X).ToArray())).ToArray();
    }

    private static bool HasUsefulText(IReadOnlyList<RecognizedWord> words)
    {
        var text = string.Join(" ", words.Select(word => word.Text));
        return words.Count >= 5 && text.Count(char.IsLetterOrDigit) >= 30
            && text.Count(character => character == '\uFFFD' || char.IsControl(character)) <= text.Length / 20;
    }

    private static decimal Parse(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
