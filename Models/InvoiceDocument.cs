using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestCaseSmartDocsHard.Models;

public sealed class InvoiceDocument
{
    public string? Number { get; set; }
    public string? Date { get; set; }
    public Seller Seller { get; init; } = new();
    public Buyer Buyer { get; init; } = new();
    public List<InvoiceItem> Items { get; init; } = [];
    [JsonConverter(typeof(MoneyConverter))]
    public decimal? AmountWithoutVat { get; set; }
    [JsonConverter(typeof(MoneyConverter))]
    public decimal? VatTotal { get; set; }
    [JsonConverter(typeof(MoneyConverter))]
    public decimal? AmountDue { get; set; }
    public string Currency { get; set; } = "RUB";
}

public sealed class Seller
{
    public string? Name { get; set; }
    public string? Inn { get; set; }
    public string? Kpp { get; set; }
    public string? Bank { get; set; }
    public string? Bik { get; set; }
    public string? Account { get; set; }
    public string? CorrAccount { get; set; }
}

public sealed class Buyer
{
    public string? Name { get; set; }
    public string? Inn { get; set; }
}

public sealed class InvoiceItem
{
    public string? Name { get; set; }
    [JsonConverter(typeof(QuantityConverter))]
    public decimal? Qty { get; set; }
    [JsonConverter(typeof(MoneyConverter))]
    public decimal? Price { get; set; }
    [JsonConverter(typeof(MoneyConverter))]
    public decimal? Amount { get; set; }
}

public sealed class MoneyConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? decimal.Parse(reader.GetString()!, CultureInfo.InvariantCulture) : reader.GetDecimal();

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("F2", CultureInfo.InvariantCulture));
}

public sealed class QuantityConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? decimal.Parse(reader.GetString()!, CultureInfo.InvariantCulture) : reader.GetDecimal();

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("0.############################", CultureInfo.InvariantCulture));
}
