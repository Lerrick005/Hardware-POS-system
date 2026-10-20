using Dapper;
using Pos.Application.Reports;
using Pos.Infrastructure.Data;

namespace Pos.Infrastructure.Reports;

public sealed class ReportsQuery : IReportsQuery
{
    private readonly DbConnectionFactory _factory;

    public ReportsQuery(DbConnectionFactory factory) => _factory = factory;

    public async Task<SalesSummary> GetSalesSummaryAsync(string dateFrom, string dateTo, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        return await connection.QuerySingleAsync<SalesSummary>(new CommandDefinition(@"
            SELECT
                (SELECT COALESCE(SUM(total_cents), 0) FROM sales
                  WHERE business_date >= $from AND business_date <= $to AND status = 'COMPLETED') AS TotalCents,
                (SELECT COUNT(1) FROM sales
                  WHERE business_date >= $from AND business_date <= $to AND status = 'COMPLETED') AS TransactionCount,
                (SELECT COALESCE(SUM(p.amount_cents), 0) FROM payments p
                  JOIN sales s ON p.sale_id = s.id
                  WHERE s.business_date >= $from AND s.business_date <= $to AND p.method = 'CASH') AS CashCents,
                (SELECT COALESCE(SUM(p.amount_cents), 0) FROM payments p
                  JOIN sales s ON p.sale_id = s.id
                  WHERE s.business_date >= $from AND s.business_date <= $to AND p.method = 'MPESA') AS MpesaCents,
                (SELECT COALESCE(SUM(p.amount_cents), 0) FROM payments p
                  JOIN sales s ON p.sale_id = s.id
                  WHERE s.business_date >= $from AND s.business_date <= $to AND p.method = 'CREDIT') AS CreditCents",
            new { from = dateFrom, to = dateTo }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ProductSalesRow>> GetTopProductsAsync(
        string dateFrom, string dateTo, int limit = 20, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        var rows = await connection.QueryAsync<ProductSalesRow>(new CommandDefinition(@"
            SELECT si.product_name AS ProductName,
                   SUM(si.base_qty_milli) AS QtySoldMilli,
                   SUM(si.line_total_cents) AS RevenueCents
              FROM sale_items si
              JOIN sales s ON s.id = si.sale_id
             WHERE s.business_date >= $from AND s.business_date <= $to AND s.status = 'COMPLETED'
             GROUP BY si.product_name
             ORDER BY RevenueCents DESC
             LIMIT $limit",
            new { from = dateFrom, to = dateTo, limit }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<StockValuationRow>> GetStockValuationAsync(CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        var rows = await connection.QueryAsync<StockValuationRow>(new CommandDefinition(@"
            SELECT COALESCE(c.name, '(Uncategorised)') AS CategoryName,
                   COUNT(p.id) AS ProductCount,
                   CAST(SUM(CAST(p.stock_milli AS REAL) * p.avg_cost_cents / 1000) AS INTEGER) AS TotalCostCents,
                   CAST(SUM(CAST(p.stock_milli AS REAL) * COALESCE(pp.price_cents, 0) / 1000) AS INTEGER) AS TotalRetailCents
              FROM products p
              LEFT JOIN categories c ON c.id = p.category_id
              LEFT JOIN price_tiers pt ON pt.is_default = 1
              LEFT JOIN product_prices pp ON pp.product_id = p.id AND pp.tier_id = pt.id
             WHERE p.is_active = 1 AND p.stock_milli > 0
             GROUP BY c.name
             ORDER BY TotalCostCents DESC",
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<DebtorRow>> GetDebtorsAsync(CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        var rows = await connection.QueryAsync<DebtorRow>(new CommandDefinition(@"
            SELECT c.name AS CustomerName,
                   COALESCE(c.phone, '') AS Phone,
                   SUM(l.amount_cents) AS BalanceCents,
                   MAX(l.business_date) AS LastActivityDate
              FROM customers c
              JOIN customer_ledger l ON l.customer_id = c.id
             WHERE c.is_active = 1
             GROUP BY c.id
             HAVING SUM(l.amount_cents) > 0
             ORDER BY BalanceCents DESC",
            cancellationToken: ct));
        return rows.AsList();
    }
}
