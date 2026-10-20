using Dapper;
using Pos.Application.Reports;
using Pos.Infrastructure.Data;

namespace Pos.Infrastructure.Reports;

public sealed class DashboardQuery : IDashboardQuery
{
    private readonly DbConnectionFactory _factory;

    public DashboardQuery(DbConnectionFactory factory) => _factory = factory;

    public async Task<DashboardSnapshot> GetSnapshotAsync(string todayBusinessDate, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        
        // Using a single query with subselects for performance.
        return await connection.QuerySingleAsync<DashboardSnapshot>(new CommandDefinition(@"
            SELECT
                -- Today's sales
                (SELECT COALESCE(SUM(total_cents), 0) FROM sales 
                  WHERE business_date = $today AND status = 'COMPLETED') AS TodaySalesCents,
                (SELECT COUNT(1) FROM sales 
                  WHERE business_date = $today AND status = 'COMPLETED') AS TodayTransactionCount,
                (SELECT COALESCE(SUM(p.amount_cents), 0) FROM payments p JOIN sales s ON p.sale_id = s.id
                  WHERE s.business_date = $today AND s.status = 'COMPLETED' AND p.method = 'CASH') AS TodayCashCents,
                (SELECT COALESCE(SUM(p.amount_cents), 0) FROM payments p JOIN sales s ON p.sale_id = s.id
                  WHERE s.business_date = $today AND s.status = 'COMPLETED' AND p.method = 'MPESA') AS TodayMpesaCents,
                (SELECT COALESCE(SUM(p.amount_cents), 0) FROM payments p JOIN sales s ON p.sale_id = s.id
                  WHERE s.business_date = $today AND s.status = 'COMPLETED' AND p.method = 'CREDIT') AS TodayCreditCents,

                -- Stock alerts
                (SELECT COUNT(1) FROM products WHERE is_active = 1 AND track_stock = 1 AND stock_milli > 0 AND stock_milli <= reorder_level_milli) AS LowStockCount,
                (SELECT COUNT(1) FROM products WHERE is_active = 1 AND track_stock = 1 AND stock_milli <= 0) AS OutOfStockCount,

                -- Debtors
                (SELECT COALESCE(SUM(balance_cents), 0) FROM v_customer_balance WHERE balance_cents > 0) AS DebtorsTotalCents,
                (SELECT COUNT(1) FROM v_customer_balance WHERE balance_cents > 0) AS DebtorCount,

                -- Setup checks
                (SELECT COUNT(1) > 0 FROM products WHERE is_active = 1) AS HasAnyProducts,
                (SELECT COUNT(1) > 0 FROM sales) AS HasAnySales,
                (SELECT COUNT(1) > 0 FROM customers WHERE is_active = 1) AS HasAnyCustomers
        ", new { today = todayBusinessDate }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<LowStockItem>> GetTopLowStockAsync(int limit = 5, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        
        // Order by how far below reorder level they are (most urgent first)
        var rows = await connection.QueryAsync<LowStockItem>(new CommandDefinition(@"
            SELECT name AS ProductName,
                   stock_milli AS StockMilli,
                   reorder_level_milli AS ReorderLevelMilli
              FROM products
             WHERE is_active = 1 
               AND track_stock = 1 
               AND stock_milli <= reorder_level_milli
             ORDER BY (reorder_level_milli - stock_milli) DESC
             LIMIT $limit
        ", new { limit }, cancellationToken: ct));
        
        return rows.AsList();
    }
}
