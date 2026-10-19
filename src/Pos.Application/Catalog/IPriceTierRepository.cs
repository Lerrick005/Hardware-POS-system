using Pos.Domain.Catalog;

namespace Pos.Application.Catalog;

public interface IPriceTierRepository
{
    Task<IReadOnlyList<PriceTier>> ListAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<PriceTier?> GetDefaultAsync(CancellationToken ct = default);
}
