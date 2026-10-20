using Dapper;
using Pos.Application.Stock;
using Pos.Infrastructure.Data;

namespace Pos.Infrastructure.Stock;

public sealed class ReceiveGoodsQuery : IReceiveGoodsQuery
{
    private readonly DbConnectionFactory _factory;

    public ReceiveGoodsQuery(DbConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<GoodsReceiptSummary>> ListRecentAsync(int limit = 100, CancellationToken ct = default)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be positive.");
        }

        using var connection = _factory.CreateOpen();
        var rows = await connection.QueryAsync<GoodsReceiptSummary>(new CommandDefinition(@"
            SELECT g.id           AS Id,
                   g.grn_number   AS GrnNumber,
                   g.supplier_id  AS SupplierId,
                   s.name         AS SupplierName,
                   g.supplier_ref AS SupplierRef,
                   g.total_cents  AS TotalCents,
                   g.paid_cents   AS PaidCents,
                   g.business_date AS BusinessDate,
                   g.created_at   AS CreatedAt,
                   (SELECT COUNT(1) FROM goods_receipt_items i WHERE i.receipt_id = g.id) AS LineCount
              FROM goods_receipts g
              LEFT JOIN suppliers s ON s.id = g.supplier_id
             ORDER BY g.id DESC
             LIMIT $limit",
            new { limit }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<GoodsReceiptDetail?> GetAsync(int receiptId, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();

        GoodsReceiptSummary? header = await connection.QueryFirstOrDefaultAsync<GoodsReceiptSummary>(new CommandDefinition(@"
            SELECT g.id           AS Id,
                   g.grn_number   AS GrnNumber,
                   g.supplier_id  AS SupplierId,
                   s.name         AS SupplierName,
                   g.supplier_ref AS SupplierRef,
                   g.total_cents  AS TotalCents,
                   g.paid_cents   AS PaidCents,
                   g.business_date AS BusinessDate,
                   g.created_at   AS CreatedAt,
                   0              AS LineCount
              FROM goods_receipts g
              LEFT JOIN suppliers s ON s.id = g.supplier_id
             WHERE g.id = $id",
            new { id = receiptId }, cancellationToken: ct));

        if (header is null)
        {
            return null;
        }

        var lines = await connection.QueryAsync<GoodsReceiptDetailLine>(new CommandDefinition(@"
            SELECT i.product_id      AS ProductId,
                   p.name            AS ProductName,
                   p.sku             AS ProductSku,
                   i.qty_milli       AS QtyMilli,
                   i.unit_cost_cents AS UnitCostCents
              FROM goods_receipt_items i
              JOIN products p ON p.id = i.product_id
             WHERE i.receipt_id = $id
             ORDER BY i.id",
            new { id = receiptId }, cancellationToken: ct));

        return new GoodsReceiptDetail
        {
            Id = header.Id,
            GrnNumber = header.GrnNumber,
            SupplierId = header.SupplierId,
            SupplierName = header.SupplierName,
            SupplierRef = header.SupplierRef,
            TotalCents = header.TotalCents,
            PaidCents = header.PaidCents,
            BusinessDate = header.BusinessDate,
            CreatedAt = header.CreatedAt,
            Lines = lines.AsList(),
        };
    }
}
