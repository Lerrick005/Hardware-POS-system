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

    // Column list for the full Product entity (used by GetById / GetByBarcode).
    private const string ProductColumns = @"
        p.id AS Id, p.uuid AS Uuid, p.sku AS Sku, p.name AS Name, p.category_id AS CategoryId,
        p.base_unit_id AS BaseUnitId, p.tax_class_id AS TaxClassId, p.track_stock AS TrackStock,
        p.stock_milli AS StockMilli, p.avg_cost_cents AS AvgCostCents, p.last_cost_cents AS LastCostCents,
        p.reorder_level_milli AS ReorderLevelMilli, p.is_active AS IsActive, p.import_batch_id AS ImportBatchId,
        p.created_at AS CreatedAt, p.updated_at AS UpdatedAt";

    // Column list for the list/search projection. Includes retail price via
    // LEFT JOINs. is_default = 1 is unique-indexed, so at most one tier row joins.
    private const string ListItemColumns = @"
        p.id                 AS Id,
        p.uuid               AS Uuid,
        p.sku                AS Sku,
        p.name               AS Name,
        p.base_unit_id       AS BaseUnitId,
        p.stock_milli        AS StockMilli,
        p.avg_cost_cents     AS AvgCostCents,
        p.reorder_level_milli AS ReorderLevelMilli,
        p.is_active          AS IsActive,
        pp.price_cents       AS RetailPriceCents";

    private const string ListItemFrom = @"
        FROM products p
        LEFT JOIN price_tiers pt ON pt.is_default = 1
        LEFT JOIN product_prices pp ON pp.product_id = p.id AND pp.tier_id = pt.id";

    public async Task<IReadOnlyList<ProductListItem>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        string sql = $"SELECT {ListItemColumns} {ListItemFrom}" +
                     (includeInactive ? "" : " WHERE p.is_active = 1") +
                     " ORDER BY p.name COLLATE NOCASE";
        var rows = await connection.QueryAsync<ProductListItem>(
            new CommandDefinition(sql, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<Product?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        return await connection.QueryFirstOrDefaultAsync<Product>(new CommandDefinition(
            $"SELECT {ProductColumns} FROM products p WHERE p.id = $id",
            new { id }, cancellationToken: ct));
    }

    public async Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        return await connection.QueryFirstOrDefaultAsync<Product>(new CommandDefinition(
            $"SELECT {ProductColumns} FROM products p " +
            "JOIN product_barcodes b ON b.product_id = p.id " +
            "WHERE b.barcode = $barcode",
            new { barcode }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ProductListItem>> SearchAsync(string term, int limit = 50, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return Array.Empty<ProductListItem>();
        }

        string[] tokens = term
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .Take(8)
            .ToArray();

        if (tokens.Length == 0)
        {
            return Array.Empty<ProductListItem>();
        }

        string[] clauses = tokens
            .Select((_, i) => $"(lower(p.name) LIKE $t{i} OR lower(coalesce(p.sku,'')) LIKE $t{i})")
            .ToArray();

        string sql = $"SELECT {ListItemColumns} {ListItemFrom} " +
                     "WHERE p.is_active = 1 AND " +
                     string.Join(" AND ", clauses) +
                     " ORDER BY p.name COLLATE NOCASE LIMIT $limit";

        DynamicParameters parameters = new();
        for (int i = 0; i < tokens.Length; i++)
        {
            parameters.Add($"t{i}", $"%{tokens[i]}%");
        }
        parameters.Add("limit", limit);

        using var connection = _factory.CreateOpen();
        var rows = await connection.QueryAsync<ProductListItem>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
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

    public async Task<int> SaveProductWithPricesAsync(
        Product product,
        string? barcode,
        IReadOnlyDictionary<int, long> pricesByTierId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(pricesByTierId);

        foreach (var entry in pricesByTierId)
        {
            if (entry.Key <= 0)
            {
                throw new ArgumentException(
                    $"Price tier id must be positive: {entry.Key}.", nameof(pricesByTierId));
            }
            if (entry.Value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pricesByTierId),
                    $"Price for tier {entry.Key} is negative: {entry.Value} cents.");
            }
        }

        using SqliteConnection connection = _factory.CreateOpen();
        using SqliteTransaction tx = connection.BeginTransaction();
        try
        {
            int productId;

            if (product.Id == 0)
            {
                long newId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(@"
                    INSERT INTO products(uuid, sku, name, category_id, base_unit_id, tax_class_id,
                                         track_stock, reorder_level_milli, is_active, import_batch_id)
                    VALUES ($Uuid, $Sku, $Name, $CategoryId, $BaseUnitId, $TaxClassId,
                            $TrackStock, $ReorderLevelMilli, $IsActive, $ImportBatchId)
                    RETURNING id",
                    product, tx, cancellationToken: ct));
                productId = (int)newId;

                if (!string.IsNullOrWhiteSpace(barcode))
                {
                    await connection.ExecuteAsync(new CommandDefinition(
                        "INSERT INTO product_barcodes(barcode, product_id, is_primary) VALUES ($Barcode, $ProductId, 1)",
                        new { Barcode = barcode, ProductId = productId },
                        tx, cancellationToken: ct));
                }
            }
            else
            {
                productId = product.Id;
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
                     WHERE id = $Id",
                    product, tx, cancellationToken: ct));
            }

            // Backwards-compatible: prices are written against the product's
            // base unit. Multi-unit price writes come through a separate method.
            int baseUnitId = product.BaseUnitId;
            foreach (var (tierId, priceCents) in pricesByTierId)
            {
                await connection.ExecuteAsync(new CommandDefinition(@"
                    INSERT INTO product_prices(product_id, tier_id, unit_id, price_cents)
                    VALUES ($productId, $tierId, $unitId, $priceCents)
                    ON CONFLICT(product_id, tier_id, unit_id)
                    DO UPDATE SET price_cents = excluded.price_cents",
                    new { productId, tierId, unitId = baseUnitId, priceCents },
                    tx, cancellationToken: ct));
            }

            tx.Commit();
            return productId;
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

        // Backwards-compatible: base-unit price lookup. Multi-unit callers
        // should use GetPriceForUnitAsync (added in the next slice).
        int? baseUnitId = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT base_unit_id FROM products WHERE id = $productId",
            new { productId }, cancellationToken: ct));

        if (baseUnitId is null) return null;

        return await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT price_cents FROM product_prices WHERE product_id = $productId AND tier_id = $tierId AND unit_id = $unitId",
            new { productId, tierId, unitId = baseUnitId.Value }, cancellationToken: ct));
    }

    public async Task UpsertPriceAsync(int productId, int tierId, long priceCents, CancellationToken ct = default)
    {
        if (priceCents < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(priceCents), "Price cannot be negative.");
        }

        using var connection = _factory.CreateOpen();

        // Backwards-compatible: writes the base-unit price. Multi-unit callers
        // should use UpsertPriceForUnitAsync (added in the next slice).
        int? baseUnitId = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT base_unit_id FROM products WHERE id = $productId",
            new { productId }, cancellationToken: ct));

        if (baseUnitId is null)
        {
            throw new InvalidOperationException($"Product {productId} not found.");
        }

        await connection.ExecuteAsync(new CommandDefinition(@"
            INSERT INTO product_prices(product_id, tier_id, unit_id, price_cents)
            VALUES ($productId, $tierId, $unitId, $priceCents)
            ON CONFLICT(product_id, tier_id, unit_id)
            DO UPDATE SET price_cents = excluded.price_cents",
            new { productId, tierId, unitId = baseUnitId.Value, priceCents }, cancellationToken: ct));
    }

    // ============ Multi-unit prices ============

    public async Task<long?> GetPriceForUnitAsync(int productId, int tierId, int unitId, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        return await connection.ExecuteScalarAsync<long?>(new CommandDefinition(@"
            SELECT price_cents FROM product_prices
             WHERE product_id = $productId AND tier_id = $tierId AND unit_id = $unitId",
            new { productId, tierId, unitId }, cancellationToken: ct));
    }

    public async Task UpsertPriceForUnitAsync(int productId, int tierId, int unitId, long priceCents, CancellationToken ct = default)
    {
        if (priceCents < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(priceCents), "Price cannot be negative.");
        }

        using var connection = _factory.CreateOpen();
        await connection.ExecuteAsync(new CommandDefinition(@"
            INSERT INTO product_prices(product_id, tier_id, unit_id, price_cents)
            VALUES ($productId, $tierId, $unitId, $priceCents)
            ON CONFLICT(product_id, tier_id, unit_id)
            DO UPDATE SET price_cents = excluded.price_cents",
            new { productId, tierId, unitId, priceCents }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ProductPriceInfo>> ListAllPricesAsync(int productId, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        var rows = await connection.QueryAsync<ProductPriceInfo>(new CommandDefinition(@"
            SELECT pp.tier_id     AS TierId,
                   pt.name        AS TierName,
                   pp.unit_id     AS UnitId,
                   u.name         AS UnitName,
                   pp.price_cents AS PriceCents
              FROM product_prices pp
              JOIN price_tiers pt ON pt.id = pp.tier_id
              JOIN units u ON u.id = pp.unit_id
             WHERE pp.product_id = $productId
             ORDER BY pt.name, u.name",
            new { productId }, cancellationToken: ct));
        return rows.AsList();
    }

    // ============ Selling units ============

    public async Task<IReadOnlyList<ProductUnitInfo>> ListProductUnitsAsync(int productId, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();

        // Base unit comes first (IsBase = true). Then every alternative unit
        // from product_units with its factor. If the product doesn't exist,
        // returns empty.
        var rows = await connection.QueryAsync<ProductUnitInfo>(new CommandDefinition(@"
            SELECT u.id           AS UnitId,
                   u.name         AS UnitName,
                   u.symbol       AS UnitSymbol,
                   1000           AS FactorMilli,
                   1              AS IsBase
              FROM products p
              JOIN units u ON u.id = p.base_unit_id
             WHERE p.id = $productId

            UNION ALL

            SELECT u.id              AS UnitId,
                   u.name            AS UnitName,
                   u.symbol          AS UnitSymbol,
                   pu.factor_milli   AS FactorMilli,
                   0                 AS IsBase
              FROM product_units pu
              JOIN units u ON u.id = pu.unit_id
             WHERE pu.product_id = $productId

             ORDER BY IsBase DESC, UnitName COLLATE NOCASE",
            new { productId }, cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<ProductUnitInfo?> GetProductUnitAsync(int productId, int unitId, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();

        return await connection.QueryFirstOrDefaultAsync<ProductUnitInfo>(new CommandDefinition(@"
            SELECT u.id           AS UnitId,
                   u.name         AS UnitName,
                   u.symbol       AS UnitSymbol,
                   CASE WHEN u.id = p.base_unit_id THEN 1000 ELSE pu.factor_milli END AS FactorMilli,
                   CASE WHEN u.id = p.base_unit_id THEN 1 ELSE 0 END AS IsBase
              FROM products p
              JOIN units u ON u.id = $unitId
              LEFT JOIN product_units pu ON pu.product_id = p.id AND pu.unit_id = u.id
             WHERE p.id = $productId
               AND (u.id = p.base_unit_id OR pu.id IS NOT NULL)",
            new { productId, unitId }, cancellationToken: ct));
    }

    public async Task SaveProductUnitAsync(int productId, int unitId, long factorMilli, CancellationToken ct = default)
    {
        if (factorMilli <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(factorMilli), "Factor must be greater than zero.");
        }

        using var connection = _factory.CreateOpen();

        // Refuse to add a "unit" equal to the base unit — that's already
        // implicitly available and would confuse the picker.
        int? baseUnitId = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT base_unit_id FROM products WHERE id = $productId",
            new { productId }, cancellationToken: ct));

        if (baseUnitId is null)
        {
            throw new InvalidOperationException($"Product {productId} not found.");
        }

        if (baseUnitId.Value == unitId)
        {
            throw new InvalidOperationException("Cannot add a selling unit that is the product's base unit.");
        }

        await connection.ExecuteAsync(new CommandDefinition(@"
            INSERT INTO product_units(product_id, unit_id, factor_milli)
            VALUES ($productId, $unitId, $factorMilli)
            ON CONFLICT(product_id, unit_id)
            DO UPDATE SET factor_milli = excluded.factor_milli",
            new { productId, unitId, factorMilli }, cancellationToken: ct));
    }

    public async Task RemoveProductUnitAsync(int productId, int unitId, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();

        // Also remove any prices for this (product, unit) combination.
        // Without this, orphan price rows would linger and confuse reports.
        using Microsoft.Data.Sqlite.SqliteTransaction tx = connection.BeginTransaction();
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM product_prices WHERE product_id = $productId AND unit_id = $unitId",
                new { productId, unitId }, tx, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM product_units WHERE product_id = $productId AND unit_id = $unitId",
                new { productId, unitId }, tx, cancellationToken: ct));

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}


