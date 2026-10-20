using Dapper;
using Pos.Application.Sales;
using Pos.Infrastructure.Catalog;
using Pos.Infrastructure.Sales;

namespace Pos.Infrastructure.Tests;

/// <summary>
/// Proves the multi-unit system is generic — not nails-specific. Any product
/// with any base unit can have any alternative units at any prices.
/// </summary>
public sealed class MultiUnitFlexibilityTests : IDisposable
{
    // Unit IDs from seed
    private const int UnitPiece = 1;
    private const int UnitMetre = 3;
    private const int UnitLitre = 4;

    private readonly TestDatabase _db = new();
    private readonly SellService _sell;
    private readonly ProductRepository _products;
    private readonly int _userId;

    public MultiUnitFlexibilityTests()
    {
        _sell = new SellService(_db.Factory);
        _products = new ProductRepository(_db.Factory);

        using var c = _db.Factory.CreateOpen();
        _userId = (int)c.ExecuteScalar<long>(
            "INSERT INTO users(name, role, pin_hash, is_active) VALUES ('C', 'CASHIER', 'PIN_NOT_SET', 1) RETURNING id");
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> CreateProductAsync(string name, int baseUnitId, long stockMilli, long basePrice)
    {
        int id = await _products.InsertAsync(new()
        {
            Uuid = Guid.NewGuid().ToString("N"),
            Name = name,
            BaseUnitId = baseUnitId,
            IsActive = true,
        });

        using var c = _db.Factory.CreateOpen();
        await c.ExecuteAsync(@"
            INSERT INTO stock_movements(product_id, movement_type, qty_milli, ref_type, business_date)
            VALUES ($id, 'OPENING', $q, 'OPENING', '2026-10-20')", new { id, q = stockMilli });

        await _products.UpsertPriceForUnitAsync(id, 1, baseUnitId, basePrice);
        return id;
    }

    private async Task<SaleResult> SellAsync(int productId, int unitId, long qtyMilli)
    {
        return await _sell.CompleteSaleAsync(new SaleRequest(
            CustomerId: null, PriceTierId: 1, BusinessDate: "2026-10-20",
            Lines: new[] { new SaleLineRequest(productId, unitId, qtyMilli, null, 0, null) },
            Payments: new[] { new PaymentRequest("CASH", 99_999_999, null) },
            UserId: _userId, CashSessionId: null));
    }

    private long CurrentStock(int productId)
    {
        using var c = _db.Factory.CreateOpen();
        return c.ExecuteScalar<long>("SELECT stock_milli FROM products WHERE id = $id", new { id = productId });
    }

    [Fact]
    public async Task PVC_Pipe_MetreBase_SoldAsMetreOrAsStandardLength()
    {
        // PVC pipe: base Metre. Sell by metre OR by standard 3-metre length.
        int id = await CreateProductAsync("PVC Pipe 1in", UnitMetre, stockMilli: 100_000, basePrice: 15_000);
        // Stock = 100 metres, base price = 150 KES/metre

        // 1 "3m length" = 3 metres = 3,000 milli
        await _products.SaveProductUnitAsync(id, UnitPiece, 3_000);
        // 3m length retail price = 400 KES
        await _products.UpsertPriceForUnitAsync(id, 1, UnitPiece, 40_000);

        long before = CurrentStock(id);   // 100,000 milli = 100 metres

        // Sell 2 × 3m lengths
        var result = await SellAsync(id, UnitPiece, 2_000);

        // Stock: 2 pieces × 3m = 6 metres = 6,000 milli deducted
        Assert.Equal(before - 6_000, CurrentStock(id));
        // Revenue: 2 × 400 KES = 800 KES = 80,000 cents
        Assert.Equal(80_000, result.TotalCents);
        Assert.Equal("Piece", result.Lines[0].UnitName);
        Assert.Equal(6_000, result.Lines[0].BaseQtyMilli);
    }

    [Fact]
    public async Task Paint_Turpentine_LitreBase_SoldAsBottleOrBulk()
    {
        // Turpentine: base Litre. Sold by litre OR by 4L tin.
        int id = await CreateProductAsync("Turpentine", UnitLitre, stockMilli: 200_000, basePrice: 20_000);
        // Stock = 200 litres, base price = 200 KES/litre

        // 1 "4L tin" = 4 litres = 4,000 milli
        await _products.SaveProductUnitAsync(id, UnitPiece, 4_000);
        // 4L tin retail price = 700 KES
        await _products.UpsertPriceForUnitAsync(id, 1, UnitPiece, 70_000);

        long before = CurrentStock(id);

        // Sell 0.5 litre (small touch-up)
        var soldByLitre = await SellAsync(id, UnitLitre, 500);
        Assert.Equal(before - 500, CurrentStock(id));       // 0.5 litres = 500 milli
        Assert.Equal(10_000, soldByLitre.TotalCents);       // 0.5 × 200 KES = 100 KES

        // Sell 1 × 4L tin
        var soldByTin = await SellAsync(id, UnitPiece, 1_000);
        Assert.Equal(before - 500 - 4_000, CurrentStock(id)); // 4 litres = 4,000 milli
        Assert.Equal(70_000, soldByTin.TotalCents);           // 700 KES

        // Total stock moved: 4.5 litres out of 200
        Assert.Equal(before - 4_500, CurrentStock(id));
    }
}
