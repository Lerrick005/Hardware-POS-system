namespace Pos.Domain.Stock;

public sealed class GoodsReceipt
{
    public int Id { get; set; }
    public string GrnNumber { get; set; } = "";
    public int? SupplierId { get; set; }
    public string? SupplierRef { get; set; }
    public long TotalCents { get; set; }
    public long PaidCents { get; set; }
    public int? UserId { get; set; }
    public string BusinessDate { get; set; } = "";
    public string? CreatedAt { get; set; }

    public Money Total => new(TotalCents);
    public Money Paid => new(PaidCents);
    public List<GoodsReceiptLine> Lines { get; set; } = new();
}
