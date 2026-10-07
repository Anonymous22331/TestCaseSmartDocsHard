using System.Text.RegularExpressions;
using TestCaseSmartDocsHard.Models;

namespace TestCaseSmartDocsHard.Services;

public sealed class InvoiceTableParser
{
    public void Extract(RecognizedDocument recognized, ExtractedInvoice result)
    {
        List<(string Name, decimal X)>? columns = null;
        var inTable = false;
        foreach (var page in recognized.Pages)
        {
            for (var lineIndex = 0; lineIndex < page.Lines.Count; lineIndex++)
            {
                var line = page.Lines[lineIndex];
                if (TryColumns(line, out var header))
                {
                    columns = header;
                    inTable = true;
                    continue;
                }
                if (lineIndex + 1 < page.Lines.Count && TryContinuedHeader(line, page.Lines[lineIndex + 1], out header))
                {
                    columns = header;
                    inTable = true;
                    lineIndex++;
                    continue;
                }
                if (InvoiceExtractionService.Is(line.Text, @"^\s*(?:итого|всего|сумма\s+(?:без\s+)?НДС|без\s+НДС|НДС|в\s+том\s+числе|в\s*т\.?\s*ч\.?|к\s+оплате|total|amount\s+due)\b"))
                {
                    inTable = false;
                    continue;
                }
                if (!inTable)
                    continue;

                InvoiceItem? item = null;
                if (columns is not null)
                    item = ReadColumns(line, columns);
                item ??= ReadText(line.Text, columns?.Any(column => column.Name == "index") == true);
                if (item is not null)
                {
                    var index = result.Document.Items.Count;
                    result.Document.Items.Add(item);
                    foreach (var field in new[] { "name", "qty", "price", "amount" })
                        result.FieldConfidence[$"items[{index}].{field}"] = line.Confidence;
                }
                else if (result.Document.Items.Count > 0 && !line.Text.Any(char.IsDigit)
                    && columns is not null && line.Words.All(word => word.X < columns.First(c => c.Name == "qty").X))
                {
                    result.Document.Items[^1].Name += " " + line.Text;
                }
                else if (line.Text.Any(char.IsLetterOrDigit))
                {
                    result.Issues.Add(new("table_row_unparsed", "items", "A table row could not be parsed reliably."));
                }
            }
        }
    }

    private static bool TryContinuedHeader(RecognizedLine first, RecognizedLine second, out List<(string Name, decimal X)> columns)
    {
        columns = [];
        if (first.Words.Count == 0 || second.Words.Count == 0)
            return false;
        if (first.Words.Count(word => ColumnName(word.Text) is not null) < 2)
            return false;
        var height = first.Words.Max(word => word.Height);
        if (second.Y - first.Words.Max(word => word.Y + word.Height) > height * 1.5m)
            return false;

        var words = first.Words.ToList();
        foreach (var continuation in second.Words)
        {
            var index = words.FindIndex(word => word.Text.EndsWith('-')
                && Math.Abs(word.X - continuation.X) <= Math.Max(word.Height, continuation.Height));
            if (index >= 0)
                words[index] = words[index] with { Text = words[index].Text + continuation.Text };
            else if (ColumnName(continuation.Text) is not null)
                words.Add(continuation);
            else
                return false;
        }
        return TryColumns(new RecognizedLine(words), out columns);
    }

    private static bool TryColumns(RecognizedLine line, out List<(string Name, decimal X)> columns)
    {
        columns = [];
        foreach (var word in line.Words)
        {
            var name = ColumnName(word.Text);
            if (name is not null && columns.All(column => column.Name != name))
                columns.Add((name, word.X - Math.Max(3, word.Height / 2)));
        }
        columns = columns.OrderBy(column => column.X).ToList();
        var names = columns.Select(column => column.Name).ToHashSet();
        return new[] { "name", "qty", "price", "amount" }.All(names.Contains);
    }

    private static string? ColumnName(string text) => text.Trim('.', ':').ToLowerInvariant() switch
    {
        "№" or "#" or "no" or "n" => "index",
        "наименование" or "товары" or "товар" or "услуги" or "description" or "name" => "name",
        "количество" or "кол-во" or "кол" or "qty" or "quantity" => "qty",
        "ед" or "единица" or "unit" => "unit",
        "цена" or "price" => "price",
        "сумма" or "amount" => "amount",
        _ => null
    };

    private static InvoiceItem? ReadColumns(RecognizedLine line, List<(string Name, decimal X)> columns)
    {
        var cells = new Dictionary<string, string>();
        for (var index = 0; index < columns.Count; index++)
        {
            var left = index == 0 ? decimal.MinValue : columns[index].X;
            var right = index + 1 < columns.Count ? columns[index + 1].X : decimal.MaxValue;
            cells[columns[index].Name] = string.Join(" ", line.Words
                .Where(word => word.X + word.Width / 2 >= left && word.X + word.Width / 2 < right).Select(word => word.Text));
        }
        var qty = ValueNormalizer.Money(Regex.Replace(cells["qty"], @"\s*(?:шт\.?|усл\.?|час\.?|кг|м2|м²|pcs)\s*$", "", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)));
        var price = ValueNormalizer.Money(cells["price"]);
        var amount = ValueNormalizer.Money(cells["amount"]);
        var name = cells["name"].Trim();
        if (qty is null && price is null && amount is null || string.IsNullOrWhiteSpace(name))
            return null;
        return new InvoiceItem { Name = name, Qty = qty, Price = price, Amount = amount };
    }

    private static InvoiceItem? ReadText(string text, bool hasIndex)
    {
        const string money = @"\d+(?:[ \u00a0\u202f]\d{3})*[.,]\d{2}";
        var prefix = hasIndex ? @"^\s*\d+[.)]?\s+" : @"^\s*";
        var row = InvoiceExtractionService.Match(text,
            prefix + @"(?<name>.+?)\s+(?<qty>\d+(?:[.,]\d+)?)\s+(?:(?:шт\.?|усл\.?|час\.?|кг|pcs)\s+)?(?<price>" + money + @")\s+(?<amount>" + money + @")\s*$");
        return !row.Success ? null : new InvoiceItem
        {
            Name = row.Groups["name"].Value,
            Qty = ValueNormalizer.Money(row.Groups["qty"].Value),
            Price = ValueNormalizer.Money(row.Groups["price"].Value),
            Amount = ValueNormalizer.Money(row.Groups["amount"].Value)
        };
    }
}
