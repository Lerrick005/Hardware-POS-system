namespace Pos.Domain.Catalog;

public sealed class Product
{
    public int Id { get; set; }
    public string Uuid { get; set; } = "";
    public string? Sku { get; set; }
    public string Name { get; set; } = "";
    public int? CategoryId { get; set; }
    public int BaseUnitId { get; set; }
    public int? TaxClassId { get; set; }
    public bool TrackStock { get; set; } = true;
    public long StockMilli { get; set; }
    public long AvgCostCents { get; set; }
    public long LastCostCents { get; set; }
    public long ReorderLevelMilli { get; set; }
    public bool IsActive { get; set; } = true;
    public int? ImportBatchId { get; set; }
    public string? CreatedAt { get; set; }
    public string? UpdatedAt { get; set; }

    public Quantity Stock => new(StockMilli);
    public Money AvgCost => new(AvgCostCents);
}
