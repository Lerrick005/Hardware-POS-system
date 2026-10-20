namespace Pos.Application.Stock;

/// <summary>
/// Reasons a shop owner would adjust stock outside of a delivery or a sale.
/// </summary>
public static class AdjustmentReasons
{
    public const string Damaged = "DAMAGED";
    public const string Lost = "LOST";
    public const string Found = "FOUND";
    public const string Counted = "COUNTED";

    public static readonly string[] All = { Damaged, Lost, Found, Counted };
}

public sealed record StockAdjustmentRequest(
    int ProductId,
    string ReasonCategory,
    long DeltaMilli,
    string Notes,
    int UserId,
    string BusinessDate);

public sealed record StockAdjustmentResult(
    int MovementId,
    long PreviousStockMilli,
    long NewStockMilli,
    string MovementType);

public sealed class StockAdjustmentException : Exception
{
    public StockAdjustmentException(string message) : base(message) { }
}

public interface IStockAdjustmentService
{
    /// <summary>
    /// Adjusts stock outside of normal delivery/sale flows. Reasons:
    ///   DAMAGED  — delta must be negative (product is gone)
    ///   LOST     — delta must be negative (product is gone)
    ///   FOUND    — delta must be positive (product appeared)
    ///   COUNTED  — delta = (counted − current); sign depends on variance
    ///
    /// Writes one stock_movements row + one audit_log row in one transaction.
    /// Refuses if the adjustment would make stock negative.
    /// </summary>
    Task<StockAdjustmentResult> AdjustAsync(
        StockAdjustmentRequest request,
        CancellationToken ct = default);
}
