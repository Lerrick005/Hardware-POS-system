using Pos.Domain.Catalog;

namespace Pos.Application.Catalog;

/// <summary>One row of the product list. Class, not record — ADR-0011.</summary>
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
    public long? RetailPriceCents { get; set; }
}

/// <summary>One selling unit for a product, including the base unit row.</summary>
public sealed class ProductUnitInfo
{
    public int UnitId { get; set; }
    public string UnitName { get; set; } = "";
    public string UnitSymbol { get; set; } = "";
    public long FactorMilli { get; set; }
    public bool IsBase { get; set; }
}

/// <summary>One price row: (tier, unit) combination.</summary>
public sealed class ProductPriceInfo
{
    public int TierId { get; set; }
    public string TierName { get; set; } = "";
    public int UnitId { get; set; }
    public string UnitName { get; set; } = "";
    public long PriceCents { get; set; }
}

/// <summary>One price tier for one alternative selling unit.</summary>
public sealed record ProductUnitPriceSave(int TierId, long PriceCents);

/// <summary>One alternative selling unit with its factor and its prices.</summary>
public sealed record ProductUnitSave(
    int UnitId,
    long FactorMilli,
    IReadOnlyList<ProductUnitPriceSave> Prices);

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

    Task<int> SaveProductWithPricesAsync(
        Product product,
        string? barcode,
        IReadOnlyDictionary<int, long> pricesByTierId,
        CancellationToken ct = default);

    Task<long?> GetPriceForUnitAsync(int productId, int tierId, int unitId, CancellationToken ct = default);
    Task UpsertPriceForUnitAsync(int productId, int tierId, int unitId, long priceCents, CancellationToken ct = default);
    Task<IReadOnlyList<ProductPriceInfo>> ListAllPricesAsync(int productId, CancellationToken ct = default);

    Task<IReadOnlyList<ProductUnitInfo>> ListProductUnitsAsync(int productId, CancellationToken ct = default);
    Task<ProductUnitInfo?> GetProductUnitAsync(int productId, int unitId, CancellationToken ct = default);
    Task SaveProductUnitAsync(int productId, int unitId, long factorMilli, CancellationToken ct = default);
    Task RemoveProductUnitAsync(int productId, int unitId, CancellationToken ct = default);

    Task<int> SaveProductWithAllPricesAsync(
        Product product,
        string? barcode,
        IReadOnlyDictionary<int, long> baseTierPrices,
        IReadOnlyList<ProductUnitSave> alternativeUnits,
        CancellationToken ct = default);
}
