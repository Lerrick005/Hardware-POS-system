namespace Pos.Application.Stock;

/// <summary>
/// Dapper-materialized list row. Must be a class with settable properties
/// (see ADR-0011) — positional records cannot be materialized by Dapper.
/// </summary>
public sealed class GoodsReceiptSummary
{
    public int Id { get; set; }
    public string GrnNumber { get; set; } = "";
    public int? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string? SupplierRef { get; set; }
    public long TotalCents { get; set; }
    public long PaidCents { get; set; }
    public string BusinessDate { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public int LineCount { get; set; }
}

/// <summary>Dapper-materialized line row. See ADR-0011.</summary>
public sealed class GoodsReceiptDetailLine
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string? ProductSku { get; set; }
    public long QtyMilli { get; set; }
    public long UnitCostCents { get; set; }
}

/// <summary>
/// Detail container. Not Dapper-materialized — it is built by the query
/// service from two queries. Can stay a plain class.
/// </summary>
public sealed class GoodsReceiptDetail
{
    public int Id { get; init; }
    public string GrnNumber { get; init; } = "";
    public int? SupplierId { get; init; }
    public string? SupplierName { get; init; }
    public string? SupplierRef { get; init; }
    public long TotalCents { get; init; }
    public long PaidCents { get; init; }
    public string BusinessDate { get; init; } = "";
    public string CreatedAt { get; init; } = "";
    public IReadOnlyList<GoodsReceiptDetailLine> Lines { get; init; } = Array.Empty<GoodsReceiptDetailLine>();
}

public interface IReceiveGoodsQuery
{
    /// <summary>Recent receipts, newest first. Default 100 rows.</summary>
    Task<IReadOnlyList<GoodsReceiptSummary>> ListRecentAsync(int limit = 100, CancellationToken ct = default);

    /// <summary>One receipt with all lines, or null if not found.</summary>
    Task<GoodsReceiptDetail?> GetAsync(int receiptId, CancellationToken ct = default);
}
