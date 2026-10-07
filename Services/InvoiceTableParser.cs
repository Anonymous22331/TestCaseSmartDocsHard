using System.Text.RegularExpressions;
using TestCaseSmartDocsHard.Models;

namespace TestCaseSmartDocsHard.Services;

public sealed class InvoiceTableParser
{
    public void Extract(RecognizedDocument recognized, ExtractedInvoice result)
    {
        foreach (var page in recognized.Pages)
        {
            List<(string Name, decimal X)>? columns = null;
            var inTable = false;
            foreach (var line in page.Lines)
            {
                if (TryColumns(line, out var header))
                {
                    columns = header;
                    inTable = true;
                    continue;
                }
                if (InvoiceExtractionService.Is(line.Text, @"^\s*(?:итого|всего|сумма\s+НДС|НДС|в\s+том\s+числе|к\s+оплате|total|amount\s+due)\b"))
                {
                    inTable = false;
                    continue;
                }
                if (!inTable)
                    continue;

                InvoiceItem? item = null;
                if (columns is not null)
                    item = ReadColumns(line, columns);
                item ??= ReadText(line.Text);
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

    private static bool TryColumns(RecognizedLine line, out List<(string Name, decimal X)> columns)
    {
        columns = [];
        foreach (var word in line.Words)
        {
            var text = word.Text.Trim('.', ':').ToLowerInvariant();
            var name = text switch
            {
                "наименование" or "товары" or "товар" or "услуги" or "description" or "name" => "name",
                "количество" or "кол-во" or "кол." or "кол" or "qty" or "quantity" => "qty",
                "ед" or "единица" or "unit" => "unit",
                "цена" or "price" => "price",
                "сумма" or "amount" => "amount",
                _ => null
            };
            if (name is not null && columns.All(column => column.Name != name))
                columns.Add((name, word.X - Math.Max(3, word.Height / 2)));
        }
        columns = columns.OrderBy(column => column.X).ToList();
        var names = columns.Select(column => column.Name).ToHashSet();
        return new[] { "name", "qty", "price", "amount" }.All(names.Contains);
    }

    private static InvoiceItem? ReadColumns(RecognizedLine line, List<(string Name, decimal X)> columns)
    {
        var cells = new Dictionary<string, string>();
        for (var index = 0; index < columns.Count; index++)
        {
            var left = columns[index].Name == "name" ? decimal.MinValue : columns[index].X;
            var right = index + 1 < columns.Count ? columns[index + 1].X : decimal.MaxValue;
            cells[columns[index].Name] = string.Join(" ", line.Words
                .Where(word => word.X + word.Width / 2 >= left && word.X + word.Width / 2 < right).Select(word => word.Text));
        }
        var qty = ValueNormalizer.Money(Regex.Replace(cells["qty"], @"\s*(?:шт\.?|усл\.?|час\.?|кг|м2|м²|pcs)\s*$", "", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)));
        var price = ValueNormalizer.Money(cells["price"]);
        var amount = ValueNormalizer.Money(cells["amount"]);
        var name = Regex.Replace(cells["name"], @"^\s*\d+[.)]?\s+", "", RegexOptions.None, TimeSpan.FromSeconds(1)).Trim();
        if (qty is null && price is null && amount is null || string.IsNullOrWhiteSpace(name))
            return null;
        return new InvoiceItem { Name = name, Qty = qty, Price = price, Amount = amount };
    }

    private static InvoiceItem? ReadText(string text)
    {
        const string money = @"\d+(?:[ \u00a0\u202f]\d{3})*[.,]\d{2}";
        var row = InvoiceExtractionService.Match(text,
            @"^\s*(?:\d+[.)]?\s+)?(?<name>.+?)\s+(?<qty>\d+(?:[.,]\d+)?)\s+(?:(?:шт\.?|усл\.?|час\.?|кг|pcs)\s+)?(?<price>" + money + @")\s+(?<amount>" + money + @")\s*$");
        return !row.Success ? null : new InvoiceItem
        {
            Name = row.Groups["name"].Value,
            Qty = ValueNormalizer.Money(row.Groups["qty"].Value),
            Price = ValueNormalizer.Money(row.Groups["price"].Value),
            Amount = ValueNormalizer.Money(row.Groups["amount"].Value)
        };
    }
}
