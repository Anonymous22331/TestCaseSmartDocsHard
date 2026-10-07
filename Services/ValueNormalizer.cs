using System.Globalization;
using System.Text.RegularExpressions;

namespace TestCaseSmartDocsHard.Services;

public static class ValueNormalizer
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly string[] DateFormats =
    [
        "d.M.yyyy", "d-M-yyyy", "d/M/yyyy", "yyyy-MM-dd",
        "d MMMM yyyy", "d MMMM yyyy 'г.'", "d MMMM yyyy 'года'"
    ];

    public static decimal? Money(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = Regex.Replace(value, @"\s", "", RegexOptions.None, TimeSpan.FromSeconds(1))
            .Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var amount) ? amount : null;
    }

    public static string? Date(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = Regex.Replace(value.Replace("«", "").Replace("»", "").Replace("\"", "").Trim(),
            @"\s+", " ", RegexOptions.None, TimeSpan.FromSeconds(1));
        return DateOnly.TryParseExact(normalized, DateFormats, Russian, DateTimeStyles.None, out var date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    }

    public static string Digits(string value) => new(value.Where(char.IsAsciiDigit).ToArray());
}
