namespace Pos.Application.Stock;

public sealed record ReceiveGoodsRequest(
    int? SupplierId,
    string? SupplierRef,
    string BusinessDate,
    IReadOnlyList<ReceiveGoodsLine> Lines,
    int? UserId = null);

public sealed record ReceiveGoodsLine(int ProductId, long QtyMilli, long UnitCostCents);

public sealed record ReceiveGoodsResult(int ReceiptId, string GrnNumber, long TotalCents);

public interface IReceiveGoods
{
    /// <summary>
    /// Records a supplier delivery in a single transaction:
    /// GRN header + lines, PURCHASE stock movements, weighted average cost update.
    /// Any failure rolls back the whole receipt.
    /// </summary>
    Task<ReceiveGoodsResult> ExecuteAsync(ReceiveGoodsRequest request, CancellationToken ct = default);
}

public sealed class ReceiveGoodsValidationException : Exception
{
    public ReceiveGoodsValidationException(string message) : base(message) { }
}
