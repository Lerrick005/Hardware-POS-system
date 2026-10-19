using Pos.Domain.Catalog;

namespace Pos.Application.Catalog;

public interface ICategoryRepository
{
    Task<IReadOnlyList<Category>> ListAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<int> InsertAsync(Category category, CancellationToken ct = default);
    Task ArchiveAsync(int id, CancellationToken ct = default);
}
