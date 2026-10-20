namespace Pos.Domain.Sales;

public enum SaleStatus
{
    Held,
    Completed,
    Voided,
}

/// <summary>
/// A single sale transaction (header). Line items live in <see cref="SaleLine"/>,
/// payments in <see cref="Payment"/>. Domain entity — never persisted via Dapper
/// directly; the SaleService assembles it from the request.
/// </summary>
public sealed class Sale
{
    public int Id { get; set; }
    public string Uuid { get; set; } = "";
    public string? ReceiptNumber { get; set; }
    public SaleStatus Status { get; set; } = SaleStatus.Completed;

    public int? CustomerId { get; set; }
    public int UserId { get; set; }
    public int? CashSessionId { get; set; }
    public int? PriceTierId { get; set; }

    /// <summary>Snapshot of the prices_include_tax setting at time of sale.</summary>
    public bool PricesIncludeTax { get; set; } = true;

    public long SubtotalCents { get; set; }
    public long DiscountCents { get; set; }
    public long TaxCents { get; set; }
    public long TotalCents { get; set; }

    public string? FiscalRef { get; set; }
    public string? VoidReason { get; set; }
    public int? VoidedBy { get; set; }
    public string? VoidedAt { get; set; }
    public string BusinessDate { get; set; } = "";
    public string? CreatedAt { get; set; }

    public List<SaleLine> Lines { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();

    public Money Subtotal => new(SubtotalCents);
    public Money Discount => new(DiscountCents);
    public Money Tax => new(TaxCents);
    public Money Total => new(TotalCents);
}
