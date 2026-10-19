namespace Pos.Domain.Stock;

public sealed class GoodsReceiptLine
{
    public int ProductId { get; set; }
    public long QtyMilli { get; set; }
    public long UnitCostCents { get; set; }

    public Quantity Qty => new(QtyMilli);
    public Money UnitCost => new(UnitCostCents);
    public Money LineTotal =>
        new(Money.RoundHalfUpDiv(checked(UnitCostCents * QtyMilli), Quantity.MilliPerUnit));
}
