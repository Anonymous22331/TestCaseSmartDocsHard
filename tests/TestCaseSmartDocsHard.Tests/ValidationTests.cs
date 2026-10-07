using System.Text.Json;
using TestCaseSmartDocsHard.Models;
using TestCaseSmartDocsHard.Services;
using TestCaseSmartDocsHard.Validation;

namespace TestCaseSmartDocsHard.Tests;

public sealed class ValidationTests
{
    [Theory]
    [InlineData("7707083893", true)]
    [InlineData("7707083894", false)]
    [InlineData("500100732259", true)]
    [InlineData("500100732258", false)]
    [InlineData("500100732249", false)]
    [InlineData("123", false)]
    [InlineData("770708389X", false)]
    [InlineData("0000000000", false)]
    [InlineData(null, false)]
    public void ValidatesInn(string? value, bool expected) => Assert.Equal(expected, RequisiteChecks.Inn(value));

    [Theory]
    [InlineData("40702810900000000000", "044525225", false, true)]
    [InlineData("40702810900000000005", "044525225", false, false)]
    [InlineData("30101810400000000225", "044525225", true, true)]
    [InlineData("30101810400000000226", "044525225", true, false)]
    [InlineData("30101810400000000225", "0445", true, false)]
    [InlineData("301018104", "044525225", true, false)]
    [InlineData("3010181040000000022X", "044525225", true, false)]
    public void ValidatesBankAccount(string account, string bik, bool correspondent, bool expected) =>
        Assert.Equal(expected, RequisiteChecks.Account(account, bik, correspondent));

    [Theory]
    [InlineData("122000.50")]
    [InlineData("122000,50")]
    [InlineData("122 000,50")]
    [InlineData("122\u00a0000,50")]
    [InlineData("122\u202f000,50")]
    public void NormalizesMoney(string value) => Assert.Equal(122000.50m, ValueNormalizer.Money(value));

    [Theory]
    [InlineData("1,2,3")]
    [InlineData("unknown")]
    [InlineData("")]
    public void RejectsInvalidMoney(string value) => Assert.Null(ValueNormalizer.Money(value));

    [Theory]
    [InlineData("15.03.2026")]
    [InlineData("15-03-2026")]
    [InlineData("15/03/2026")]
    [InlineData("2026-03-15")]
    [InlineData("15 марта 2026")]
    [InlineData("«15» марта 2026 г.")]
    public void NormalizesDates(string value) => Assert.Equal("2026-03-15", ValueNormalizer.Date(value));

    [Theory]
    [InlineData("31.02.2026")]
    [InlineData("not a date")]
    [InlineData("")]
    public void RejectsInvalidDate(string value) => Assert.Null(ValueNormalizer.Date(value));

    [Theory]
    [InlineData("20.00", false)]
    [InlineData("20.01", false)]
    [InlineData("19.99", false)]
    [InlineData("20.02", true)]
    public void ChecksLineTolerance(string amount, bool mismatch)
    {
        var document = ValidInvoice();
        document.Items[0].Amount = ValueNormalizer.Money(amount);
        document.AmountDue = document.Items[0].Amount;
        var issues = new InvoiceValidator().Validate(document);
        Assert.Equal(mismatch, issues.Any(issue => issue.Code == "line_amount_mismatch"));
    }

    [Theory]
    [InlineData("25", false)]
    [InlineData("25.01", false)]
    [InlineData("25.02", true)]
    public void ChecksMultipleLineTotal(string total, bool mismatch)
    {
        var document = ValidInvoice();
        document.Items.Add(new InvoiceItem { Name = "Service", Qty = 1, Price = 5, Amount = 5 });
        document.AmountDue = ValueNormalizer.Money(total);
        Assert.Equal(mismatch, new InvoiceValidator().Validate(document).Any(issue => issue.Code == "total_mismatch"));
    }

    [Fact]
    public void RequiresItemsAndMandatoryFields()
    {
        var issues = new InvoiceValidator().Validate(new InvoiceDocument());
        Assert.Contains(issues, issue => issue.Code == "number_missing");
        Assert.Contains(issues, issue => issue.Code == "date_unparsed");
        Assert.Contains(issues, issue => issue.Code == "seller_inn_missing");
        Assert.Contains(issues, issue => issue.Code == "amount_due_missing");
        Assert.Contains(issues, issue => issue.Code == "items_missing");
    }

    [Fact]
    public void MissingBikDoesNotSilentlyApproveAnAccount()
    {
        var document = ValidInvoice();
        document.Seller.Account = "40702810900000000000";
        Assert.Contains(new InvoiceValidator().Validate(document), issue => issue.Code == "bik_missing");
    }

    [Fact]
    public void OverflowRequiresReview()
    {
        var document = ValidInvoice();
        document.Items[0].Price = decimal.MaxValue;
        Assert.Contains(new InvoiceValidator().Validate(document), issue => issue.Code == "line_amount_mismatch");
    }

    [Fact]
    public void SerializesMoneyAsFixedPrecisionStringsAndPreservesNulls()
    {
        var document = ValidInvoice();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(document, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal("20.00", json.RootElement.GetProperty("amountDue").GetString());
        Assert.Equal("2", json.RootElement.GetProperty("items")[0].GetProperty("qty").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("vatTotal").ValueKind);
        Assert.Empty(new InvoiceValidator().Validate(document));
    }

    internal static InvoiceDocument ValidInvoice() => new()
    {
        Number = "A-17",
        Date = "2026-03-15",
        Seller = new Seller { Inn = "7707083893" },
        Items = [new InvoiceItem { Name = "Support", Qty = 2, Price = 10, Amount = 20 }],
        AmountDue = 20
    };
}
