using Dapper;
using Microsoft.Data.Sqlite;
using Pos.Application.Stock;
using Pos.Infrastructure.Catalog;
using Pos.Infrastructure.Stock;
using Pos.Domain.Catalog;

namespace Pos.Infrastructure.Tests;

public sealed class ReceiveGoodsServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly ReceiveGoodsService _service;
    private readonly ProductRepository _products;
    private readonly int _productA;
    private readonly int _productB;

    public ReceiveGoodsServiceTests()
    {
        _service = new ReceiveGoodsService(_db.Factory);
        _products = new ProductRepository(_db.Factory);
        _productA = _products.InsertAsync(new Product
        {
            Uuid = Guid.NewGuid().ToString("N"),
            Name = "Cement 50kg",
            BaseUnitId = 1,
        }).GetAwaiter().GetResult();
        _productB = _products.InsertAsync(new Product
        {
            Uuid = Guid.NewGuid().ToString("N"),
            Name = "Roofing Nail 3 inch",
            BaseUnitId = 1,
        }).GetAwaiter().GetResult();
    }

    public void Dispose() => _db.Dispose();

    private static ReceiveGoodsRequest Req(params ReceiveGoodsLine[] lines) => new(
        SupplierId: null,
        SupplierRef: null,
        BusinessDate: "2026-10-19",
        Lines: lines);

    [Fact]
    public async Task Execute_HappyPath_InsertsGRNAndStock()
    {
        var result = await _service.ExecuteAsync(Req(
            new ReceiveGoodsLine(_productA, 10000, 250),
            new ReceiveGoodsLine(_productB, 5000, 40)));

        Assert.True(result.ReceiptId > 0);
        Assert.StartsWith("GRN-", result.GrnNumber);
        Assert.Equal(10000 * 250 / 1000 + 5000 * 40 / 1000, result.TotalCents);

        Product pA = (await _products.GetByIdAsync(_productA))!;
        Product pB = (await _products.GetByIdAsync(_productB))!;
        Assert.Equal(10000, pA.StockMilli);
        Assert.Equal(5000, pB.StockMilli);
        Assert.Equal(250, pA.AvgCostCents);
        Assert.Equal(40, pB.AvgCostCents);
    }

    [Fact]
    public async Task Execute_SecondReceipt_AppliesWeightedAverage()
    {
        await _service.ExecuteAsync(Req(new ReceiveGoodsLine(_productA, 10000, 250)));
        await _service.ExecuteAsync(Req(new ReceiveGoodsLine(_productA, 10000, 300)));

        Product p = (await _products.GetByIdAsync(_productA))!;
        Assert.Equal(20000, p.StockMilli);
        Assert.Equal(275, p.AvgCostCents);      // (250 + 300) / 2
        Assert.Equal(300, p.LastCostCents);
    }

    [Fact]
    public async Task Execute_OpeningStockWithNoCost_AdoptsReceivedCost()
    {
        // Pre-existing opening stock at avg=0 (unknown cost).
        using (SqliteConnection c = _db.Factory.CreateOpen())
        {
            await c.ExecuteAsync(@"
                INSERT INTO stock_movements(product_id, movement_type, qty_milli, ref_type, business_date)
                VALUES ($p, 'OPENING', 4000, 'OPENING', '2026-10-18');", new { p = _productA });
        }

        await _service.ExecuteAsync(Req(new ReceiveGoodsLine(_productA, 6000, 250)));

        Product p = (await _products.GetByIdAsync(_productA))!;
        Assert.Equal(10000, p.StockMilli);
        Assert.Equal(250, p.AvgCostCents);
    }

    [Fact]
    public async Task Execute_MissingProduct_RollsBackEverything()
    {
        var ex = await Assert.ThrowsAsync<ReceiveGoodsValidationException>(() =>
            _service.ExecuteAsync(Req(
                new ReceiveGoodsLine(_productA, 1000, 250),
                new ReceiveGoodsLine(ProductId: 9999, QtyMilli: 1000, UnitCostCents: 100))));

        Assert.Contains("9999", ex.Message);

        using SqliteConnection c = _db.Factory.CreateOpen();
        long grnCount = await c.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM goods_receipts");
        long movementCount = await c.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM stock_movements");
        Product pA = (await _products.GetByIdAsync(_productA))!;

        Assert.Equal(0L, grnCount);
        Assert.Equal(0L, movementCount);
        Assert.Equal(0L, pA.StockMilli);
    }

    [Fact]
    public async Task Execute_DuplicateProductInOneReceipt_Rejected()
    {
        var ex = await Assert.ThrowsAsync<ReceiveGoodsValidationException>(() =>
            _service.ExecuteAsync(Req(
                new ReceiveGoodsLine(_productA, 1000, 250),
                new ReceiveGoodsLine(_productA, 500, 260))));

        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact]
    public async Task Execute_EmptyLines_Rejected()
    {
        await Assert.ThrowsAsync<ReceiveGoodsValidationException>(() =>
            _service.ExecuteAsync(Req()));
    }

    [Fact]
    public async Task Execute_BadBusinessDate_Rejected()
    {
        var req = new ReceiveGoodsRequest(
            SupplierId: null, SupplierRef: null, BusinessDate: "19-10-2026",
            Lines: new[] { new ReceiveGoodsLine(_productA, 1000, 250) });

        await Assert.ThrowsAsync<ReceiveGoodsValidationException>(() => _service.ExecuteAsync(req));
    }

    [Fact]
    public async Task Execute_ZeroQty_Rejected()
    {
        await Assert.ThrowsAsync<ReceiveGoodsValidationException>(() =>
            _service.ExecuteAsync(Req(new ReceiveGoodsLine(_productA, 0, 250))));
    }

    [Fact]
    public async Task Execute_NegativeCost_Rejected()
    {
        await Assert.ThrowsAsync<ReceiveGoodsValidationException>(() =>
            _service.ExecuteAsync(Req(new ReceiveGoodsLine(_productA, 1000, -1))));
    }

    [Fact]
    public async Task Execute_GrnNumbersAreSequentialAndGapFree_OnSuccess()
    {
        var r1 = await _service.ExecuteAsync(Req(new ReceiveGoodsLine(_productA, 1000, 250)));
        var r2 = await _service.ExecuteAsync(Req(new ReceiveGoodsLine(_productA, 1000, 250)));
        var r3 = await _service.ExecuteAsync(Req(new ReceiveGoodsLine(_productA, 1000, 250)));

        Assert.Equal("GRN-000001", r1.GrnNumber);
        Assert.Equal("GRN-000002", r2.GrnNumber);
        Assert.Equal("GRN-000003", r3.GrnNumber);
    }
}
