using Pos.Domain.Catalog;

namespace Pos.Application.Catalog;

public interface IProductRepository
{
    Task<IReadOnlyList<Product>> ListAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<Product?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken ct = default);
    Task<IReadOnlyList<Product>> SearchAsync(string term, int limit = 50, CancellationToken ct = default);
    Task<int> InsertAsync(Product product, CancellationToken ct = default);
    Task UpdateAsync(Product product, CancellationToken ct = default);
    Task ArchiveAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<ProductBarcode>> ListBarcodesAsync(int productId, CancellationToken ct = default);
    Task AddBarcodeAsync(ProductBarcode barcode, CancellationToken ct = default);
    Task<bool> BarcodeExistsAsync(string barcode, CancellationToken ct = default);

    /// <summary>
    /// Inserts a product and (optionally) its primary barcode in one transaction.
    /// If the barcode already exists, throws and the product is not created.
    /// </summary>
    Task<int> InsertWithBarcodeAsync(Product product, string? barcode, CancellationToken ct = default);

    /// <summary>Returns the price for a product at a given tier, or null if not set.</summary>
    Task<long?> GetPriceAsync(int productId, int tierId, CancellationToken ct = default);

    /// <summary>Inserts or updates a product's price at a given tier.</summary>
    Task UpsertPriceAsync(int productId, int tierId, long priceCents, CancellationToken ct = default);
}
