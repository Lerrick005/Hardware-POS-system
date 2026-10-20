using Dapper;
using Pos.Domain.Catalog;
using Pos.Infrastructure.Catalog;

namespace Pos.Infrastructure.Tests;

public sealed class MultiUnitPricingTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly ProductRepository _repo;
    private readonly int _nailProductId;

    // Unit IDs from seed: 1=Piece, 2=Kilogram, 3=Metre, 4=Litre, 5=Bag, 6=Box, 7=Roll,
    //                     8=Square Metre, 9=Cubic Metre, 10=Foot, 11=Wheelbarrow, 12=Tipper
    private const int UnitPiece = 1;
    private const int UnitKilogram = 2;
    private const int UnitBox = 6;

    // Tier IDs from seed: 1=Retail, 2=Wholesale, 3=Contractor

    public MultiUnitPricingTests()
    {
        _repo = new ProductRepository(_db.Factory);

        // Product: Roofing Nails. Base unit = Piece.
        // Stockpile: 1000 pieces.
        _nailProductId = _repo.InsertAsync(new Product
        {
            Uuid = Guid.NewGuid().ToString("N"),
            Name = "Roofing Nail 3 inch",
            BaseUnitId = UnitPiece,
            IsActive = true,
        }).GetAwaiter().GetResult();

        // Seed stock so we can verify stock math later
        using var c = _db.Factory.CreateOpen();
        c.Execute(@"
            INSERT INTO stock_movements(product_id, movement_type, qty_milli, ref_type, business_date)
            VALUES ($pid, 'OPENING', 1000000, 'OPENING', '2026-10-20')",
            new { pid = _nailProductId });
    }

    public void Dispose() => _db.Dispose();

    // ===== Selling units =====

    [Fact]
    public async Task ListProductUnits_FreshProduct_ReturnsOnlyBaseUnit()
    {
        var units = await _repo.ListProductUnitsAsync(_nailProductId);

        Assert.Single(units);
        Assert.Equal(UnitPiece, units[0].UnitId);
        Assert.Equal("Piece", units[0].UnitName);
        Assert.Equal(1000, units[0].FactorMilli);
        Assert.True(units[0].IsBase);
    }

    [Fact]
    public async Task SaveProductUnit_AddsAlternativeUnit()
    {
        // 1 kg of nails = 25 pieces (25,000 milli)
        await _repo.SaveProductUnitAsync(_nailProductId, UnitKilogram, 25_000);

        var units = await _repo.ListProductUnitsAsync(_nailProductId);

        Assert.Equal(2, units.Count);
        Assert.True(units[0].IsBase);           // Piece first (ordered IsBase DESC)
        Assert.Equal("Kilogram", units[1].UnitName);
        Assert.Equal(25_000, units[1].FactorMilli);
        Assert.False(units[1].IsBase);
    }

    [Fact]
    public async Task SaveProductUnit_AddsMultipleAlternatives()
    {
        await _repo.SaveProductUnitAsync(_nailProductId, UnitKilogram, 25_000);
        await _repo.SaveProductUnitAsync(_nailProductId, UnitBox, 120_000);

        var units = await _repo.ListProductUnitsAsync(_nailProductId);

        Assert.Equal(3, units.Count);
        Assert.Contains(units, u => u.UnitId == UnitPiece && u.IsBase);
        Assert.Contains(units, u => u.UnitId == UnitKilogram && u.FactorMilli == 25_000);
        Assert.Contains(units, u => u.UnitId == UnitBox && u.FactorMilli == 120_000);
    }

    [Fact]
    public async Task SaveProductUnit_OnBaseUnit_Throws()
    {
        // Cannot add "Piece" as an alternative to a Piece-based product
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _repo.SaveProductUnitAsync(_nailProductId, UnitPiece, 1000));

        Assert.Contains("base unit", ex.Message);
    }

    [Fact]
    public async Task SaveProductUnit_ZeroOrNegativeFactor_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _repo.SaveProductUnitAsync(_nailProductId, UnitKilogram, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _repo.SaveProductUnitAsync(_nailProductId, UnitKilogram, -1000));
    }

    [Fact]
    public async Task SaveProductUnit_UpdatesExistingFactor()
    {
        await _repo.SaveProductUnitAsync(_nailProductId, UnitKilogram, 25_000);
        await _repo.SaveProductUnitAsync(_nailProductId, UnitKilogram, 30_000);

        var units = await _repo.ListProductUnitsAsync(_nailProductId);
        var kg = units.Single(u => u.UnitId == UnitKilogram);

        Assert.Equal(30_000, kg.FactorMilli);
    }

    // ===== Get single unit =====

    [Fact]
    public async Task GetProductUnit_Base_ReturnsFactor1000()
    {
        var unit = await _repo.GetProductUnitAsync(_nailProductId, UnitPiece);

        Assert.NotNull(unit);
        Assert.Equal(1000, unit!.FactorMilli);
        Assert.True(unit.IsBase);
    }

    [Fact]
    public async Task GetProductUnit_NotConfigured_ReturnsNull()
    {
        // Kilogram not yet added to this product
        var unit = await _repo.GetProductUnitAsync(_nailProductId, UnitKilogram);

        Assert.Null(unit);
    }

    [Fact]
    public async Task GetProductUnit_ConfiguredAlternative_ReturnsFactor()
    {
        await _repo.SaveProductUnitAsync(_nailProductId, UnitBox, 120_000);

        var unit = await _repo.GetProductUnitAsync(_nailProductId, UnitBox);

        Assert.NotNull(unit);
        Assert.Equal(120_000, unit!.FactorMilli);
        Assert.False(unit.IsBase);
    }

    // ===== Multi-unit prices =====

    [Fact]
    public async Task UpsertPriceForUnit_StoresPricePerUnit()
    {
        await _repo.SaveProductUnitAsync(_nailProductId, UnitKilogram, 25_000);

        await _repo.UpsertPriceForUnitAsync(_nailProductId, tierId: 1, UnitKilogram, priceCents: 40_000);

        long? price = await _repo.GetPriceForUnitAsync(_nailProductId, tierId: 1, UnitKilogram);
        Assert.Equal(40_000, price);
    }

    [Fact]
    public async Task GetPriceForUnit_BaseUnit_WorksWithoutExplicitAlternative()
    {
        // Even without a product_units row, base unit prices work
        await _repo.UpsertPriceForUnitAsync(_nailProductId, tierId: 1, UnitPiece, priceCents: 500);

        long? price = await _repo.GetPriceForUnitAsync(_nailProductId, tierId: 1, UnitPiece);
        Assert.Equal(500, price);
    }

    [Fact]
    public async Task ListAllPrices_ShowsEveryTierUnitCombination()
    {
        await _repo.SaveProductUnitAsync(_nailProductId, UnitKilogram, 25_000);
        await _repo.SaveProductUnitAsync(_nailProductId, UnitBox, 120_000);

        // Piece / Retail / 5 KES
        await _repo.UpsertPriceForUnitAsync(_nailProductId, tierId: 1, UnitPiece, priceCents: 500);
        // Kilogram / Retail / 400 KES
        await _repo.UpsertPriceForUnitAsync(_nailProductId, tierId: 1, UnitKilogram, priceCents: 40_000);
        // Box / Retail / 500 KES
        await _repo.UpsertPriceForUnitAsync(_nailProductId, tierId: 1, UnitBox, priceCents: 50_000);
        // Box / Wholesale / 450 KES
        await _repo.UpsertPriceForUnitAsync(_nailProductId, tierId: 2, UnitBox, priceCents: 45_000);

        var prices = await _repo.ListAllPricesAsync(_nailProductId);

        Assert.Equal(4, prices.Count);
        Assert.Contains(prices, p => p.TierName == "Retail" && p.UnitName == "Piece" && p.PriceCents == 500);
        Assert.Contains(prices, p => p.TierName == "Retail" && p.UnitName == "Kilogram" && p.PriceCents == 40_000);
        Assert.Contains(prices, p => p.TierName == "Retail" && p.UnitName == "Box" && p.PriceCents == 50_000);
        Assert.Contains(prices, p => p.TierName == "Wholesale" && p.UnitName == "Box" && p.PriceCents == 45_000);
    }

    [Fact]
    public async Task UpsertPriceForUnit_RejectsNegativePrice()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _repo.UpsertPriceForUnitAsync(_nailProductId, 1, UnitPiece, -1));
    }

    // ===== Removing units =====

    [Fact]
    public async Task RemoveProductUnit_AlsoRemovesItsPrices()
    {
        await _repo.SaveProductUnitAsync(_nailProductId, UnitBox, 120_000);
        await _repo.UpsertPriceForUnitAsync(_nailProductId, 1, UnitBox, 50_000);
        await _repo.UpsertPriceForUnitAsync(_nailProductId, 1, UnitPiece, 500);  // stays

        await _repo.RemoveProductUnitAsync(_nailProductId, UnitBox);

        var units = await _repo.ListProductUnitsAsync(_nailProductId);
        Assert.Single(units);   // only Piece remains
        Assert.Equal(UnitPiece, units[0].UnitId);

        var prices = await _repo.ListAllPricesAsync(_nailProductId);
        Assert.Single(prices);   // only Piece / Retail / 500
        Assert.Equal(UnitPiece, prices[0].UnitId);
    }

    [Fact]
    public async Task RemoveProductUnit_LeavesOtherUnitsIntact()
    {
        await _repo.SaveProductUnitAsync(_nailProductId, UnitKilogram, 25_000);
        await _repo.SaveProductUnitAsync(_nailProductId, UnitBox, 120_000);

        await _repo.RemoveProductUnitAsync(_nailProductId, UnitKilogram);

        var units = await _repo.ListProductUnitsAsync(_nailProductId);
        Assert.Equal(2, units.Count);
        Assert.Contains(units, u => u.UnitId == UnitPiece);
        Assert.Contains(units, u => u.UnitId == UnitBox);
    }

    [Fact]
    public async Task RemoveProductUnit_OnBaseUnit_NoOp()
    {
        // Base unit isn't in product_units, so nothing to delete — but the
        // method should not throw either.
        await _repo.RemoveProductUnitAsync(_nailProductId, UnitPiece);

        var units = await _repo.ListProductUnitsAsync(_nailProductId);
        Assert.Single(units);   // still just the base
    }
}
