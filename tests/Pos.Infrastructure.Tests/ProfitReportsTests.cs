using Dapper;
using Microsoft.Data.Sqlite;
using Pos.Infrastructure.Expenses;
using Pos.Infrastructure.Reports;
using Pos.Infrastructure.Sales;

namespace Pos.Infrastructure.Tests;

public sealed class ProfitReportsTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly ReportsQuery _reports;
    private readonly ExpensesService _expenses;
    private readonly SellService _sell;
    private readonly int _userId;
    private readonly int _productA;
    private readonly int _productB;

    public ProfitReportsTests()
    {
        _reports = new ReportsQuery(_db.Factory);
        _expenses = new ExpensesService(_db.Factory);
        _sell = new SellService(_db.Factory);

        _userId = InsertUser();
        _productA = InsertProduct("Cement 50kg", 55000, 65000, 20000);
        _productB = InsertProduct("Nail 3in", 300, 500, 50000);
    }

    public void Dispose() => _db.Dispose();

    private int InsertUser()
    {
        using var c = _db.Factory.CreateOpen();
        return (int)c.ExecuteScalar<long>(
            "INSERT INTO users(name, role, pin_hash, is_active) VALUES ('Test', 'ADMIN', 'PIN_NOT_SET', 1) RETURNING id");
    }

    private int InsertProduct(string name, long costCents, long priceCents, long stockMilli)
    {
        using var c = _db.Factory.CreateOpen();
        int id = (int)c.ExecuteScalar<long>(@"
            INSERT INTO products(uuid, name, base_unit_id, avg_cost_cents, stock_milli, is_active)
            VALUES ($uuid, $name, 1, $costCents, $stockMilli, 1) RETURNING id",
            new { uuid = Guid.NewGuid().ToString("N"), name, costCents, stockMilli });

        c.Execute(@"
            INSERT INTO product_prices(product_id, tier_id, price_cents) VALUES ($id, 1, $priceCents)",
            new { id, priceCents });
        return id;
    }

    private async Task<int> SellAndPayAsync(int productId, long qtyMilli)
    {
        var request = new Application.Sales.SaleRequest(
            CustomerId: null,
            PriceTierId: 1,
            BusinessDate: "2026-10-20",
            Lines: new[] { new Application.Sales.SaleLineRequest(productId, 1, qtyMilli, null, 0, null) },
            Payments: Array.Empty<Application.Sales.PaymentRequest>(),
            UserId: _userId,
            CashSessionId: null);

        // Use direct SQL for sales — simpler for tests, we're testing reports not sell
        using var c = _db.Factory.CreateOpen();
        long saleId = c.ExecuteScalar<long>(@"
            INSERT INTO sales(uuid, receipt_number, status, user_id, price_tier_id, prices_include_tax,
                              subtotal_cents, total_cents, business_date)
            VALUES ($uuid, 'RCT-TEST-' || $rand, 'COMPLETED', $userId, 1, 1, $total, $total, '2026-10-20')
            RETURNING id",
            new { uuid = Guid.NewGuid().ToString("N"), rand = Guid.NewGuid().ToString("N")[..8],
                  userId = _userId, total = 0L });

        long unitPrice = c.ExecuteScalar<long>(
            "SELECT price_cents FROM product_prices WHERE product_id = $id AND tier_id = 1", new { id = productId });
        long lineTotal = unitPrice * qtyMilli / 1000;

        c.Execute(@"
            INSERT INTO sale_items(sale_id, product_id, product_name, unit_id, qty_milli, base_qty_milli,
                                   unit_price_cents, unit_cost_cents, discount_cents, tax_rate_bp, line_total_cents)
            SELECT $saleId, p.id, p.name, 1, $qtyMilli, $qtyMilli, $unitPrice, p.avg_cost_cents, 0, 0, $lineTotal
              FROM products p WHERE p.id = $productId",
            new { saleId, productId, qtyMilli, unitPrice, lineTotal });

        c.Execute("UPDATE sales SET total_cents = $total, subtotal_cents = $total WHERE id = $saleId",
            new { total = lineTotal, saleId });

        return (int)saleId;
    }

    // ===== ProfitSummary =====

    [Fact]
    public async Task ProfitSummary_NoData_ReturnsZeros()
    {
        var summary = await _reports.GetProfitSummaryAsync("2026-10-20", "2026-10-20");

        Assert.Equal(0, summary.RevenueCents);
        Assert.Equal(0, summary.CostOfGoodsSoldCents);
        Assert.Equal(0, summary.ExpensesCents);
        Assert.Equal(0, summary.GrossProfitCents);
        Assert.Equal(0, summary.NetProfitCents);
        Assert.Equal(0, summary.TransactionCount);
    }

    [Fact]
    public async Task ProfitSummary_CalculatesGrossCorrectly()
    {
        // Sell 10 bags of cement at 650 = 6,500 revenue, cost 550*10 = 5,500, gross 1,000
        await SellAndPayAsync(_productA, 10_000);

        var summary = await _reports.GetProfitSummaryAsync("2026-10-20", "2026-10-20");

        Assert.Equal(6_500_00L, summary.RevenueCents);
        Assert.Equal(5_500_00L, summary.CostOfGoodsSoldCents);
        Assert.Equal(1_000_00L, summary.GrossProfitCents);
        Assert.Equal(0L, summary.ExpensesCents);
        Assert.Equal(1_000_00L, summary.NetProfitCents);
    }

    [Fact]
    public async Task ProfitSummary_SubtractsExpenses()
    {
        await SellAndPayAsync(_productA, 10_000);  // gross profit 1,000 KES
        await _expenses.RecordExpenseAsync("Rent", null, 400_00, "2026-10-20", _userId);
        await _expenses.RecordExpenseAsync("Transport", null, 300_00, "2026-10-20", _userId);

        var summary = await _reports.GetProfitSummaryAsync("2026-10-20", "2026-10-20");

        Assert.Equal(1_000_00L, summary.GrossProfitCents);
        Assert.Equal(700_00L, summary.ExpensesCents);
        Assert.Equal(300_00L, summary.NetProfitCents);  // net profit = 300 KES
    }

    [Fact]
    public async Task ProfitSummary_CanBeNegative()
    {
        await SellAndPayAsync(_productA, 1_000);  // gross profit 100 KES
        await _expenses.RecordExpenseAsync("Rent", null, 400_00, "2026-10-20", _userId);

        var summary = await _reports.GetProfitSummaryAsync("2026-10-20", "2026-10-20");

        Assert.Equal(100_00L, summary.GrossProfitCents);
        Assert.Equal(400_00L, summary.ExpensesCents);
        Assert.True(summary.NetProfitCents < 0);
        Assert.Equal(-300_00L, summary.NetProfitCents);  // loss of 300 KES
    }

    [Fact]
    public async Task ProfitSummary_FlagsMissingCostLines()
    {
        // Insert a product with zero cost (no goods receipt ever recorded)
        using var c = _db.Factory.CreateOpen();
        int zeroCostProduct = (int)c.ExecuteScalar<long>(@"
            INSERT INTO products(uuid, name, base_unit_id, avg_cost_cents, stock_milli, is_active)
            VALUES ($uuid, 'Mystery Item', 1, 0, 10000, 1) RETURNING id",
            new { uuid = Guid.NewGuid().ToString("N") });
        c.Execute("INSERT INTO product_prices(product_id, tier_id, price_cents) VALUES ($id, 1, 500)",
            new { id = zeroCostProduct });

        await SellAndPayAsync(zeroCostProduct, 1_000);

        var summary = await _reports.GetProfitSummaryAsync("2026-10-20", "2026-10-20");

        Assert.Equal(1, summary.UncategorizedCostLineCount);
        // Profit is inflated because cost is 0
        Assert.Equal(5_00L, summary.GrossProfitCents);
    }

    [Fact]
    public async Task ProfitSummary_OnlyCountsCompletedSales()
    {
        using var c = _db.Factory.CreateOpen();
        c.Execute(@"
            INSERT INTO sales(uuid, receipt_number, status, user_id, prices_include_tax,
                              subtotal_cents, total_cents, business_date)
            VALUES ($uuid, 'RCT-VOID-1', 'VOIDED', $userId, 1, 100000, 100000, '2026-10-20')",
            new { uuid = Guid.NewGuid().ToString("N"), userId = _userId });

        var summary = await _reports.GetProfitSummaryAsync("2026-10-20", "2026-10-20");

        Assert.Equal(0L, summary.RevenueCents);  // voided sale excluded
    }

    // ===== ProductProfit =====

    [Fact]
    public async Task ProductProfit_SortsByProfitDescending()
    {
        // Cement: sell 1 bag, profit = 650-550 = 100 KES
        await SellAndPayAsync(_productA, 1_000);
        // Nails: sell 100 nails, profit = (500-300)*100 = 20,000 KES -- higher profit
        await SellAndPayAsync(_productB, 100_000);

        var rows = await _reports.GetProductProfitAsync("2026-10-20", "2026-10-20");

        Assert.Equal(2, rows.Count);
        Assert.Equal("Nail 3in", rows[0].ProductName);  // nails have more profit
        Assert.Equal("Cement 50kg", rows[1].ProductName);
    }

    [Fact]
    public async Task ProductProfit_CalculatesMarginPercentage()
    {
        await SellAndPayAsync(_productA, 1_000);  // 650 revenue, 550 cost, 100 profit = 15.4% margin

        var rows = await _reports.GetProductProfitAsync("2026-10-20", "2026-10-20");

        Assert.Single(rows);
        Assert.Equal(15.4m, rows[0].MarginPercent);
    }

    // ===== Dead Stock =====

    [Fact]
    public async Task DeadStock_IncludesNeverSoldProducts()
    {
        // Neither product has sold — both should appear
        var rows = await _reports.GetDeadStockAsync(90);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(-1, r.DaysSinceLastSale));
    }

    [Fact]
    public async Task DeadStock_ExcludesRecentlySold()
    {
        await SellAndPayAsync(_productA, 1_000);

        // Today's sale on product A, so it shouldn't be dead
        var rows = await _reports.GetDeadStockAsync(90);

        Assert.Single(rows);
        Assert.Equal("Nail 3in", rows[0].ProductName);  // only nail is dead
    }

    [Fact]
    public async Task DeadStock_ExcludesProductsWithNoStock()
    {
        using var c = _db.Factory.CreateOpen();
        // Add a product with 0 stock — shouldn't show as dead stock
        c.Execute(@"
            INSERT INTO products(uuid, name, base_unit_id, avg_cost_cents, stock_milli, is_active)
            VALUES ($uuid, 'Empty Product', 1, 100, 0, 1)",
            new { uuid = Guid.NewGuid().ToString("N") });

        var rows = await _reports.GetDeadStockAsync(90);

        Assert.DoesNotContain(rows, r => r.ProductName == "Empty Product");
    }
}
