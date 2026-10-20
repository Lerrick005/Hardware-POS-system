using Dapper;
using Microsoft.Data.Sqlite;
using Pos.Domain.Catalog;
using Pos.Infrastructure.Catalog;

namespace Pos.Infrastructure.Tests;

public sealed class ProductRepositoryTests : IDisposable
{
    private static readonly string PerfLogPath =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pos-perf.txt");

    private readonly TestDatabase _db = new();
    private readonly ProductRepository _repo;

    public ProductRepositoryTests() => _repo = new ProductRepository(_db.Factory);

    public void Dispose() => _db.Dispose();

    private static Product NewProduct(string name, string? sku = null) => new()
    {
        Uuid = Guid.NewGuid().ToString("N"),
        Name = name,
        Sku = sku,
        BaseUnitId = 1,
        TrackStock = true,
        IsActive = true,
    };

    [Fact]
    public async Task Insert_AssignsId_AndGetReturnsIt()
    {
        int id = await _repo.InsertAsync(NewProduct("Cement 50kg"));

        Product? loaded = await _repo.GetByIdAsync(id);

        Assert.NotNull(loaded);
        Assert.Equal("Cement 50kg", loaded!.Name);
        Assert.True(loaded.IsActive);
    }

    [Fact]
    public async Task Search_MatchesAllTokensInAnyOrder()
    {
        await _repo.InsertAsync(NewProduct("Simba Cement 50kg"));
        await _repo.InsertAsync(NewProduct("Bamburi Cement 50kg"));
        await _repo.InsertAsync(NewProduct("Roofing Nail 3 inch"));

        var cement = await _repo.SearchAsync("cement 50kg");
        var nails = await _repo.SearchAsync("nail 3");
        var reversed = await _repo.SearchAsync("3 nail");

        Assert.Equal(2, cement.Count);
        Assert.Single(nails);
        Assert.Single(reversed);
        Assert.Equal("Roofing Nail 3 inch", nails[0].Name);
    }

    [Fact]
    public async Task Search_ExcludesArchived()
    {
        int id = await _repo.InsertAsync(NewProduct("PVC Pipe 1 inch"));
        await _repo.ArchiveAsync(id);

        var results = await _repo.SearchAsync("pvc");

        Assert.Empty(results);
        Assert.NotNull(await _repo.GetByIdAsync(id));
        Assert.False((await _repo.GetByIdAsync(id))!.IsActive);
    }

    [Fact]
    public async Task Barcode_RoundTrips()
    {
        int id = await _repo.InsertAsync(NewProduct("Hammer 500g"));
        await _repo.AddBarcodeAsync(new ProductBarcode { Barcode = "HM-500", ProductId = id, IsPrimary = true });

        Assert.True(await _repo.BarcodeExistsAsync("HM-500"));
        Assert.False(await _repo.BarcodeExistsAsync("HM-999"));

        Product? byBarcode = await _repo.GetByBarcodeAsync("HM-500");
        Assert.NotNull(byBarcode);
        Assert.Equal("Hammer 500g", byBarcode!.Name);
    }

    [Fact]
    public async Task List_ExcludesInactiveByDefault()
    {
        int id = await _repo.InsertAsync(NewProduct("Wire Mesh 1m"));
        await _repo.InsertAsync(NewProduct("Wire Mesh 2m"));
        await _repo.ArchiveAsync(id);

        var active = await _repo.ListAsync(includeInactive: false);
        var all = await _repo.ListAsync(includeInactive: true);

        Assert.Single(active);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task Update_ChangesNameAndSku()
    {
        int id = await _repo.InsertAsync(NewProduct("Old name"));
        Product p = (await _repo.GetByIdAsync(id))!;
        p.Name = "New name";
        p.Sku = "SKU-1";
        await _repo.UpdateAsync(p);

        Product reloaded = (await _repo.GetByIdAsync(id))!;
        Assert.Equal("New name", reloaded.Name);
        Assert.Equal("SKU-1", reloaded.Sku);
    }

    [Fact]
    public async Task Search_Performance_20kProducts_Under100ms()
    {
        using (SqliteConnection bulk = _db.Factory.CreateOpen())
        using (SqliteTransaction tx = bulk.BeginTransaction())
        {
            using SqliteCommand cmd = bulk.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i+1 FROM n WHERE i < 20000)
                INSERT INTO products(uuid, name, sku, base_unit_id, track_stock, is_active)
                SELECT lower(hex(randomblob(16))),
                       'Product ' || printf('%05d', i) || CASE WHEN i % 100 = 0 THEN ' cement' ELSE '' END,
                       CASE WHEN i % 50 = 0 THEN 'SKU-' || i ELSE NULL END,
                       1, 1, 1
                FROM n;";
            cmd.ExecuteNonQuery();
            tx.Commit();
        }

        // Warm-up: the very first query on a cold SQLite connection after
        // bulk insert pays cold-cache cost. Real cashiers hit warm cache
        // hundreds of times per day. Warm the plan + page cache before timing.
        await _repo.SearchAsync("cement", limit: 500);
        await _repo.SearchAsync("product 12", limit: 500);

        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        var first = await _repo.SearchAsync("cement", limit: 500);
        sw.Stop();
        long firstMs = sw.ElapsedMilliseconds;

        sw.Restart();
        var second = await _repo.SearchAsync("product 12", limit: 500);
        sw.Stop();
        long secondMs = sw.ElapsedMilliseconds;

        File.AppendAllText(PerfLogPath,
            "Search(cement) on 20k: " + firstMs + " ms" + Environment.NewLine);
        File.AppendAllText(PerfLogPath,
            "Search(product 12) on 20k: " + secondMs + " ms" + Environment.NewLine);

        Assert.Equal(200, first.Count);
        Assert.NotEmpty(second);
        Assert.True(firstMs < 100, "First search took " + firstMs + " ms (target < 100)");
        Assert.True(secondMs < 100, "Second search took " + secondMs + " ms (target < 100)");
    }
}

