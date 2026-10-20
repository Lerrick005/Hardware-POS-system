namespace Pos.Application.Sales;

/// <summary>
/// One line in a sale request. The caller supplies only the primitives —
/// product id, quantity, price override if any, and line discount.
/// The service computes base quantity, unit cost, tax, and line total,
/// so a buggy UI cannot accidentally persist wrong arithmetic.
/// </summary>
public sealed record SaleLineRequest(
    int ProductId,
    int UnitId,
    long QtyMilli,
    /// <summary>Null means: use the price for the sale's tier. Non-null: override (with audit).</summary>
    long? UnitPriceCentsOverride,
    /// <summary>Line discount in cents. Zero for none.</summary>
    long DiscountCents,
    string? Notes);

/// <summary>
/// One payment attempt. Amount is positive. Sum of all payment amounts must
/// be >= the sale total. If sum > total, the difference is returned as change.
/// Credit payments require the sale to carry a CustomerId.
/// </summary>
public sealed record PaymentRequest(
    string Method,
    long AmountCents,
    string? Reference);

public sealed record SaleRequest(
    int? CustomerId,
    /// <summary>Null means: customer's default tier, else Retail. Non-null: force this tier.</summary>
    int? PriceTierId,
    string BusinessDate,
    IReadOnlyList<SaleLineRequest> Lines,
    IReadOnlyList<PaymentRequest> Payments,
    int UserId,
    int? CashSessionId);

/// <summary>
/// One line on a completed sale, ready for the receipt. Includes both the
/// requested selling unit (e.g. "Box") and the base-unit quantity that was
/// deducted from stock, so the receipt can show
/// "2 Box (= 240 pcs) @ 500/Box = 1,000" or "0.75 KG (= 18.75 pcs) @ 400/KG = 300".
/// </summary>
public sealed record SaleResultLine(
    string ProductName,
    string UnitName,
    string UnitSymbol,
    long QtyMilli,
    long BaseQtyMilli,
    long FactorMilli,
    long UnitPriceCents,
    long LineTotalCents);

public sealed record SaleResultPayment(
    string Method,
    long AmountCents,
    string? Reference);

public sealed record SaleResult(
    int SaleId,
    string ReceiptNumber,
    long SubtotalCents,
    long DiscountCents,
    long TaxCents,
    long TotalCents,
    long PaidCents,
    long ChangeCents,
    IReadOnlyList<SaleResultLine> Lines,
    IReadOnlyList<SaleResultPayment> Payments);

public sealed class SaleValidationException : Exception
{
    public SaleValidationException(string message) : base(message) { }
}

public interface ISellService
{
    /// <summary>
    /// Completes a sale atomically. In one transaction:
    ///   - allocates a gap-free receipt number
    ///   - inserts the sale, its lines, and its payments
    ///   - inserts SALE stock movements for each line
    ///   - inserts a CHARGE on the customer's ledger if any payment is CREDIT
    ///   - writes audit entries for price overrides (if any) and the sale
    ///
    /// Any failure rolls back the whole sale. Partial sales never persist.
    /// </summary>
    Task<SaleResult> CompleteSaleAsync(SaleRequest request, CancellationToken ct = default);
}


