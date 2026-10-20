using Dapper;
using Pos.Application.Suppliers;
using Pos.Domain.Suppliers;
using Pos.Infrastructure.Data;

namespace Pos.Infrastructure.Suppliers;

public sealed class SupplierRepository : ISupplierRepository
{
    private readonly DbConnectionFactory _factory;

    public SupplierRepository(DbConnectionFactory factory) => _factory = factory;

    // We calculate the balance by summing unpaid goods receipts for this supplier.
    private const string BalanceColumn = 
        "COALESCE((SELECT SUM(total_cents - paid_cents) FROM goods_receipts WHERE supplier_id = s.id), 0)";

    private const string ListSelect = @"
        SELECT s.id         AS Id,
               s.name       AS Name,
               s.phone      AS Phone,
               s.email      AS Email,
               s.is_active  AS IsActive,
               " + BalanceColumn + @" AS BalanceCents
          FROM suppliers s";

    public async Task<IReadOnlyList<SupplierListItem>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        string sql = ListSelect +
                     (includeInactive ? "" : " WHERE s.is_active = 1") +
                     " ORDER BY s.name COLLATE NOCASE";
        
        var rows = await connection.QueryAsync<SupplierListItem>(
            new CommandDefinition(sql, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<SupplierListItem>> SearchAsync(string term, int limit = 100, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return await ListAsync(includeInactive: false, ct);
        }

        string[] tokens = term
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .Take(6)
            .ToArray();

        if (tokens.Length == 0)
        {
            return await ListAsync(includeInactive: false, ct);
        }

        string[] clauses = tokens
            .Select((_, i) => $"(lower(s.name) LIKE $t{i} OR lower(coalesce(s.phone,'')) LIKE $t{i})")
            .ToArray();

        string sql = ListSelect +
                     " WHERE s.is_active = 1 AND " +
                     string.Join(" AND ", clauses) +
                     " ORDER BY s.name COLLATE NOCASE LIMIT $limit";

        DynamicParameters parameters = new();
        for (int i = 0; i < tokens.Length; i++)
        {
            parameters.Add($"t{i}", $"%{tokens[i]}%");
        }
        parameters.Add("limit", limit);

        using var connection = _factory.CreateOpen();
        var rows = await connection.QueryAsync<SupplierListItem>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<Supplier?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        return await connection.QueryFirstOrDefaultAsync<Supplier>(new CommandDefinition(@"
            SELECT id AS Id, name AS Name, phone AS Phone, email AS Email,
                   notes AS Notes, is_active AS IsActive
              FROM suppliers WHERE id = $id",
            new { id }, cancellationToken: ct));
    }

    public async Task<int> InsertAsync(Supplier supplier, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        // Note: The schema does not have a uuid column for suppliers.
        long id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(@"
            INSERT INTO suppliers(name, phone, email, notes, is_active)
            VALUES ($Name, $Phone, $Email, $Notes, $IsActive)
            RETURNING id",
            supplier, cancellationToken: ct));
        return (int)id;
    }

    public async Task UpdateAsync(Supplier supplier, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        await connection.ExecuteAsync(new CommandDefinition(@"
            UPDATE suppliers
               SET name      = $Name,
                   phone     = $Phone,
                   email     = $Email,
                   notes     = $Notes,
                   is_active = $IsActive
             WHERE id = $Id", supplier, cancellationToken: ct));
    }

    public async Task ArchiveAsync(int id, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE suppliers SET is_active = 0 WHERE id = $id",
            new { id }, cancellationToken: ct));
    }
}
