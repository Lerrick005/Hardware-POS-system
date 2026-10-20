using Pos.Domain.Suppliers;

namespace Pos.Application.Suppliers;

/// <summary>
/// A row in the supplier list. Includes a calculated balance (total unpaid deliveries).
/// Class, not record (ADR-0011).
/// </summary>
public sealed class SupplierListItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; }
    
    /// <summary>Total value of deliveries minus payments made to this supplier.</summary>
    public long BalanceCents { get; set; }
}

public interface ISupplierRepository
{
    Task<IReadOnlyList<SupplierListItem>> ListAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<IReadOnlyList<SupplierListItem>> SearchAsync(string term, int limit = 100, CancellationToken ct = default);
    Task<Supplier?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<int> InsertAsync(Supplier supplier, CancellationToken ct = default);
    Task UpdateAsync(Supplier supplier, CancellationToken ct = default);
    Task ArchiveAsync(int id, CancellationToken ct = default);
}
