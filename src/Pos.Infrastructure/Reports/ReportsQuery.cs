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

    public async Task<ProfitSummary> GetProfitSummaryAsync(
        string dateFrom, string dateTo, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();

        var summary = await connection.QuerySingleAsync<ProfitSummary>(new CommandDefinition(@"
            SELECT
                COALESCE((SELECT SUM(total_cents) FROM sales
                    WHERE business_date >= $from AND business_date <= $to
                      AND status = 'COMPLETED'), 0) AS RevenueCents,
                COALESCE((SELECT SUM(si.unit_cost_cents * si.base_qty_milli / 1000)
                    FROM sale_items si
                    JOIN sales s ON s.id = si.sale_id
                    WHERE s.business_date >= $from AND s.business_date <= $to
                      AND s.status = 'COMPLETED'), 0) AS CostOfGoodsSoldCents,
                COALESCE((SELECT SUM(amount_cents) FROM expenses
                    WHERE business_date >= $from AND business_date <= $to), 0) AS ExpensesCents,
                (SELECT COUNT(1) FROM sales
                    WHERE business_date >= $from AND business_date <= $to
                      AND status = 'COMPLETED') AS TransactionCount,
                COALESCE((SELECT COUNT(1) FROM sale_items si
                    JOIN sales s ON s.id = si.sale_id
                    WHERE s.business_date >= $from AND s.business_date <= $to
                      AND s.status = 'COMPLETED'
                      AND si.unit_cost_cents = 0), 0) AS UncategorizedCostLineCount",
            new { from = dateFrom, to = dateTo }, cancellationToken: ct));

        summary.GrossProfitCents = summary.RevenueCents - summary.CostOfGoodsSoldCents;
        summary.NetProfitCents = summary.GrossProfitCents - summary.ExpensesCents;
        return summary;
    }

    public async Task<IReadOnlyList<ProductProfitRow>> GetProductProfitAsync(
        string dateFrom, string dateTo, int limit = 50, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        var rows = await connection.QueryAsync<ProductProfitRow>(new CommandDefinition(@"
            SELECT si.product_name AS ProductName,
                   SUM(si.base_qty_milli) AS QtySoldMilli,
                   SUM(si.line_total_cents) AS RevenueCents,
                   SUM(si.unit_cost_cents * si.base_qty_milli / 1000) AS CostCents,
                   SUM(si.line_total_cents) - SUM(si.unit_cost_cents * si.base_qty_milli / 1000) AS ProfitCents
              FROM sale_items si
              JOIN sales s ON s.id = si.sale_id
             WHERE s.business_date >= $from AND s.business_date <= $to
               AND s.status = 'COMPLETED'
             GROUP BY si.product_name
             ORDER BY ProfitCents DESC
             LIMIT $limit",
            new { from = dateFrom, to = dateTo, limit }, cancellationToken: ct));

        var list = rows.AsList();

        // Compute margin percentage in C# — SQLite's integer division would truncate
        // small percentages to 0. decimal math is correct math here.
        foreach (var row in list)
        {
            row.MarginPercent = row.RevenueCents == 0
                ? 0m
                : Math.Round((decimal)row.ProfitCents / row.RevenueCents * 100m, 1);
        }

        return list;
    }

    public async Task<IReadOnlyList<DeadStockRow>> GetDeadStockAsync(
        int daysThreshold = 90, CancellationToken ct = default)
    {
        if (daysThreshold < 1) throw new ArgumentOutOfRangeException(nameof(daysThreshold));

        using var connection = _factory.CreateOpen();
        var rows = await connection.QueryAsync<DeadStockRow>(new CommandDefinition(@"
            SELECT p.name AS ProductName,
                   p.stock_milli AS StockMilli,
                   p.avg_cost_cents AS AvgCostCents,
                   CAST(p.stock_milli * p.avg_cost_cents / 1000 AS INTEGER) AS TotalCostCents,
                   (SELECT MAX(s.business_date) FROM sale_items si
                      JOIN sales s ON s.id = si.sale_id
                     WHERE si.product_id = p.id AND s.status = 'COMPLETED') AS LastSoldDate
              FROM products p
             WHERE p.is_active = 1
               AND p.track_stock = 1
               AND p.stock_milli > 0
               AND (
                    NOT EXISTS (SELECT 1 FROM sale_items si
                                JOIN sales s ON s.id = si.sale_id
                                WHERE si.product_id = p.id AND s.status = 'COMPLETED')
                    OR
                    (SELECT MAX(s.business_date) FROM sale_items si
                       JOIN sales s ON s.id = si.sale_id
                      WHERE si.product_id = p.id AND s.status = 'COMPLETED')
                       < date('now', '-' || $days || ' days')
               )
             ORDER BY TotalCostCents DESC",
            new { days = daysThreshold }, cancellationToken: ct));

        var list = rows.AsList();

        // Compute days since last sale in C#
        DateTime today = DateTime.Today;
        foreach (var row in list)
        {
            if (string.IsNullOrEmpty(row.LastSoldDate))
            {
                row.DaysSinceLastSale = -1;
            }
            else if (DateTime.TryParse(row.LastSoldDate, out DateTime lastSold))
            {
                row.DaysSinceLastSale = (int)(today - lastSold).TotalDays;
            }
            else
            {
                row.DaysSinceLastSale = -1;
            }
        }

        return list;
    }
}
