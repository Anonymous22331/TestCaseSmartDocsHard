using TestCaseSmartDocsHard.Models;
using TestCaseSmartDocsHard.Services;
using TestCaseSmartDocsHard.Validation;

namespace TestCaseSmartDocsHard.Tests;

public sealed class ExtractionTests
{
    [Fact]
    public void ExtractsPartiesRequisitesDatesAndSpatialTable()
    {
        var result = Extract();
        var document = result.Document;
        Assert.Equal("AB-42", document.Number);
        Assert.Equal("2026-03-15", document.Date);
        Assert.Equal("7707083893", document.Seller.Inn);
        Assert.Equal("500100732259", document.Buyer.Inn);
        Assert.Equal("770701001", document.Seller.Kpp);
        Assert.Equal("044525225", document.Seller.Bik);
        Assert.Equal("40702810900000000000", document.Seller.Account);
        Assert.Equal("30101810400000000225", document.Seller.CorrAccount);
        Assert.Equal("АО Тестовый банк", document.Seller.Bank);
        Assert.Equal("ООО Альфа", document.Seller.Name);
        Assert.Equal("ИП Бета", document.Buyer.Name);
        Assert.Equal(25m, document.AmountDue);
        Assert.Null(document.VatTotal);
        Assert.Null(document.AmountWithoutVat);
        Assert.Equal(2, document.Items.Count);
        Assert.Equal("Поддержка ПО", document.Items[0].Name);
        Assert.Equal(2m, document.Items[0].Qty);
        Assert.Empty(result.Issues);
        Assert.Empty(new InvoiceValidator().Validate(document));
    }

    [Fact]
    public void SupportsScaledAndTranslatedColumns()
    {
        var result = Extract(3m, 120m);
        Assert.Equal(2, result.Document.Items.Count);
        Assert.Equal(10m, result.Document.Items[0].Price);
        Assert.Empty(new InvoiceValidator().Validate(result.Document));
    }

    [Fact]
    public void CombinesTableContinuationAcrossPages()
    {
        var source = Source();
        var recognized = new RecognizedDocument([
            new(1, source.Take(7).ToArray()),
            new(2, source.Skip(7).ToArray())
        ]);
        var result = new InvoiceExtractionService(new InvoiceTableParser()).Extract(recognized);
        Assert.Equal(2, result.Document.Items.Count);
        Assert.Empty(result.Issues);
        Assert.Empty(new InvoiceValidator().Validate(result.Document));
    }

    [Fact]
    public void UnparsedRowsRequireReview()
    {
        var source = Source().ToList();
        source.Insert(source.Count - 1, Line(190, ("3 Сломанная строка 10 ???", 0)));
        var result = new InvoiceExtractionService(new InvoiceTableParser()).Extract(new RecognizedDocument([new(1, source)]));
        Assert.Contains(result.Issues, issue => issue.Code == "table_row_unparsed");
    }

    [Fact]
    public void DoesNotInventMissingPrintedVat()
    {
        var result = Extract();
        Assert.Null(result.Document.VatTotal);
        Assert.Null(result.Document.AmountWithoutVat);
    }

    [Fact]
    public void ExtractsPrintedVatAndPrefersAmountDueOverSubtotal()
    {
        var source = Source().Concat(new[]
        {
            Line(220, ("Сумма без НДС: 25,00", 0)),
            Line(240, ("НДС (20%): 5,00", 0)),
            Line(260, ("Всего к оплате: 30,00", 0))
        }).ToArray();
        var result = new InvoiceExtractionService(new InvoiceTableParser()).Extract(new RecognizedDocument([new(1, source)]));
        Assert.Equal(25m, result.Document.AmountWithoutVat);
        Assert.Equal(5m, result.Document.VatTotal);
        Assert.Equal(30m, result.Document.AmountDue);
    }

    [Fact]
    public void ConfidenceUsesOcrAndValidation()
    {
        var extracted = Extract();
        extracted.FieldConfidence["number"] = 40;
        extracted.Document.Seller.Inn = "7707083894";
        var issues = new InvoiceValidator().Validate(extracted.Document);
        var confidence = InvoiceProcessingService.BuildConfidence(extracted, issues);
        Assert.Equal("low", confidence["seller.inn"]);
        Assert.Equal("low", confidence["number"]);
        Assert.Equal("high", confidence["date"]);
        Assert.Equal("medium", confidence["seller.name"]);
    }

    [Fact]
    public void ReadsTsvCoordinatesBlocksAndConfidence()
    {
        const string tsv = "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n5\t1\t2\t1\t3\t1\t10\t20\t30\t12\t91.50\tСчет\n";
        var word = Assert.Single(DocumentRecognitionService.ParseTsv(tsv));
        Assert.Equal("Счет", word.Text);
        Assert.Equal(91.50m, word.Confidence);
        Assert.Equal(10m, word.X);
        Assert.Equal(2, word.Block);
        Assert.Equal(3, word.Line);
    }

    private static ExtractedInvoice Extract(decimal scale = 1, decimal offset = 0)
    {
        var lines = Source().Select(line => new RecognizedLine(line.Words.Select(word => word with
        {
            X = word.X * scale + offset,
            Y = word.Y * scale,
            Width = word.Width * scale,
            Height = word.Height * scale
        }).ToArray())).ToArray();
        return new InvoiceExtractionService(new InvoiceTableParser()).Extract(new RecognizedDocument([new(1, lines)]));
    }

    private static IReadOnlyList<RecognizedLine> Source() =>
    [
        Line(0, ("Банк: АО Тестовый банк БИК: 044525225", 0)),
        Line(20, ("Р/с: 40702 8109 0000 0000 000 К/с: 30101810400000000225", 0)),
        Line(40, ("Счет на оплату № AB-42 от 15 марта 2026", 0)),
        Line(60, ("Поставщик: ООО Альфа, ИНН: 7707 083 893 КПП: 770701001", 0)),
        Line(80, ("Покупатель: ИП Бета, ИНН: 500100732259", 0)),
        Line(100, ("№", 0), ("Наименование", 30), ("Количество", 300), ("Цена", 400), ("Сумма", 500)),
        Line(130, ("1", 0), ("Поддержка ПО", 30), ("2", 310), ("10,00", 405), ("20,00", 505)),
        Line(160, ("2", 0), ("Настройка", 30), ("1", 310), ("5,00", 405), ("5,00", 505)),
        Line(200, ("Итого: 25,00", 0))
    ];

    private static RecognizedLine Line(decimal y, params (string Text, decimal X)[] words) =>
        new(words.Select(word => new RecognizedWord(word.Text, word.X, y, word.Text.Length * 5m, 10, 95)).ToArray());
}
