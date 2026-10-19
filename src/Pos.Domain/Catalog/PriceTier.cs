namespace Pos.Domain.Catalog;

public sealed class PriceTier
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}
