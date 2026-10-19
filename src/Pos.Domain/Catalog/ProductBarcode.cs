namespace Pos.Domain.Catalog;

public sealed class ProductBarcode
{
    public string Barcode { get; set; } = "";
    public int ProductId { get; set; }
    public bool IsPrimary { get; set; }
}
