namespace Pos.Domain.Sales;

/// <summary>
/// One line on a sale. Snapshots product name, sku, price, cost and tax rate at
/// time of sale so historical receipts and reports stay accurate even after
/// the product is edited or archived.
/// </summary>
public sealed class SaleLine
{
    public int Id { get; set; }
    public int ProductId { get; set; }

    public string ProductName { get; set; } = "";
    public string? ProductSku { get; set; }

    public int UnitId { get; set; }

    /// <summary>Quantity in the selling unit (thousandths).</summary>
    public long QtyMilli { get; set; }

    /// <summary>Quantity converted to the product's base unit (thousandths). What moves in stock.</summary>
    public long BaseQtyMilli { get; set; }

    public long UnitPriceCents { get; set; }
    public long UnitCostCents { get; set; }
    public long DiscountCents { get; set; }
    public int TaxRateBp { get; set; }
    public long LineTotalCents { get; set; }

    public string? Notes { get; set; }

    public Quantity Qty => new(QtyMilli);
    public Quantity BaseQty => new(BaseQtyMilli);
    public Money UnitPrice => new(UnitPriceCents);
    public Money UnitCost => new(UnitCostCents);
    public Money Discount => new(DiscountCents);
    public Money LineTotal => new(LineTotalCents);
}
