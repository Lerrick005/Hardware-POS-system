namespace Pos.Domain.Catalog;

public sealed class TaxClass
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int RateBp { get; set; }
    public bool IsActive { get; set; } = true;
}
