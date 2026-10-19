using Dapper;
using Pos.Application.Catalog;
using Pos.Domain.Catalog;
using Pos.Infrastructure.Data;

namespace Pos.Infrastructure.Catalog;

public sealed class PriceTierRepository : IPriceTierRepository
{
    private readonly DbConnectionFactory _factory;

    public PriceTierRepository(DbConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<PriceTier>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        string sql = "SELECT id AS Id, name AS Name, is_default AS IsDefault, is_active AS IsActive FROM price_tiers" +
                     (includeInactive ? "" : " WHERE is_active = 1") +
                     " ORDER BY name";
        var rows = await connection.QueryAsync<PriceTier>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<PriceTier?> GetDefaultAsync(CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        return await connection.QueryFirstOrDefaultAsync<PriceTier>(new CommandDefinition(
            "SELECT id AS Id, name AS Name, is_default AS IsDefault, is_active AS IsActive FROM price_tiers WHERE is_default = 1 LIMIT 1",
            cancellationToken: ct));
    }
}
