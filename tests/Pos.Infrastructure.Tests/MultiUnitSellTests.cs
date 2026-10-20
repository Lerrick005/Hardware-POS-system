using Dapper;
using Pos.Application.Sales;
using Pos.Infrastructure.Catalog;
using Pos.Infrastructure.Sales;

namespace Pos.Infrastructure.Tests;

/// <summary>
/// Proves the "roofing nails sold as pieces, kg, or boxes" scenario:
/// same product, same physical stockpile, different selling units, different
/// prices per unit. Stock deducts correctly. Revenue matches the unit price.
/// </summary>
public sealed class MultiUnitSellTests : IDisposable
{
    private const int UnitPiece = 1;
    private const int UnitKilogram = 2;
    private const int UnitBox = 6;

    private readonly TestDatabase _db = new();
    private readonly SellService _sell;
    private readonly ProductRepository _products;
    private readonly int _userId;
    private readonly int _nailId;

    public MultiUnitSellTests()
    {
        _sell = new SellService(_db.Factory);
        _products = new ProductRepository(_db.Factory);

        _userId = InsertUser();

        // Roofing Nails, base unit Piece. Stockpile 1000 pieces.
        _nailId = _products.InsertAsync(new()
        {
            Uuid = Guid.NewGuid().ToString("N"),
            Name = "Roofing Nail 3in",
            BaseUnitId = UnitPiece,
            IsActive = true,
        }).GetAwaiter().GetResult();

        // Seed stock: 1000 pieces
        using (var c = _db.Factory.CreateOpen())
        {
            c.Execute(@"
                INSERT INTO stock_movements(product_id, movement_type, qty_milli, ref_type, business_date)
                VALUES ($p, 'OPENING', 1000000, 'OPENING', '2026-10-20')", new { p = _nailId });
        }

        // Configure: 1 kg = 25 pieces, 1 box = 120 pieces
        _products.SaveProductUnitAsync(_nailId, UnitKilogram, 25_000).GetAwaiter().GetResult();
        _products.SaveProductUnitAsync(_nailId, UnitBox, 120_000).GetAwaiter().GetResult();

        // Prices (Retail tier = 1):
        //   Piece     = 5 KES  (500 cents)
        //   Kilogram  = 400 KES (40,000 cents)
        //   Box       = 500 KES (50,000 cents)
        _products.UpsertPriceForUnitAsync(_nailId, 1, UnitPiece, 500).GetAwaiter().GetResult();
        _products.UpsertPriceForUnitAsync(_nailId, 1, UnitKilogram, 40_000).GetAwaiter().GetResult();
        _products.UpsertPriceForUnitAsync(_nailId, 1, UnitBox, 50_000).GetAwaiter().GetResult();
    }

    public void Dispose() => _db.Dispose();

    private int InsertUser()
    {
        using var c = _db.Factory.CreateOpen();
        return (int)c.ExecuteScalar<long>(
            "INSERT INTO users(name, role, pin_hash, is_active) VALUES ('Cashier', 'CASHIER', 'PIN_NOT_SET', 1) RETURNING id");
    }

    private long CurrentStockMilli()
    {
        using var c = _db.Factory.CreateOpen();
        return c.ExecuteScalar<long>("SELECT stock_milli FROM products WHERE id = $id", new { id = _nailId });
    }

    private async Task<SaleResult> SellAsync(params (int UnitId, long QtyMilli)[] lines)
    {
        var req = new SaleRequest(
            CustomerId: null,
            PriceTierId: 1,
            BusinessDate: "2026-10-20",
            Lines: lines.Select(l => new SaleLineRequest(
                ProductId: _nailId, UnitId: l.UnitId,
                QtyMilli: l.QtyMilli, UnitPriceCentsOverride: null,
                DiscountCents: 0, Notes: null)).ToArray(),
            Payments: new[] { new PaymentRequest("CASH", 999_999_99, null) },
            UserId: _userId,
            CashSessionId: null);

        return await _sell.CompleteSaleAsync(req);
    }

    // ===== Individual units =====

    [Fact]
    public async Task SellByPiece_DeductsPieces_RevenueAtPiecePrice()
    {
        long before = CurrentStockMilli();  // 1,000,000

        var result = await SellAsync((UnitPiece, 3_000));   // 3 pieces

        Assert.Single(result.Lines);
        Assert.Equal("Piece", result.Lines[0].UnitName);
        Assert.Equal(3_000, result.Lines[0].QtyMilli);
        Assert.Equal(3_000, result.Lines[0].BaseQtyMilli);
        Assert.Equal(1_000, result.Lines[0].FactorMilli);
        Assert.Equal(1_500, result.Lines[0].LineTotalCents);  // 3 × 5 KES = 15 KES

        Assert.Equal(before - 3_000, CurrentStockMilli());
    }

    [Fact]
    public async Task SellByKilogram_DeductsBasePieces_RevenueAtKgPrice()
    {
        long before = CurrentStockMilli();  // 1,000,000

        var result = await SellAsync((UnitKilogram, 750));   // 0.75 kg

        Assert.Single(result.Lines);
        Assert.Equal("Kilogram", result.Lines[0].UnitName);
        Assert.Equal(750, result.Lines[0].QtyMilli);
        Assert.Equal(18_750, result.Lines[0].BaseQtyMilli);   // 0.75 × 25 pieces
        Assert.Equal(25_000, result.Lines[0].FactorMilli);
        Assert.Equal(30_000, result.Lines[0].LineTotalCents); // 0.75 × 400 KES = 300 KES

        Assert.Equal(before - 18_750, CurrentStockMilli());
    }

    [Fact]
    public async Task SellByBox_DeductsBasePieces_RevenueAtBoxPrice()
    {
        long before = CurrentStockMilli();  // 1,000,000

        var result = await SellAsync((UnitBox, 2_000));      // 2 boxes

        Assert.Single(result.Lines);
        Assert.Equal("Box", result.Lines[0].UnitName);
        Assert.Equal(2_000, result.Lines[0].QtyMilli);
        Assert.Equal(240_000, result.Lines[0].BaseQtyMilli);  // 2 × 120 pieces
        Assert.Equal(120_000, result.Lines[0].FactorMilli);
        Assert.Equal(100_000, result.Lines[0].LineTotalCents); // 2 × 500 KES = 1,000 KES

        Assert.Equal(before - 240_000, CurrentStockMilli());
    }

    // ===== Mixed units on one receipt =====

    [Fact]
    public async Task SellMixedUnits_SameProductThreeUnits_OneReceipt()
    {
        long before = CurrentStockMilli();  // 1,000,000

        // 1 box (120 pcs) + 0.5 kg (12.5 pcs) + 5 pieces = 137.5 pieces deducted
        var result = await SellAsync(
            (UnitBox, 1_000),
            (UnitKilogram, 500),
            (UnitPiece, 5_000));

        Assert.Equal(3, result.Lines.Count);

        // Stock: −137,500 milli = 137.5 pieces
        Assert.Equal(before - 137_500, CurrentStockMilli());

        // Revenue: 1×500 + 0.5×400 + 5×5 = 500 + 200 + 25 = 725 KES
        Assert.Equal(72_500, result.TotalCents);
    }

    // ===== Validation =====

    [Fact]
    public async Task Sell_UnconfiguredUnit_RejectedWithClearMessage()
    {
        // Metre (id 3) not configured for nails
        var ex = await Assert.ThrowsAsync<SaleValidationException>(() =>
            SellAsync((3, 1_000)));   // 1 metre, but no factor / price

        Assert.Contains("not configured", ex.Message);
    }

    [Fact]
    public async Task Sell_AlternateUnitWithoutPrice_Rejected()
    {
        // Add a new product with a KG unit but NO price for it
        int id = await _products.InsertAsync(new()
        {
            Uuid = Guid.NewGuid().ToString("N"),
            Name = "No Price Nail",
            BaseUnitId = UnitPiece,
            IsActive = true,
        });
        await _products.SaveProductUnitAsync(id, UnitKilogram, 25_000);
        // Deliberately do NOT set a price for KG

        using var c = _db.Factory.CreateOpen();
        await c.ExecuteAsync(@"
            INSERT INTO stock_movements(product_id, movement_type, qty_milli, ref_type, business_date)
            VALUES ($id, 'OPENING', 1000000, 'OPENING', '2026-10-20')", new { id });

        var ex = await Assert.ThrowsAsync<SaleValidationException>(() =>
            _sell.CompleteSaleAsync(new SaleRequest(
                CustomerId: null, PriceTierId: 1, BusinessDate: "2026-10-20",
                Lines: new[] { new SaleLineRequest(id, UnitKilogram, 500, null, 0, null) },
                Payments: new[] { new PaymentRequest("CASH", 999_999_99, null) },
                UserId: _userId, CashSessionId: null)));

        Assert.Contains("No price set", ex.Message);
    }

    // ===== Price override on alternate unit =====

    [Fact]
    public async Task Sell_PriceOverrideOnKg_UsesOverride()
    {
        var result = await _sell.CompleteSaleAsync(new SaleRequest(
            CustomerId: null, PriceTierId: 1, BusinessDate: "2026-10-20",
            Lines: new[]
            {
                new SaleLineRequest(_nailId, UnitKilogram, 1_000, 45_000, 0, null), // override to 450 KES/kg
            },
            Payments: new[] { new PaymentRequest("CASH", 999_999_99, null) },
            UserId: _userId, CashSessionId: null));

        // 1 kg × 450 KES = 450 KES = 45,000 cents
        Assert.Equal(45_000, result.Lines[0].LineTotalCents);
        Assert.Equal(25_000, result.Lines[0].BaseQtyMilli);  // 1 kg = 25 pieces
    }
}
