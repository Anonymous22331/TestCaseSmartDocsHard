using TestCaseSmartDocsHard.Models;
using TestCaseSmartDocsHard.Services;

namespace TestCaseSmartDocsHard.Validation;

public sealed class InvoiceValidator
{
    public IReadOnlyList<InvoiceIssue> Validate(InvoiceDocument document)
    {
        var issues = new List<InvoiceIssue>();
        if (string.IsNullOrWhiteSpace(document.Number))
            issues.Add(new("number_missing", "number", "The invoice number was not found."));
        if (document.Date is null || ValueNormalizer.Date(document.Date) != document.Date)
            issues.Add(new("date_unparsed", "date", "The invoice date could not be parsed."));
        if (document.Seller.Inn is null)
            issues.Add(new("seller_inn_missing", "seller.inn", "The seller INN was not found."));
        else if (!RequisiteChecks.Inn(document.Seller.Inn))
            issues.Add(new("inn_checksum", "seller.inn", "The seller INN checksum is invalid."));
        if (document.Buyer.Inn is not null && !RequisiteChecks.Inn(document.Buyer.Inn))
            issues.Add(new("inn_checksum", "buyer.inn", "The buyer INN checksum is invalid."));
        if (document.Seller.Kpp is not null && !RequisiteChecks.HasDigits(document.Seller.Kpp, 9))
            issues.Add(new("kpp_format", "seller.kpp", "KPP must contain 9 digits."));
        if (document.Seller.Bik is not null && !RequisiteChecks.HasDigits(document.Seller.Bik, 9))
            issues.Add(new("bik_format", "seller.bik", "BIK must contain 9 digits."));
        CheckAccount(document.Seller.Account, "seller.account", false);
        CheckAccount(document.Seller.CorrAccount, "seller.corrAccount", true);

        if (document.AmountDue is null)
            issues.Add(new("amount_due_missing", "amountDue", "The amount due was not found."));
        if (document.Items.Count == 0)
            issues.Add(new("items_missing", "items", "No invoice items were recognized."));

        for (var index = 0; index < document.Items.Count; index++)
        {
            var item = document.Items[index];
            var field = $"items[{index}]";
            if (string.IsNullOrWhiteSpace(item.Name) || item.Qty is null || item.Price is null || item.Amount is null)
            {
                issues.Add(new("item_incomplete", field, "An invoice item has missing fields."));
                continue;
            }
            try
            {
                if (Math.Abs(item.Qty.Value * item.Price.Value - item.Amount.Value) > 0.01m)
                    issues.Add(new("line_amount_mismatch", field, "Quantity multiplied by price differs from the line amount."));
            }
            catch (OverflowException)
            {
                issues.Add(new("line_amount_mismatch", field, "The item values exceed the supported arithmetic range."));
            }
        }

        if (document.Items.Count > 0 && document.Items.All(item => item.Amount is not null) && document.AmountDue is not null)
        {
            try
            {
                if (Math.Abs(document.Items.Sum(item => item.Amount!.Value) - document.AmountDue.Value) > 0.01m)
                    issues.Add(new("total_mismatch", "amountDue", "The sum of item amounts differs from the amount due."));
            }
            catch (OverflowException)
            {
                issues.Add(new("total_mismatch", "amountDue", "The totals exceed the supported arithmetic range."));
            }
        }
        return issues;

        void CheckAccount(string? account, string field, bool correspondent)
        {
            if (account is null)
                return;
            if (!RequisiteChecks.HasDigits(account, 20))
                issues.Add(new("account_checksum", field, "A bank account must contain 20 digits."));
            else if (document.Seller.Bik is null)
                issues.Add(new("bik_missing", "seller.bik", "BIK is required to validate the extracted bank account."));
            else if (!RequisiteChecks.Account(account, document.Seller.Bik, correspondent))
                issues.Add(new("account_checksum", field, "The bank account checksum is invalid."));
        }
    }
}
