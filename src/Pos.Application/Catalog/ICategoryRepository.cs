using Pos.Domain.Catalog;

namespace Pos.Application.Catalog;

/// <summary>
/// A category with a count of products currently assigned to it. Used by the
/// category management dialog so archive warnings can say "5 products will
/// be uncategorised" instead of silently orphaning them.
/// Class, not record — see ADR-0011.
/// </summary>
public sealed class CategoryWithCount
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? ParentId { get; set; }
    public bool IsActive { get; set; }
    public int ProductCount { get; set; }
}

public interface ICategoryRepository
{
    /// <summary>Active categories, alphabetical. Used by pickers.</summary>
    Task<IReadOnlyList<Category>> ListAsync(bool includeInactive = false, CancellationToken ct = default);

    /// <summary>For loading an existing category into an edit form.</summary>
    Task<Category?> GetByIdAsync(int id, CancellationToken ct = default);

    Task<int> InsertAsync(Category category, CancellationToken ct = default);

    /// <summary>Rename / reparent / activate-deactivate.</summary>
    Task UpdateAsync(Category category, CancellationToken ct = default);

    Task ArchiveAsync(int id, CancellationToken ct = default);

    /// <summary>For the management dialog — includes per-category product count.</summary>
    Task<IReadOnlyList<CategoryWithCount>> ListWithCountsAsync(
        bool includeInactive = false,
        CancellationToken ct = default);
}
