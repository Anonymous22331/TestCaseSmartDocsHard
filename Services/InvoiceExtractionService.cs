using System.Text.RegularExpressions;
using TestCaseSmartDocsHard.Models;

namespace TestCaseSmartDocsHard.Services;

public sealed class InvoiceExtractionService(InvoiceTableParser tableParser)
{
    private const string SellerLabel = @"(?:поставщик|продавец|исполнитель|получатель|seller|supplier)";
    private const string BuyerLabel = @"(?:покупатель|заказчик|плательщик|buyer|customer)";
    private const string DatePattern = @"(?:\d{4}-\d{2}-\d{2}|\d{1,2}[./-]\d{1,2}[./-]\d{4}|[«""]?\d{1,2}[»""]?\s+[а-я]+\s+\d{4})";
    private const string AmountPattern = @"[-+]?\d+(?:[ \u00a0\u202f]\d{3})*(?:[.,]\d{1,2})?";

    public ExtractedInvoice Extract(RecognizedDocument recognized)
    {
        var result = new ExtractedInvoice();
        var document = result.Document;
        var lines = recognized.Pages.SelectMany(page => page.Lines).ToArray();
        var buyerSection = false;
        var duePriority = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var text = line.Text.Replace('ё', 'е').Replace('Ё', 'Е');
            var invoice = Match(text, @"(?:счет(?:\s+на\s+оплату)?|invoice)\s*(?:№|N[oо]?\.?|#)\s*(?<number>[^\s|]+)");
            if (invoice.Success)
            {
                document.Number = invoice.Groups["number"].Value.TrimEnd(',', ';');
                Record("number");
                var date = Match(text[invoice.Index..], DatePattern);
                if (date.Success)
                {
                    document.Date = ValueNormalizer.Date(date.Value);
                    Record("date");
                }
            }
            if (document.Date is null && (Is(text, @"(?:дата|date)\s*:") || index > 0 && Is(lines[index - 1].Text, @"сч[её]т|invoice")))
            {
                var date = Match(text, DatePattern);
                if (date.Success)
                {
                    document.Date = ValueNormalizer.Date(date.Value);
                    Record("date");
                }
            }

            if (Is(text, BuyerLabel + @"\s*[:(]"))
                buyerSection = true;
            else if (Is(text, SellerLabel + @"\s*[:(]"))
                buyerSection = false;

            var name = Match(text, (buyerSection ? BuyerLabel : SellerLabel) + @"\s*(?:\([^)]*\))?\s*:\s*(?<name>.*?)(?=\s*(?:,?\s*(?:ИНН|INN)|,?\s*КПП|,?\s*адрес)\b|$)");
            if (name.Success)
            {
                var value = name.Groups["name"].Value.Trim(' ', ',', ';');
                if (value.Length == 0 && index + 1 < lines.Length && !Is(lines[index + 1].Text, @"ИНН|КПП|БИК|счет|покупатель|поставщик"))
                    value = lines[index + 1].Text.Trim();
                if (value.Length > 0)
                {
                    if (buyerSection)
                        document.Buyer.Name = value;
                    else
                        document.Seller.Name = value;
                    Record(buyerSection ? "buyer.name" : "seller.name");
                }
            }

            var inn = Requisite(text, @"(?:ИНН|INN)(?:\s*/\s*КПП)?");
            if (inn is not null)
            {
                if (buyerSection)
                    document.Buyer.Inn = inn;
                else
                    document.Seller.Inn = inn;
                Record(buyerSection ? "buyer.inn" : "seller.inn");
            }
            if (!buyerSection)
            {
                Assign(Requisite(text, @"КПП"), "seller.kpp", value => document.Seller.Kpp = value);
                var combined = Match(text, @"ИНН\s*/\s*КПП\s*:?\s*[\d\s-]+/\s*(?<value>[\d\s-]+)");
                if (combined.Success)
                    Assign(ValueNormalizer.Digits(combined.Groups["value"].Value), "seller.kpp", value => document.Seller.Kpp = value);
                Assign(Requisite(text, @"(?:БИК|BIK)"), "seller.bik", value => document.Seller.Bik = value);
                Assign(Requisite(text, @"(?:р\s*[/\\.]\s*с\.?|расчетный\s+счет|расч\.?\s*счет|account)"), "seller.account", value => document.Seller.Account = value);
                Assign(Requisite(text, @"(?:к\s*[/\\.]\s*с\.?|корр(?:еспондентский|\.)?\s*счет|corrAccount)"), "seller.corrAccount", value => document.Seller.CorrAccount = value);
                if (Is(text, @"сч\.?\s*№"))
                {
                    var account = Requisite(text, @"сч\.?\s*№");
                    if (account?.Length == 20)
                        Assign(account, account.StartsWith("301") ? "seller.corrAccount" : "seller.account", value =>
                        {
                            if (value.StartsWith("301"))
                                document.Seller.CorrAccount = value;
                            else
                                document.Seller.Account = value;
                        });
                }
                var bank = Match(text, @"(?:банк(?:\s+получателя)?|bank)\s*:\s*(?<value>.*?)(?=\s*(?:БИК|BIK|к/с|р/с)\b|$)");
                if (bank.Success)
                    Assign(bank.Groups["value"].Value.Trim(), "seller.bank", value => document.Seller.Bank = value);
                else if (Is(text, @"банк\s+получателя") && index > 0)
                {
                    var preceding = Regex.Split(lines[index - 1].Text, @"\s+(?:БИК|BIK|Сч\.)", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1))[0];
                    if (!Is(preceding, @"ИНН|КПП|счет\s+на"))
                        Assign(preceding.Trim(), "seller.bank", value => document.Seller.Bank = value);
                }
            }

            var total = Match(text, @"^\s*(?<label>всего\s+к\s+оплате|итого\s+к\s+оплате|к\s+оплате|итого|всего|amount\s+due|total)\s*:?\s*(?<amount>" + AmountPattern + @")\s*(?:руб\.?|₽|RUB)?\s*$");
            if (total.Success)
            {
                var priority = Is(total.Groups["label"].Value, @"оплате|amount\s+due") ? 2 : 1;
                if (priority >= duePriority)
                {
                    document.AmountDue = ValueNormalizer.Money(total.Groups["amount"].Value);
                    duePriority = priority;
                    Record("amountDue");
                }
            }
            var withoutVat = Match(text, @"^\s*(?:(?:сумма|итого|всего)\s+)?без\s+НДС\s*:?\s*(?<amount>" + AmountPattern + @")\s*(?:руб\.?|₽|RUB)?\s*$");
            if (withoutVat.Success)
            {
                document.AmountWithoutVat = ValueNormalizer.Money(withoutVat.Groups["amount"].Value);
                Record("amountWithoutVat");
            }
            var vat = Match(text, @"^\s*(?:в\s*т\.?\s*ч\.?\s*|в\s+том\s+числе\s+|сумма\s+)?НДС(?:\s*\(?\d+(?:[.,]\d+)?\s*%\)?)?\s*:?\s*(?<amount>" + AmountPattern + @")\s*(?:руб\.?|₽|RUB)?\s*$");
            if (vat.Success && !Is(text, @"без\s+НДС"))
            {
                document.VatTotal = ValueNormalizer.Money(vat.Groups["amount"].Value);
                Record("vatTotal");
            }

            void Record(string field) => result.FieldConfidence[field] = line.Confidence;
            void Assign(string? value, string field, Action<string> setter)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    setter(value);
                    Record(field);
                }
            }
        }

        tableParser.Extract(recognized, result);
        return result;
    }

    private static string? Requisite(string text, string label)
    {
        var match = Match(text, @"(?<![а-яa-z])" + label + @"\s*[:№]?\s*(?<value>\d(?:[\d \u00a0\u202f|\-]*\d)?)");
        return match.Success ? ValueNormalizer.Digits(match.Groups["value"].Value) : null;
    }

    internal static Match Match(string text, string pattern) => Regex.Match(text, pattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    internal static bool Is(string text, string pattern) => Match(text, pattern).Success;
}
