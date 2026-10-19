namespace Pos.Domain.Catalog;

public sealed class Unit
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Symbol { get; set; } = "";
    public bool AllowsDecimal { get; set; }
    public bool IsActive { get; set; } = true;
}
