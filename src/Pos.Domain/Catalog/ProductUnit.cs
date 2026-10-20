namespace Pos.Domain.Catalog;

/// <summary>
/// An alternative selling unit for a product. The base unit (product.base_unit_id)
/// is implicit and needs no row here — every product is always sold by its base
/// unit. This table only stores ADDITIONAL units the shop wants to offer.
///
/// Example: Roofing Nails, base unit = Piece. Rows here might be:
///   (product_id=42, unit_id=Kilogram, factor_milli=25_000)   -- 1 kg = 25 pieces
///   (product_id=42, unit_id=Box,      factor_milli=120_000)  -- 1 box = 120 pieces
///
/// factor_milli is the base-unit quantity (in thousandths) represented by
/// ONE of this unit. So a "Box" with factor 120,000 means selling 1 Box
/// deducts 120 pieces from stock (120,000 milli).
/// </summary>
public sealed class ProductUnit
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int UnitId { get; set; }
    public long FactorMilli { get; set; }
}
