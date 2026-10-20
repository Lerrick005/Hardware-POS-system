namespace Pos.Domain.Customers;

public enum LedgerEntryType
{
    Opening,
    Charge,
    Payment,
    Adjustment,
}

public sealed class CustomerLedgerEntry
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public LedgerEntryType EntryType { get; set; }

    /// <summary>
    /// Signed amount in cents. Positive increases what the customer owes
    /// (charge, opening balance). Negative reduces it (payment, credit note).
    /// </summary>
    public long AmountCents { get; set; }

    public int? SaleId { get; set; }
    public int? ReturnId { get; set; }
    public int? CashSessionId { get; set; }

    /// <summary>Payment method when this entry is a payment (Cash, M-Pesa, Bank).</summary>
    public string? Method { get; set; }

    /// <summary>Free-form reference (M-Pesa code, receipt #, cheque #).</summary>
    public string? Reference { get; set; }

    public string? Notes { get; set; }
    public int? UserId { get; set; }
    public int? ImportBatchId { get; set; }
    public string BusinessDate { get; set; } = "";
    public string? CreatedAt { get; set; }

    public Money Amount => new(AmountCents);

    public string DisplayType => EntryType switch
    {
        LedgerEntryType.Opening => "Old balance",
        LedgerEntryType.Charge => "Sale on credit",
        LedgerEntryType.Payment => "Payment received",
        LedgerEntryType.Adjustment => "Adjustment",
        _ => EntryType.ToString(),
    };
}
