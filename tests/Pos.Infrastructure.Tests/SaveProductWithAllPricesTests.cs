using Dapper;
using Pos.Application.Catalog;
using Pos.Domain.Catalog;
using Pos.Infrastructure.Catalog;

namespace Pos.Infrastructure.Tests;

public sealed class SaveProductWithAllPricesTests : IDisposable
{
    private const int UnitPiece = 1;
    private const int UnitKilogram = 2;
    private const int UnitBox = 6;

    private readonly TestDatabase _db = new();
    private readonly ProductRepository _repo;

    public SaveProductWithAllPricesTests()
    {
        _repo = new ProductRepository(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    private static Product NewNails() => new()
    {
        Uuid = Guid.NewGuid().ToString("N"),
        Name = "Roofing Nails",
        BaseUnitId = UnitPiece,
        IsActive = true,
    };

    [Fact]
    public async Task Save_Insert_ProductAndUnitsAndPrices_AllInOne()
    {
        int id = await _repo.SaveProductWithAllPricesAsync(
            NewNails(),
            barcode: "NAIL-3IN",
            baseTierPrices: new Dictionary<int, long> { [1] = 500, [2] = 450 },
            alternativeUnits: new[]
            {
                new ProductUnitSave(UnitKilogram, 25_000, new[]
                {
                    new ProductUnitPriceSave(1, 40_000),
                    new ProductUnitPriceSave(2, 36_000),
                }),
                new ProductUnitSave(UnitBox, 120_000, new[]
                {
                    new ProductUnitPriceSave(1, 50_000),
                    new ProductUnitPriceSave(2, 45_000),
                }),
            });

        Assert.True(id > 0);

        var units = await _repo.ListProductUnitsAsync(id);
        Assert.Equal(3, units.Count);

        var prices = await _repo.ListAllPricesAsync(id);
        Assert.Equal(6, prices.Count);

        Assert.Equal(50_000, await _repo.GetPriceForUnitAsync(id, 1, UnitBox));
        Assert.Equal(45_000, await _repo.GetPriceForUnitAsync(id, 2, UnitBox));
        Assert.Equal(500, await _repo.GetPriceForUnitAsync(id, 1, UnitPiece));
        Assert.Equal(450, await _repo.GetPriceForUnitAsync(id, 2, UnitPiece));
    }

    [Fact]
    public async Task Save_Update_ReplacesUnits_DeletesRemovedOnes()
    {
        int id = await _repo.SaveProductWithAllPricesAsync(
            NewNails(), null,
            new Dictionary<int, long> { [1] = 500 },
            new[]
            {
                new ProductUnitSave(UnitKilogram, 25_000, new[] { new ProductUnitPriceSave(1, 40_000) }),
                new ProductUnitSave(UnitBox, 120_000, new[] { new ProductUnitPriceSave(1, 50_000) }),
            });

        Product product = (await _repo.GetByIdAsync(id))!;
        await _repo.SaveProductWithAllPricesAsync(
            product, null,
            new Dictionary<int, long> { [1] = 500 },
            new[]
            {
                new ProductUnitSave(UnitKilogram, 25_000, new[] { new ProductUnitPriceSave(1, 42_000) }),
            });

        var units = await _repo.ListProductUnitsAsync(id);
        Assert.Equal(2, units.Count);
        Assert.DoesNotContain(units, u => u.UnitId == UnitBox);

        var prices = await _repo.ListAllPricesAsync(id);
        Assert.DoesNotContain(prices, p => p.UnitId == UnitBox);
        Assert.Equal(42_000, await _repo.GetPriceForUnitAsync(id, 1, UnitKilogram));
    }

    [Fact]
    public async Task Save_Update_EmptyAlternatives_DeletesAll()
    {
        int id = await _repo.SaveProductWithAllPricesAsync(
            NewNails(), null,
            new Dictionary<int, long> { [1] = 500 },
            new[]
            {
                new ProductUnitSave(UnitKilogram, 25_000, new[] { new ProductUnitPriceSave(1, 40_000) }),
                new ProductUnitSave(UnitBox, 120_000, new[] { new ProductUnitPriceSave(1, 50_000) }),
            });

        Product product = (await _repo.GetByIdAsync(id))!;
        await _repo.SaveProductWithAllPricesAsync(
            product, null,
            new Dictionary<int, long> { [1] = 500 },
            Array.Empty<ProductUnitSave>());

        var units = await _repo.ListProductUnitsAsync(id);
        Assert.Single(units);
        Assert.True(units[0].IsBase);

        var prices = await _repo.ListAllPricesAsync(id);
        Assert.Single(prices);
        Assert.Equal(UnitPiece, prices[0].UnitId);
    }

    [Fact]
    public async Task Save_RejectsBaseUnitAsAlternative()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _repo.SaveProductWithAllPricesAsync(
                NewNails(), null,
                new Dictionary<int, long> { [1] = 500 },
                new[] { new ProductUnitSave(UnitPiece, 1000, Array.Empty<ProductUnitPriceSave>()) }));

        Assert.Contains("base unit", ex.Message);
    }

    [Fact]
    public async Task Save_RejectsDuplicateUnits()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _repo.SaveProductWithAllPricesAsync(
                NewNails(), null,
                new Dictionary<int, long> { [1] = 500 },
                new[]
                {
                    new ProductUnitSave(UnitKilogram, 25_000, new[] { new ProductUnitPriceSave(1, 40_000) }),
                    new ProductUnitSave(UnitKilogram, 30_000, new[] { new ProductUnitPriceSave(1, 42_000) }),
                }));

        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact]
    public async Task Save_RejectsZeroFactor()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _repo.SaveProductWithAllPricesAsync(
                NewNails(), null,
                new Dictionary<int, long> { [1] = 500 },
                new[] { new ProductUnitSave(UnitKilogram, 0, Array.Empty<ProductUnitPriceSave>()) }));
    }

    [Fact]
    public async Task Save_Atomic_RollbackOnInvalidUnitId()
    {
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _repo.SaveProductWithAllPricesAsync(
                NewNails(), null,
                new Dictionary<int, long> { [1] = 500 },
                new[] { new ProductUnitSave(99999, 25_000, new[] { new ProductUnitPriceSave(1, 40_000) }) }));

        using var c = _db.Factory.CreateOpen();
        int productCount = c.ExecuteScalar<int>("SELECT COUNT(1) FROM products WHERE name = 'Roofing Nails'");
        int orphanPrices = c.ExecuteScalar<int>("SELECT COUNT(1) FROM product_prices WHERE product_id NOT IN (SELECT id FROM products)");

        Assert.Equal(0, productCount);
        Assert.Equal(0, orphanPrices);
    }
}
