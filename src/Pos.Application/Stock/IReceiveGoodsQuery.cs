namespace Pos.Application.Stock;

public sealed record GoodsReceiptSummary(
    int Id,
    string GrnNumber,
    int? SupplierId,
    string? SupplierName,
    string? SupplierRef,
    long TotalCents,
    long PaidCents,
    string BusinessDate,
    string CreatedAt,
    int LineCount);

public sealed record GoodsReceiptDetail(
    int Id,
    string GrnNumber,
    int? SupplierId,
    string? SupplierName,
    string? SupplierRef,
    long TotalCents,
    long PaidCents,
    string BusinessDate,
    string CreatedAt,
    IReadOnlyList<GoodsReceiptDetailLine> Lines);

public sealed record GoodsReceiptDetailLine(
    int ProductId,
    string ProductName,
    string? ProductSku,
    long QtyMilli,
    long UnitCostCents);

public interface IReceiveGoodsQuery
{
    /// <summary>Recent receipts, newest first. Default 100 rows.</summary>
    Task<IReadOnlyList<GoodsReceiptSummary>> ListRecentAsync(int limit = 100, CancellationToken ct = default);

    /// <summary>One receipt with all lines, or null if not found.</summary>
    Task<GoodsReceiptDetail?> GetAsync(int receiptId, CancellationToken ct = default);
}
