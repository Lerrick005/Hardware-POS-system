using Pos.Domain.Catalog;

namespace Pos.Application.Catalog;

public interface IUnitRepository
{
    Task<IReadOnlyList<Unit>> ListAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<Unit?> GetByIdAsync(int id, CancellationToken ct = default);
}
