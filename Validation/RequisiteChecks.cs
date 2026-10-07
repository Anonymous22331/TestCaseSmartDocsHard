namespace TestCaseSmartDocsHard.Validation;

public static class RequisiteChecks
{
    public static bool Inn(string? value)
    {
        if (value is null || value.Length is not (10 or 12) || !value.All(char.IsAsciiDigit) || value.All(c => c == '0'))
            return false;
        return value.Length == 10
            ? Digit(value, [2, 4, 10, 3, 5, 9, 4, 6, 8]) == value[9] - '0'
            : Digit(value, [7, 2, 4, 10, 3, 5, 9, 4, 6, 8]) == value[10] - '0'
                && Digit(value, [3, 7, 2, 4, 10, 3, 5, 9, 4, 6, 8]) == value[11] - '0';
    }

    public static bool Account(string? account, string? bik, bool correspondent = false)
    {
        if (!HasDigits(account, 20) || !HasDigits(bik, 9))
            return false;
        var sequence = (correspondent ? "0" + bik!.Substring(4, 2) : bik![^3..]) + account;
        int[] weights = [7, 1, 3];
        return sequence.Select((character, index) => (character - '0') * weights[index % 3]).Sum() % 10 == 0;
    }

    public static bool HasDigits(string? value, int length) =>
        value is not null && value.Length == length && value.All(char.IsAsciiDigit);

    private static int Digit(string value, int[] weights) =>
        weights.Select((weight, index) => weight * (value[index] - '0')).Sum() % 11 % 10;
}
