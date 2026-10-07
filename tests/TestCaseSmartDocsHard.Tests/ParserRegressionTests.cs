using TestCaseSmartDocsHard.Models;
using TestCaseSmartDocsHard.Services;
using TestCaseSmartDocsHard.Validation;

namespace TestCaseSmartDocsHard.Tests;

public sealed class ParserRegressionTests
{
    [Theory]
    [InlineData("В том числе НДС 20% 20,00")]
    [InlineData("В том числе НДС 20%: 20,00")]
    [InlineData("НДС (20%) 20.00 руб.")]
    [InlineData("Сумма НДС: 20,00")]
    [InlineData("НДС 20,00")]
    [InlineData("В т.ч. НДС 20% 20,00")]
    public void ExtractsPrintedVatWithOrWithoutColon(string text)
    {
        var lines = Source();
        lines.Insert(lines.Count - 1, Line(95, (text, 0)));
        var result = Extract(lines);
        Assert.Equal(20m, result.Document.VatTotal);
        Assert.Null(result.Document.AmountWithoutVat);
        AssertValid(result);
    }

    [Theory]
    [InlineData("Без НДС: 100,00")]
    [InlineData("Без НДС 100,00")]
    [InlineData("Сумма без НДС: 100.00 RUB")]
    [InlineData("Итого без НДС 100,00")]
    public void ExtractsPrintedAmountWithoutVat(string text)
    {
        var lines = Source();
        lines.Insert(lines.Count - 1, Line(95, (text, 0)));
        var result = Extract(lines);
        Assert.Equal(100m, result.Document.AmountWithoutVat);
        Assert.Null(result.Document.VatTotal);
        AssertValid(result);
    }

    [Theory]
    [InlineData("Без НДС")]
    [InlineData("НДС 20%")]
    [InlineData("НДС (20%)")]
    [InlineData("НДС: не облагается")]
    public void DoesNotUseVatRateOrExemptionAsPrintedAmount(string text)
    {
        var lines = Source();
        lines.Add(Line(120, (text, 0)));
        var result = Extract(lines);
        Assert.Null(result.Document.AmountWithoutVat);
        Assert.Null(result.Document.VatTotal);
        AssertValid(result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreservesNumericNameAndSeparatesOnlyExplicitIndexColumn(bool hasIndex)
    {
        var result = Extract(Source(hasIndex: hasIndex));
        Assert.Equal("10 лицензий ПО", Assert.Single(result.Document.Items).Name);
        AssertValid(result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextFallbackPreservesNumericName(bool hasIndex)
    {
        var lines = Source(hasIndex: hasIndex);
        lines[4] = Line(80, ((hasIndex ? "1 " : "") + "10 лицензий ПО 1 120,00 120,00", 30));
        var result = Extract(lines);
        Assert.Equal("10 лицензий ПО", Assert.Single(result.Document.Items).Name);
        AssertValid(result);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(3, 120)]
    public void RecognizesTwoLineQuantityHeader(int scale, int offset)
    {
        var lines = Source(splitHeader: true);
        var transformed = lines.Select(line => new RecognizedLine(line.Words.Select(word => word with
        {
            X = word.X * scale + offset,
            Y = word.Y * scale,
            Width = word.Width * scale,
            Height = word.Height * scale
        }).ToArray())).ToArray();
        var result = Extract(transformed);
        var item = Assert.Single(result.Document.Items);
        Assert.Equal("10 лицензий ПО", item.Name);
        Assert.Equal(1m, item.Qty);
        Assert.Equal(120m, item.Price);
        Assert.Equal(120m, item.Amount);
        AssertValid(result);
    }

    [Fact]
    public void DoesNotTreatMisalignedSuffixAsAHeaderContinuation()
    {
        var lines = Source(splitHeader: true);
        lines[4] = Line(70, ("во", 450));
        var result = Extract(lines);
        Assert.Empty(result.Document.Items);
        Assert.Contains(new InvoiceValidator().Validate(result.Document), issue => issue.Code == "items_missing");
    }

    [Fact]
    public void KeepsItemImmediatelyBeforeRepeatedHeader()
    {
        var lines = Source();
        lines.Insert(5, Line(95, ("Наименование", 30), ("Количество", 300), ("Цена", 400), ("Сумма", 500)));
        var result = Extract(lines);
        Assert.Single(result.Document.Items);
        AssertValid(result);
    }

    private static List<RecognizedLine> Source(bool hasIndex = false, bool splitHeader = false)
    {
        var header = new List<(string, decimal)> { ("Наименование", 30), (splitHeader ? "Кол-" : "Количество", 300), ("Цена", 400), ("Сумма", 500) };
        var row = new List<(string, decimal)> { ("10 лицензий ПО", 30), ("1", 300), ("120,00", 400), ("120,00", 500) };
        if (hasIndex)
        {
            header.Insert(0, ("№", 0));
            row.Insert(0, ("1", 0));
        }
        var lines = new List<RecognizedLine>
        {
            Line(0, ("Счет № AUDIT-1 от 15.03.2026", 0)),
            Line(20, ("Поставщик: ООО Альфа, ИНН: 7707083893", 0)),
            Line(40, ("Покупатель: ИП Бета, ИНН: 500100732259", 0)),
            Line(60, header.ToArray()),
            Line(80, row.ToArray()),
            Line(110, ("Итого: 120,00", 0))
        };
        if (splitHeader)
            lines.Insert(4, Line(70, ("во", 300)));
        return lines;
    }

    private static RecognizedLine Line(decimal y, params (string Text, decimal X)[] words) =>
        new(words.Select(word => new RecognizedWord(word.Text, word.X, y, word.Text.Length * 5m, 10, 95)).ToArray());

    private static ExtractedInvoice Extract(IReadOnlyList<RecognizedLine> lines) =>
        new InvoiceExtractionService(new InvoiceTableParser()).Extract(new RecognizedDocument([new(1, lines)]));

    private static void AssertValid(ExtractedInvoice result)
    {
        Assert.Empty(result.Issues);
        Assert.Empty(new InvoiceValidator().Validate(result.Document));
    }
}
