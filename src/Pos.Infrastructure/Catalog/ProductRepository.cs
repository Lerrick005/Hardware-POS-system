using Dapper;
using Microsoft.Data.Sqlite;
using Pos.Application.Catalog;
using Pos.Domain.Catalog;
using Pos.Infrastructure.Data;

namespace Pos.Infrastructure.Catalog;

public sealed class ProductRepository : IProductRepository
{
    private readonly DbConnectionFactory _factory;

    public ProductRepository(DbConnectionFactory factory) => _factory = factory;

    private const string SelectColumns = @"
        id AS Id, uuid AS Uuid, sku AS Sku, name AS Name, category_id AS CategoryId,
        base_unit_id AS BaseUnitId, tax_class_id AS TaxClassId, track_stock AS TrackStock,
        stock_milli AS StockMilli, avg_cost_cents AS AvgCostCents, last_cost_cents AS LastCostCents,
        reorder_level_milli AS ReorderLevelMilli, is_active AS IsActive, import_batch_id AS ImportBatchId,
        created_at AS CreatedAt, updated_at AS UpdatedAt";

    private const string SelectColumnsP = @"
        p.id AS Id, p.uuid AS Uuid, p.sku AS Sku, p.name AS Name, p.category_id AS CategoryId,
        p.base_unit_id AS BaseUnitId, p.tax_class_id AS TaxClassId, p.track_stock AS TrackStock,
        p.stock_milli AS StockMilli, p.avg_cost_cents AS AvgCostCents, p.last_cost_cents AS LastCostCents,
        p.reorder_level_milli AS ReorderLevelMilli, p.is_active AS IsActive, p.import_batch_id AS ImportBatchId,
        p.created_at AS CreatedAt, p.updated_at AS UpdatedAt";

    public async Task<IReadOnlyList<Product>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        string sql = $"SELECT {SelectColumns} FROM products" +
                     (includeInactive ? "" : " WHERE is_active = 1") +
                     " ORDER BY name COLLATE NOCASE";
        var rows = await connection.QueryAsync<Product>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<Product?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        return await connection.QueryFirstOrDefaultAsync<Product>(new CommandDefinition(
            $"SELECT {SelectColumns} FROM products WHERE id = $id",
            new { id }, cancellationToken: ct));
    }

    public async Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        return await connection.QueryFirstOrDefaultAsync<Product>(new CommandDefinition(
            $"SELECT {SelectColumnsP} FROM products p " +
            "JOIN product_barcodes b ON b.product_id = p.id " +
            "WHERE b.barcode = $barcode",
            new { barcode }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Product>> SearchAsync(string term, int limit = 50, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return Array.Empty<Product>();
        }

        string[] tokens = term
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .Take(8)
            .ToArray();

        if (tokens.Length == 0)
        {
            return Array.Empty<Product>();
        }

        string[] clauses = tokens
            .Select((_, i) => $"(lower(name) LIKE $t{i} OR lower(coalesce(sku,'')) LIKE $t{i})")
            .ToArray();

        string sql = $"SELECT {SelectColumns} FROM products WHERE is_active = 1 AND " +
                     string.Join(" AND ", clauses) +
                     " ORDER BY name COLLATE NOCASE LIMIT $limit";

        DynamicParameters parameters = new();
        for (int i = 0; i < tokens.Length; i++)
        {
            parameters.Add($"t{i}", $"%{tokens[i]}%");
        }
        parameters.Add("limit", limit);

        using var connection = _factory.CreateOpen();
        var rows = await connection.QueryAsync<Product>(new CommandDefinition(sql, parameters, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<int> InsertAsync(Product product, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        long id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(@"
            INSERT INTO products(uuid, sku, name, category_id, base_unit_id, tax_class_id,
                                 track_stock, reorder_level_milli, is_active, import_batch_id)
            VALUES ($Uuid, $Sku, $Name, $CategoryId, $BaseUnitId, $TaxClassId,
                    $TrackStock, $ReorderLevelMilli, $IsActive, $ImportBatchId)
            RETURNING id", product, cancellationToken: ct));
        return (int)id;
    }

    public async Task<int> InsertWithBarcodeAsync(Product product, string? barcode, CancellationToken ct = default)
    {
        using SqliteConnection connection = _factory.CreateOpen();
        using SqliteTransaction tx = connection.BeginTransaction();
        try
        {
            long id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(@"
                INSERT INTO products(uuid, sku, name, category_id, base_unit_id, tax_class_id,
                                     track_stock, reorder_level_milli, is_active, import_batch_id)
                VALUES ($Uuid, $Sku, $Name, $CategoryId, $BaseUnitId, $TaxClassId,
                        $TrackStock, $ReorderLevelMilli, $IsActive, $ImportBatchId)
                RETURNING id",
                product, tx, cancellationToken: ct));

            if (!string.IsNullOrWhiteSpace(barcode))
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO product_barcodes(barcode, product_id, is_primary) VALUES ($Barcode, $ProductId, 1)",
                    new { Barcode = barcode, ProductId = (int)id },
                    tx, cancellationToken: ct));
            }

            tx.Commit();
            return (int)id;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task UpdateAsync(Product product, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        await connection.ExecuteAsync(new CommandDefinition(@"
            UPDATE products
               SET sku                 = $Sku,
                   name                = $Name,
                   category_id         = $CategoryId,
                   base_unit_id        = $BaseUnitId,
                   tax_class_id        = $TaxClassId,
                   track_stock         = $TrackStock,
                   reorder_level_milli = $ReorderLevelMilli,
                   is_active           = $IsActive,
                   updated_at          = strftime('%Y-%m-%dT%H:%M:%SZ','now')
             WHERE id = $Id", product, cancellationToken: ct));
    }

    public async Task ArchiveAsync(int id, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE products SET is_active = 0, updated_at = strftime('%Y-%m-%dT%H:%M:%SZ','now') WHERE id = $id",
            new { id }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ProductBarcode>> ListBarcodesAsync(int productId, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        var rows = await connection.QueryAsync<ProductBarcode>(new CommandDefinition(
            "SELECT barcode AS Barcode, product_id AS ProductId, is_primary AS IsPrimary FROM product_barcodes WHERE product_id = $productId",
            new { productId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task AddBarcodeAsync(ProductBarcode barcode, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO product_barcodes(barcode, product_id, is_primary) VALUES ($Barcode, $ProductId, $IsPrimary)",
            barcode, cancellationToken: ct));
    }

    public async Task<bool> BarcodeExistsAsync(string barcode, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        long count = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(1) FROM product_barcodes WHERE barcode = $barcode",
            new { barcode }, cancellationToken: ct));
        return count > 0;
    }

    public async Task<long?> GetPriceAsync(int productId, int tierId, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        return await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT price_cents FROM product_prices WHERE product_id = $productId AND tier_id = $tierId",
            new { productId, tierId }, cancellationToken: ct));
    }

    public async Task UpsertPriceAsync(int productId, int tierId, long priceCents, CancellationToken ct = default)
    {
        if (priceCents < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(priceCents), "Price cannot be negative.");
        }

        using var connection = _factory.CreateOpen();
        await connection.ExecuteAsync(new CommandDefinition(@"
            INSERT INTO product_prices(product_id, tier_id, price_cents)
            VALUES ($productId, $tierId, $priceCents)
            ON CONFLICT(product_id, tier_id)
            DO UPDATE SET price_cents = excluded.price_cents",
            new { productId, tierId, priceCents }, cancellationToken: ct));
    }
}
