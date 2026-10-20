using Pos.Domain.Catalog;

namespace Pos.Application.Catalog;

/// <summary>
/// One row of the product list. Includes the retail price (joined from
/// product_prices), so the grid can show what the shop sells it for without
/// a second query per row. Class, not record — see ADR-0011.
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

    /// <summary>Retail price in cents, or null if no retail price has been set.</summary>
    public long? RetailPriceCents { get; set; }
}

public interface IProductRepository
{
    Task<IReadOnlyList<ProductListItem>> ListAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<Product?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken ct = default);
    Task<IReadOnlyList<ProductListItem>> SearchAsync(string term, int limit = 50, CancellationToken ct = default);
    Task<int> InsertAsync(Product product, CancellationToken ct = default);
    Task UpdateAsync(Product product, CancellationToken ct = default);
    Task ArchiveAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<ProductBarcode>> ListBarcodesAsync(int productId, CancellationToken ct = default);
    Task AddBarcodeAsync(ProductBarcode barcode, CancellationToken ct = default);
    Task<bool> BarcodeExistsAsync(string barcode, CancellationToken ct = default);
    Task<int> InsertWithBarcodeAsync(Product product, string? barcode, CancellationToken ct = default);
    Task<long?> GetPriceAsync(int productId, int tierId, CancellationToken ct = default);
    Task UpsertPriceAsync(int productId, int tierId, long priceCents, CancellationToken ct = default);

    /// <summary>
    /// Creates or updates a product along with its prices in a single transaction.
    ///
    /// Id == 0  => insert. When barcode is provided, it is written with is_primary = 1.
    /// Id > 0   => update. Barcode is ignored on this path (add more barcodes via AddBarcodeAsync).
    ///
    /// pricesByTierId is tier_id -> price_cents. Tiers not present are left unchanged
    /// (so passing only Retail preserves any existing Wholesale price from an earlier save).
    ///
    /// Returns the product id.
    /// </summary>
    Task<int> SaveProductWithPricesAsync(
        Product product,
        string? barcode,
        IReadOnlyDictionary<int, long> pricesByTierId,
        CancellationToken ct = default);
}
