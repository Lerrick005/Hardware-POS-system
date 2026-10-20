using Dapper;
using Microsoft.Data.Sqlite;
using Pos.Application.Sales;
using Pos.Infrastructure.Catalog;
using Pos.Infrastructure.Sales;

namespace Pos.Infrastructure.Tests;

public sealed class SellServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly SellService _service;
    private readonly ProductRepository _products;
    private readonly int _productA;
    private readonly int _productB;
    private readonly int _customer;
    private readonly int _userId;

    public SellServiceTests()
    {
        _service = new SellService(_db.Factory);
        _products = new ProductRepository(_db.Factory);
        _userId = InsertUser();

        _productA = _products.InsertAsync(new()
        {
            Uuid = Guid.NewGuid().ToString("N"),
            Name = "Cement 50kg",
            BaseUnitId = 1,
            IsActive = true,
        }).GetAwaiter().GetResult();
        _products.UpsertPriceAsync(_productA, 1, 65000).GetAwaiter().GetResult();

        _productB = _products.InsertAsync(new()
        {
            Uuid = Guid.NewGuid().ToString("N"),
            Name = "Nails 3in",
            BaseUnitId = 1,
            IsActive = true,
        }).GetAwaiter().GetResult();
        _products.UpsertPriceAsync(_productB, 1, 500).GetAwaiter().GetResult();

        _customer = InsertCustomer();
    }

    public void Dispose() => _db.Dispose();

    private int InsertUser()
    {
        using SqliteConnection c = _db.Factory.CreateOpen();
        long id = c.ExecuteScalar<long>(@"
            INSERT INTO users(name, role, pin_hash)
            VALUES ('Test Cashier', 'CASHIER', 'placeholder-hash') RETURNING id");
        return (int)id;
    }

    private int InsertCustomer()
    {
        using SqliteConnection c = _db.Factory.CreateOpen();
        long id = c.ExecuteScalar<long>(@"
            INSERT INTO customers(uuid, name, credit_limit_cents, is_active)
            VALUES ($uuid, 'Test Customer', 1000000, 1) RETURNING id",
            new { uuid = Guid.NewGuid().ToString("N") });
        return (int)id;
    }

    private async Task StockUpAsync(int productId, long qtyMilli)
    {
        using SqliteConnection c = _db.Factory.CreateOpen();
        await c.ExecuteAsync(@"
            INSERT INTO stock_movements(product_id, movement_type, qty_milli, ref_type, business_date)
            VALUES ($productId, 'OPENING', $qtyMilli, 'OPENING', '2026-10-20')",
            new { productId, qtyMilli });
    }

    private SaleRequest SingleLineCash(int productId, long qtyMilli, long priceCents, long cashCents) => new(
        CustomerId: null,
        PriceTierId: 1,
        BusinessDate: "2026-10-20",
        Lines: new[] { new SaleLineRequest(productId, 1, qtyMilli, priceCents, 0, null) },
        Payments: new[] { new PaymentRequest("CASH", cashCents, null) },
        UserId: _userId,
        CashSessionId: null);

    private async Task<(int sales, int items, int payments, int movements, int ledger, int audit)> CountsAsync()
    {
        using SqliteConnection c = _db.Factory.CreateOpen();
        return (
            (int)await c.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM sales"),
            (int)await c.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM sale_items"),
            (int)await c.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM payments"),
            (int)await c.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM stock_movements"),
            (int)await c.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM customer_ledger"),
            (int)await c.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM audit_log"));
    }

    [Fact]
    public async Task CashSale_SingleLine_AllRowsWritten_StockDecremented()
    {
        await StockUpAsync(_productA, 10_000);

        SaleResult result = await _service.CompleteSaleAsync(SingleLineCash(_productA, 2000, 65000, 130000));

        Assert.StartsWith("RCT-", result.ReceiptNumber);
        Assert.Equal(130000, result.TotalCents);
        Assert.Equal(130000, result.PaidCents);
        Assert.Equal(0, result.ChangeCents);

        using SqliteConnection c = _db.Factory.CreateOpen();
        long stock = await c.ExecuteScalarAsync<long>(
            "SELECT stock_milli FROM products WHERE id = $id", new { id = _productA });
        Assert.Equal(8000, stock);

        Assert.Equal(1L, await c.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM sales"));
        Assert.Equal(1L, await c.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM sale_items"));
        Assert.Equal(1L, await c.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM payments"));
    }

    [Fact]
    public async Task CashSale_Overpayment_ReturnsChange()
    {
        await StockUpAsync(_productA, 10_000);

        SaleResult result = await _service.CompleteSaleAsync(
            SingleLineCash(_productA, 1000, 65000, 100000));

        Assert.Equal(65000, result.TotalCents);
        Assert.Equal(100000, result.PaidCents);
        Assert.Equal(35000, result.ChangeCents);
    }

    [Fact]
    public async Task MultiLine_SplitPayment_CreditWritesLedger()
    {
        await StockUpAsync(_productA, 10_000);
        await StockUpAsync(_productB, 20_000);

        SaleRequest request = new(
            CustomerId: _customer,
            PriceTierId: 1,
            BusinessDate: "2026-10-20",
            Lines: new[]
            {
                new SaleLineRequest(_productA, 1, 1000, 65000, 0, null),
                new SaleLineRequest(_productB, 1, 2000, 500, 0, null),
            },
            Payments: new[]
            {
                new PaymentRequest("CASH", 30000, null),
                new PaymentRequest("MPESA", 30000, "QWE123"),
                new PaymentRequest("CREDIT", 6000, null),
            },
            UserId: _userId,
            CashSessionId: null);

        SaleResult result = await _service.CompleteSaleAsync(request);

        Assert.Equal(66000, result.TotalCents);
        Assert.Equal(66000, result.PaidCents);
        Assert.Equal(0, result.ChangeCents);

        using SqliteConnection c = _db.Factory.CreateOpen();
        long balance = await c.ExecuteScalarAsync<long>(
            "SELECT COALESCE(SUM(amount_cents),0) FROM customer_ledger WHERE customer_id = $id",
            new { id = _customer });
        Assert.Equal(6000, balance);
    }

    [Fact]
    public async Task ReceiptNumbersAreSequential()
    {
        await StockUpAsync(_productA, 10_000);

        SaleResult r1 = await _service.CompleteSaleAsync(SingleLineCash(_productA, 1000, 65000, 65000));
        SaleResult r2 = await _service.CompleteSaleAsync(SingleLineCash(_productA, 1000, 65000, 65000));
        SaleResult r3 = await _service.CompleteSaleAsync(SingleLineCash(_productA, 1000, 65000, 65000));

        Assert.Equal("RCT-000001", r1.ReceiptNumber);
        Assert.Equal("RCT-000002", r2.ReceiptNumber);
        Assert.Equal("RCT-000003", r3.ReceiptNumber);
    }

    [Fact]
    public async Task InsufficientStock_RollsBackEverything()
    {
        await StockUpAsync(_productA, 1000);

        var before = await CountsAsync();
        await Assert.ThrowsAsync<SaleValidationException>(() =>
            _service.CompleteSaleAsync(SingleLineCash(_productA, 2000, 65000, 130000)));
        var after = await CountsAsync();

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Underpayment_RollsBackEverything()
    {
        await StockUpAsync(_productA, 10_000);

        var before = await CountsAsync();
        await Assert.ThrowsAsync<SaleValidationException>(() =>
            _service.CompleteSaleAsync(SingleLineCash(_productA, 2000, 65000, 100000)));
        var after = await CountsAsync();

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task CreditExceedsLimit_RollsBack()
    {
        using (SqliteConnection c = _db.Factory.CreateOpen())
        {
            await c.ExecuteAsync("UPDATE customers SET credit_limit_cents = 5000 WHERE id = $id",
                new { id = _customer });
        }
        await StockUpAsync(_productA, 10_000);

        SaleRequest req = new(
            CustomerId: _customer, PriceTierId: 1, BusinessDate: "2026-10-20",
            Lines: new[] { new SaleLineRequest(_productA, 1, 1000, 65000, 0, null) },
            Payments: new[] { new PaymentRequest("CREDIT", 65000, null) },
            UserId: _userId, CashSessionId: null);

        var before = await CountsAsync();
        await Assert.ThrowsAsync<SaleValidationException>(() => _service.CompleteSaleAsync(req));
        var after = await CountsAsync();

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task CreditWithoutCustomer_Rejected()
    {
        await StockUpAsync(_productA, 10_000);

        SaleRequest req = new(
            CustomerId: null, PriceTierId: 1, BusinessDate: "2026-10-20",
            Lines: new[] { new SaleLineRequest(_productA, 1, 1000, 65000, 0, null) },
            Payments: new[] { new PaymentRequest("CREDIT", 65000, null) },
            UserId: _userId, CashSessionId: null);

        await Assert.ThrowsAsync<SaleValidationException>(() => _service.CompleteSaleAsync(req));
    }

    [Fact]
    public async Task DuplicateProduct_Rejected()
    {
        await StockUpAsync(_productA, 10_000);

        SaleRequest req = new(
            CustomerId: null, PriceTierId: 1, BusinessDate: "2026-10-20",
            Lines: new[]
            {
                new SaleLineRequest(_productA, 1, 1000, 65000, 0, null),
                new SaleLineRequest(_productA, 1, 500, 65000, 0, null),
            },
            Payments: new[] { new PaymentRequest("CASH", 100000, null) },
            UserId: _userId, CashSessionId: null);

        await Assert.ThrowsAsync<SaleValidationException>(() => _service.CompleteSaleAsync(req));
    }

    [Fact]
    public async Task UnknownProduct_RollsBack()
    {
        await StockUpAsync(_productA, 10_000);

        SaleRequest req = new(
            CustomerId: null, PriceTierId: 1, BusinessDate: "2026-10-20",
            Lines: new[] { new SaleLineRequest(9999, 1, 1000, 65000, 0, null) },
            Payments: new[] { new PaymentRequest("CASH", 65000, null) },
            UserId: _userId, CashSessionId: null);

        var before = await CountsAsync();
        await Assert.ThrowsAsync<SaleValidationException>(() => _service.CompleteSaleAsync(req));
        var after = await CountsAsync();

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task CreditWithOverpayment_Rejected()
    {
        await StockUpAsync(_productA, 10_000);

        // 66,000 sale, but paying 66,000 cash AND 6,000 credit — absurd.
        // Service must reject: you cannot both fully pay in cash and borrow.
        SaleRequest req = new(
            CustomerId: _customer, PriceTierId: 1, BusinessDate: "2026-10-20",
            Lines: new[] { new SaleLineRequest(_productA, 1, 1000, 65000, 0, null) },
            Payments: new[]
            {
                new PaymentRequest("CASH", 66000, null),
                new PaymentRequest("CREDIT", 6000, null),
            },
            UserId: _userId, CashSessionId: null);

        var before = await CountsAsync();
        await Assert.ThrowsAsync<SaleValidationException>(() => _service.CompleteSaleAsync(req));
        var after = await CountsAsync();

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task CreditLessThanShortfall_Rejected()
    {
        await StockUpAsync(_productA, 10_000);

        // 65,000 sale, cash 50,000, credit 5,000 — leaves 10,000 unaccounted for.
        SaleRequest req = new(
            CustomerId: _customer, PriceTierId: 1, BusinessDate: "2026-10-20",
            Lines: new[] { new SaleLineRequest(_productA, 1, 1000, 65000, 0, null) },
            Payments: new[]
            {
                new PaymentRequest("CASH", 50000, null),
                new PaymentRequest("CREDIT", 5000, null),
            },
            UserId: _userId, CashSessionId: null);

        await Assert.ThrowsAsync<SaleValidationException>(() => _service.CompleteSaleAsync(req));
    }

    [Fact]
    public async Task PriceOverride_WritesAuditEntry()
    {
        await StockUpAsync(_productA, 10_000);

        SaleRequest req = new(
            CustomerId: null, PriceTierId: 1, BusinessDate: "2026-10-20",
            Lines: new[] { new SaleLineRequest(_productA, 1, 1000, 60000, 0, null) },
            Payments: new[] { new PaymentRequest("CASH", 60000, null) },
            UserId: _userId, CashSessionId: null);

        await _service.CompleteSaleAsync(req);

        using SqliteConnection c = _db.Factory.CreateOpen();
        long overrideCount = await c.ExecuteScalarAsync<long>(
            "SELECT COUNT(1) FROM audit_log WHERE action = 'PRICE_OVERRIDE'");
        Assert.Equal(1L, overrideCount);
    }
}



