using Dapper;
using Pos.Application.Catalog;
using Pos.Domain.Catalog;
using Pos.Infrastructure.Data;

namespace Pos.Infrastructure.Catalog;

public sealed class UnitRepository : IUnitRepository
{
    private readonly DbConnectionFactory _factory;

    public UnitRepository(DbConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<Unit>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        string sql = "SELECT id AS Id, name AS Name, symbol AS Symbol, allows_decimal AS AllowsDecimal, is_active AS IsActive FROM units" +
                     (includeInactive ? "" : " WHERE is_active = 1") +
                     " ORDER BY name";
        var rows = await connection.QueryAsync<Unit>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<Unit?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        return await connection.QueryFirstOrDefaultAsync<Unit>(new CommandDefinition(
            "SELECT id AS Id, name AS Name, symbol AS Symbol, allows_decimal AS AllowsDecimal, is_active AS IsActive FROM units WHERE id = $id",
            new { id }, cancellationToken: ct));
    }
}
