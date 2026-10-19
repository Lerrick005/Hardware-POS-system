using Dapper;
using Microsoft.Data.Sqlite;
using Pos.Domain.Catalog;
using Pos.Infrastructure.Catalog;

namespace Pos.Infrastructure.Tests;

public sealed class ProductRepositoryTransactionTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly ProductRepository _repo;

    public ProductRepositoryTransactionTests() => _repo = new ProductRepository(_db.Factory);

    public void Dispose() => _db.Dispose();

    private static Product NewProduct(string name) => new()
    {
        Uuid = Guid.NewGuid().ToString("N"),
        Name = name,
        BaseUnitId = 1,
        TrackStock = true,
        IsActive = true,
    };

    private async Task<int> CountProductsAsync()
    {
        using SqliteConnection c = _db.Factory.CreateOpen();
        return (int)await c.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM products");
    }

    private async Task<int> CountBarcodesAsync()
    {
        using SqliteConnection c = _db.Factory.CreateOpen();
        return (int)await c.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM product_barcodes");
    }

    [Fact]
    public async Task InsertWithBarcode_NoBarcode_CreatesProductOnly()
    {
        int id = await _repo.InsertWithBarcodeAsync(NewProduct("Cement 50kg"), barcode: null);

        Assert.True(id > 0);
        Assert.Equal(1, await CountProductsAsync());
        Assert.Equal(0, await CountBarcodesAsync());
    }

    [Fact]
    public async Task InsertWithBarcode_WithBarcode_CreatesBoth()
    {
        int id = await _repo.InsertWithBarcodeAsync(NewProduct("Cement 50kg"), barcode: "CMT-50");

        Assert.True(id > 0);
        Assert.Equal(1, await CountProductsAsync());
        Assert.Equal(1, await CountBarcodesAsync());
        Assert.True(await _repo.BarcodeExistsAsync("CMT-50"));
    }

    [Fact]
    public async Task InsertWithBarcode_DuplicateBarcode_NoOrphanProduct()
    {
        // Seed the first product+barcode.
        await _repo.InsertWithBarcodeAsync(NewProduct("First product"), barcode: "DUP-1");

        int productsBefore = await CountProductsAsync();
        int barcodesBefore = await CountBarcodesAsync();

        // Attempt a second insert that reuses the barcode. It must throw,
        // and neither product nor barcode may be persisted.
        await Assert.ThrowsAsync<SqliteException>(() =>
            _repo.InsertWithBarcodeAsync(NewProduct("Orphan candidate"), barcode: "DUP-1"));

        Assert.Equal(productsBefore, await CountProductsAsync());
        Assert.Equal(barcodesBefore, await CountBarcodesAsync());
    }

    [Fact]
    public async Task InsertWithBarcode_DuplicateUuid_NoOrphanProduct()
    {
        string sharedUuid = Guid.NewGuid().ToString("N");

        Product first = NewProduct("First product");
        first.Uuid = sharedUuid;
        await _repo.InsertWithBarcodeAsync(first, barcode: null);

        Product second = NewProduct("Orphan candidate");
        second.Uuid = sharedUuid;   // violates UNIQUE(uuid)

        int productsBefore = await CountProductsAsync();

        await Assert.ThrowsAsync<SqliteException>(() =>
            _repo.InsertWithBarcodeAsync(second, barcode: "UNIQUE-UUID"));

        Assert.Equal(productsBefore, await CountProductsAsync());
        // Barcode must NOT have been written for a product that rolled back.
        Assert.False(await _repo.BarcodeExistsAsync("UNIQUE-UUID"));
    }
}
