using Pos.Domain.Catalog;

namespace Pos.Application.Catalog;

/// <summary>
/// One row of the product list. Includes the retail price (joined from
/// product_prices at the base unit), so the grid can show what the shop
/// sells it for without a second query per row. Class, not record — ADR-0011.
/// </summary>
public sealed class ProductListItem
{
    public int Id { get; set; }
    public string Uuid { get; set; } = "";
    public string? Sku { get; set; }
    public string Name { get; set; } = "";
    public int BaseUnitId { get; set; }
    public long StockMilli { get; set; }
    public long AvgCostCents { get; set; }
    public long ReorderLevelMilli { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Retail price in cents at the base unit, or null if not set.</summary>
    public long? RetailPriceCents { get; set; }
}

/// <summary>
/// A selling unit for a product, ready for UI. Includes the base unit as the
/// first row (factor_milli = 1000, IsBase = true) plus every alternative from
/// product_units. Class, not record — ADR-0011.
/// </summary>
public sealed class ProductUnitInfo
{
    public int UnitId { get; set; }
    public string UnitName { get; set; } = "";
    public string UnitSymbol { get; set; } = "";

    /// <summary>Base-unit quantity per 1 of this selling unit, in thousandths.</summary>
    public long FactorMilli { get; set; }

    /// <summary>True only for the product's base unit row.</summary>
    public bool IsBase { get; set; }
}

/// <summary>
/// One price row: a (tier, unit) combination for a product. Class, not record.
/// </summary>
public sealed class ProductPriceInfo
{
    public int TierId { get; set; }
    public string TierName { get; set; } = "";
    public int UnitId { get; set; }
    public string UnitName { get; set; } = "";
    public long PriceCents { get; set; }
}

public interface IProductRepository
{
    // ============ Product CRUD ============

    Task<IReadOnlyList<ProductListItem>> ListAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<Product?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken ct = default);
    Task<IReadOnlyList<ProductListItem>> SearchAsync(string term, int limit = 50, CancellationToken ct = default);
    Task<int> InsertAsync(Product product, CancellationToken ct = default);
    Task UpdateAsync(Product product, CancellationToken ct = default);
    Task ArchiveAsync(int id, CancellationToken ct = default);

    // ============ Barcodes ============

    Task<IReadOnlyList<ProductBarcode>> ListBarcodesAsync(int productId, CancellationToken ct = default);
    Task AddBarcodeAsync(ProductBarcode barcode, CancellationToken ct = default);
    Task<bool> BarcodeExistsAsync(string barcode, CancellationToken ct = default);
    Task<int> InsertWithBarcodeAsync(Product product, string? barcode, CancellationToken ct = default);

    // ============ Prices (base unit only — backward-compatible) ============
    // Use the *ForUnit methods below when you need multi-unit pricing.

    Task<long?> GetPriceAsync(int productId, int tierId, CancellationToken ct = default);
    Task UpsertPriceAsync(int productId, int tierId, long priceCents, CancellationToken ct = default);

    /// <summary>
    /// Creates or updates a product and its base-unit prices in one transaction.
    /// For multi-unit prices, call UpsertPriceForUnitAsync separately after this.
    /// </summary>
    Task<int> SaveProductWithPricesAsync(
        Product product,
        string? barcode,
        IReadOnlyDictionary<int, long> pricesByTierId,
        CancellationToken ct = default);

    // ============ Multi-unit prices ============

    Task<long?> GetPriceForUnitAsync(int productId, int tierId, int unitId, CancellationToken ct = default);
    Task UpsertPriceForUnitAsync(int productId, int tierId, int unitId, long priceCents, CancellationToken ct = default);

    /// <summary>All prices set for a product across every tier and unit.</summary>
    Task<IReadOnlyList<ProductPriceInfo>> ListAllPricesAsync(int productId, CancellationToken ct = default);

    // ============ Selling units ============

    /// <summary>
    /// All selling units for a product: the base unit (first) plus every
    /// alternative unit with a factor set. Used to populate the unit picker.
    /// </summary>
    Task<IReadOnlyList<ProductUnitInfo>> ListProductUnitsAsync(int productId, CancellationToken ct = default);

    /// <summary>
    /// One selling unit for a product, or null if the product doesn't exist
    /// or the unit isn't configured for that product.
    /// </summary>
    Task<ProductUnitInfo?> GetProductUnitAsync(int productId, int unitId, CancellationToken ct = default);

    /// <summary>Add or update an alternative selling unit for a product.</summary>
    Task SaveProductUnitAsync(int productId, int unitId, long factorMilli, CancellationToken ct = default);

    /// <summary>Remove an alternative selling unit. Base unit cannot be removed.</summary>
    Task RemoveProductUnitAsync(int productId, int unitId, CancellationToken ct = default);
}
