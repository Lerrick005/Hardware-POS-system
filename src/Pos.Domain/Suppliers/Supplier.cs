namespace Pos.Domain.Suppliers;

public sealed class Supplier
{
    public int Id { get; set; }
    
    /// <summary>Stable UUID for future cloud sync. Not shown to users.</summary>
    public string Uuid { get; set; } = "";
    
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Notes { get; set; }
    
    public bool IsActive { get; set; } = true;
    public string? CreatedAt { get; set; }
}
