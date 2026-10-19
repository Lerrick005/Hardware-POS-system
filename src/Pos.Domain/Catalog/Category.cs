namespace Pos.Domain.Catalog;

public sealed class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? ParentId { get; set; }
    public bool IsActive { get; set; } = true;
}
