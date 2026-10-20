namespace Pos.Domain.Customers;

public sealed class Customer
{
    public int Id { get; set; }

    /// <summary>Stable UUID for future cloud sync / migration. Never shown to users.</summary>
    public string Uuid { get; set; } = "";

    /// <summary>The name a shop worker would use at the counter.</summary>
    public string Name { get; set; } = "";

    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? KraPin { get; set; }

    /// <summary>Default price type for this customer (Retail / Wholesale / Contractor).</summary>
    public int? PriceTierId { get; set; }

    /// <summary>Maximum credit the shop will extend. Zero means cash only.</summary>
    public long CreditLimitCents { get; set; }

    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public int? ImportBatchId { get; set; }
    public string? CreatedAt { get; set; }

    public Money CreditLimit => new(CreditLimitCents);
}
