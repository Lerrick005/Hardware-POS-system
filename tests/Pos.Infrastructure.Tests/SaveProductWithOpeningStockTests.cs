using Dapper;
using Pos.Application.Catalog;
using Pos.Domain.Catalog;
using Pos.Infrastructure.Catalog;

namespace Pos.Infrastructure.Tests;

public sealed class SaveProductWithOpeningStockTests : IDisposable
{
    private const int UnitBag = 5;
    private const int UnitKilogram = 2;

    private readonly TestDatabase _db = new();
    private readonly ProductRepository _repo;
    private readonly int _userId;

    public SaveProductWithOpeningStockTests()
    {
        _repo = new ProductRepository(_db.Factory);

        using var c = _db.Factory.CreateOpen();
        _userId = (int)c.ExecuteScalar<long>(
            "INSERT INTO users(name, role, pin_hash, is_active) VALUES ('Admin', 'ADMIN', 'PIN_NOT_SET', 1) RETURNING id");
    }

    public void Dispose() => _db.Dispose();

    private static Product NewCement() => new()
    {
        Uuid = Guid.NewGuid().ToString("N"),
        Name = "Simba Cement 50kg",
        BaseUnitId = UnitBag,
        IsActive = true,
    };

    [Fact]
    public async Task Save_WithOpeningStock_SetsStockAndCreatesOpeningMovement()
    {
        int id = await _repo.SaveProductWithAllPricesAndOpeningStockAsync(
            NewCement(),
            barcode: null,
            baseTierPrices: new Dictionary<int, long> { [1] = 65_000 },
            alternativeUnits: Array.Empty<ProductUnitSave>(),
            openingStockMilli: 50_000,   // 50 bags
            openingCostCents: 55_000,    // 550 per bag
            businessDate: "2026-10-21",
            userId: _userId);

        Product? product = await _repo.GetByIdAsync(id);
        Assert.NotNull(product);
        Assert.Equal(50_000, product!.StockMilli);
        Assert.Equal(55_000, product.AvgCostCents);

        using var c = _db.Factory.CreateOpen();
        var movement = c.QuerySingle<(string MovementType, long QtyMilli, int UserId)>(
            "SELECT movement_type, qty_milli, user_id FROM stock_movements WHERE product_id = $id",
            new { id });
        Assert.Equal("OPENING", movement.MovementType);
        Assert.Equal(50_000, movement.QtyMilli);
        Assert.Equal(_userId, movement.UserId);
    }

    [Fact]
    public async Task Save_WithZeroOpeningStock_NoMovement_StockIsZero()
    {
        int id = await _repo.SaveProductWithAllPricesAndOpeningStockAsync(
            NewCement(),
            barcode: null,
            baseTierPrices: new Dictionary<int, long> { [1] = 65_000 },
            alternativeUnits: Array.Empty<ProductUnitSave>(),
            openingStockMilli: 0,
            openingCostCents: 0,
            businessDate: null,
            userId: null);

        Product? product = await _repo.GetByIdAsync(id);
        Assert.Equal(0, product!.StockMilli);
        Assert.Equal(0, product.AvgCostCents);

        using var c = _db.Factory.CreateOpen();
        int movements = c.ExecuteScalar<int>(
            "SELECT COUNT(1) FROM stock_movements WHERE product_id = $id", new { id });
        Assert.Equal(0, movements);
    }

    [Fact]
    public async Task Save_WithOpeningStockAndCost_WithoutCost_AvgCostStaysZero()
    {
        int id = await _repo.SaveProductWithAllPricesAndOpeningStockAsync(
            NewCement(),
            barcode: null,
            baseTierPrices: new Dictionary<int, long> { [1] = 65_000 },
            alternativeUnits: Array.Empty<ProductUnitSave>(),
            openingStockMilli: 30_000,
            openingCostCents: 0,
            businessDate: "2026-10-21",
            userId: _userId);

        Product? product = await _repo.GetByIdAsync(id);
        Assert.Equal(30_000, product!.StockMilli);
        Assert.Equal(0, product.AvgCostCents);
    }

    [Fact]
    public async Task Save_WithOpeningStockAndAlternativeUnits_BothPersist()
    {
        int id = await _repo.SaveProductWithAllPricesAndOpeningStockAsync(
            new()
            {
                Uuid = Guid.NewGuid().ToString("N"),
                Name = "Roofing Nails",
                BaseUnitId = 1,   // Piece
                IsActive = true,
            },
            barcode: "NAIL-3IN",
            baseTierPrices: new Dictionary<int, long> { [1] = 500 },
            alternativeUnits: new[]
            {
                new ProductUnitSave(UnitKilogram, 25_000, new[]
                {
                    new ProductUnitPriceSave(1, 40_000),
                }),
            },
            openingStockMilli: 1_000_000,   // 1000 pieces
            openingCostCents: 200,
            businessDate: "2026-10-21",
            userId: _userId);

        Product? product = await _repo.GetByIdAsync(id);
        Assert.Equal(1_000_000, product!.StockMilli);
        Assert.Equal(200, product.AvgCostCents);

        var units = await _repo.ListProductUnitsAsync(id);
        Assert.Equal(2, units.Count);   // base + kilogram

        long? kgPrice = await _repo.GetPriceForUnitAsync(id, 1, UnitKilogram);
        Assert.Equal(40_000, kgPrice);
    }

    [Fact]
    public async Task Save_WithOpeningStockButNoDate_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _repo.SaveProductWithAllPricesAndOpeningStockAsync(
                NewCement(),
                barcode: null,
                baseTierPrices: new Dictionary<int, long> { [1] = 65_000 },
                alternativeUnits: Array.Empty<ProductUnitSave>(),
                openingStockMilli: 50_000,
                openingCostCents: 55_000,
                businessDate: null,
                userId: _userId));
    }
}
