using Dapper;
using Pos.Application.Catalog;
using Pos.Domain.Catalog;
using Pos.Infrastructure.Data;

namespace Pos.Infrastructure.Catalog;

public sealed class CategoryRepository : ICategoryRepository
{
    private readonly DbConnectionFactory _factory;

    public CategoryRepository(DbConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<Category>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        string sql = "SELECT id AS Id, name AS Name, parent_id AS ParentId, is_active AS IsActive FROM categories" +
                     (includeInactive ? "" : " WHERE is_active = 1") +
                     " ORDER BY name COLLATE NOCASE";
        var rows = await connection.QueryAsync<Category>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<Category?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        return await connection.QueryFirstOrDefaultAsync<Category>(new CommandDefinition(
            "SELECT id AS Id, name AS Name, parent_id AS ParentId, is_active AS IsActive FROM categories WHERE id = $id",
            new { id }, cancellationToken: ct));
    }

    public async Task<int> InsertAsync(Category category, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        long id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "INSERT INTO categories(name, parent_id, is_active) VALUES ($Name, $ParentId, $IsActive) RETURNING id",
            category, cancellationToken: ct));
        return (int)id;
    }

    public async Task UpdateAsync(Category category, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        await connection.ExecuteAsync(new CommandDefinition(@"
            UPDATE categories
               SET name      = $Name,
                   parent_id = $ParentId,
                   is_active = $IsActive
             WHERE id = $Id", category, cancellationToken: ct));
    }

    public async Task ArchiveAsync(int id, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE categories SET is_active = 0 WHERE id = $id",
            new { id }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<CategoryWithCount>> ListWithCountsAsync(
        bool includeInactive = false,
        CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        string sql = @"
            SELECT c.id         AS Id,
                   c.name       AS Name,
                   c.parent_id  AS ParentId,
                   c.is_active  AS IsActive,
                   (SELECT COUNT(1) FROM products p WHERE p.category_id = c.id AND p.is_active = 1) AS ProductCount
              FROM categories c" +
            (includeInactive ? "" : " WHERE c.is_active = 1") +
            " ORDER BY c.name COLLATE NOCASE";

        var rows = await connection.QueryAsync<CategoryWithCount>(
            new CommandDefinition(sql, cancellationToken: ct));
        return rows.AsList();
    }
}
