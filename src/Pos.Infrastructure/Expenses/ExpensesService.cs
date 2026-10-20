using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.Sqlite;
using Pos.Application.Expenses;
using Pos.Infrastructure.Data;

namespace Pos.Infrastructure.Expenses;

public sealed partial class ExpensesService : IExpensesService
{
    private readonly DbConnectionFactory _factory;

    public ExpensesService(DbConnectionFactory factory) => _factory = factory;

    public async Task<int> RecordExpenseAsync(
        string category,
        string? description,
        long amountCents,
        string businessDate,
        int userId,
        string? notes = null,
        CancellationToken ct = default)
    {
        ValidateCategory(category);
        ValidateAmount(amountCents);
        ValidateDate(businessDate);

        using SqliteConnection connection = _factory.CreateOpen();
        using SqliteTransaction tx = connection.BeginTransaction();

        try
        {
            long id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(@"
                INSERT INTO expenses(category, description, amount_cents, business_date, user_id, notes)
                VALUES ($category, $description, $amountCents, $businessDate, $userId, $notes)
                RETURNING id",
                new
                {
                    category = category.Trim(),
                    description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                    amountCents,
                    businessDate,
                    userId,
                    notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
                },
                tx, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition(@"
                INSERT INTO audit_log(user_id, action, entity_type, entity_id, old_value, new_value)
                VALUES ($userId, 'EXPENSE_RECORDED', 'expense', $id, NULL, $detail)",
                new
                {
                    userId,
                    id = (int)id,
                    detail = $"{category.Trim()} {amountCents / 100m:0.00} on {businessDate}",
                },
                tx, cancellationToken: ct));

            tx.Commit();
            return (int)id;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<IReadOnlyList<ExpenseListItem>> ListAsync(
        string dateFrom,
        string dateTo,
        CancellationToken ct = default)
    {
        ValidateDate(dateFrom);
        ValidateDate(dateTo);

        using var connection = _factory.CreateOpen();
        var rows = await connection.QueryAsync<ExpenseListItem>(new CommandDefinition(@"
            SELECT e.id           AS Id,
                   e.category     AS Category,
                   e.description  AS Description,
                   e.amount_cents AS AmountCents,
                   e.business_date AS BusinessDate,
                   e.notes        AS Notes,
                   e.created_at   AS CreatedAt,
                   u.name         AS RecordedByName
              FROM expenses e
              LEFT JOIN users u ON u.id = e.user_id
             WHERE e.business_date >= $from AND e.business_date <= $to
             ORDER BY e.business_date DESC, e.id DESC",
            new { from = dateFrom, to = dateTo }, cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<IReadOnlyList<ExpenseListItem>> ListRecentAsync(
        int limit = 100,
        CancellationToken ct = default)
    {
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));

        using var connection = _factory.CreateOpen();
        var rows = await connection.QueryAsync<ExpenseListItem>(new CommandDefinition(@"
            SELECT e.id           AS Id,
                   e.category     AS Category,
                   e.description  AS Description,
                   e.amount_cents AS AmountCents,
                   e.business_date AS BusinessDate,
                   e.notes        AS Notes,
                   e.created_at   AS CreatedAt,
                   u.name         AS RecordedByName
              FROM expenses e
              LEFT JOIN users u ON u.id = e.user_id
             ORDER BY e.business_date DESC, e.id DESC
             LIMIT $limit",
            new { limit }, cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<long> GetTotalAsync(
        string dateFrom,
        string dateTo,
        CancellationToken ct = default)
    {
        ValidateDate(dateFrom);
        ValidateDate(dateTo);

        using var connection = _factory.CreateOpen();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(@"
            SELECT COALESCE(SUM(amount_cents), 0)
              FROM expenses
             WHERE business_date >= $from AND business_date <= $to",
            new { from = dateFrom, to = dateTo }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ExpenseCategoryTotal>> GetTotalsByCategoryAsync(
        string dateFrom,
        string dateTo,
        CancellationToken ct = default)
    {
        ValidateDate(dateFrom);
        ValidateDate(dateTo);

        using var connection = _factory.CreateOpen();
        var rows = await connection.QueryAsync<ExpenseCategoryTotal>(new CommandDefinition(@"
            SELECT category                      AS Category,
                   SUM(amount_cents)             AS TotalCents,
                   COUNT(1)                      AS EntryCount
              FROM expenses
             WHERE business_date >= $from AND business_date <= $to
             GROUP BY category
             ORDER BY SUM(amount_cents) DESC",
            new { from = dateFrom, to = dateTo }, cancellationToken: ct));

        return rows.AsList();
    }

    public async Task DeleteAsync(int expenseId, int deletedByUserId, CancellationToken ct = default)
    {
        if (expenseId <= 0) throw new ArgumentOutOfRangeException(nameof(expenseId));
        if (deletedByUserId <= 0) throw new ArgumentOutOfRangeException(nameof(deletedByUserId));

        using SqliteConnection connection = _factory.CreateOpen();
        using SqliteTransaction tx = connection.BeginTransaction();

        try
        {
            ExpenseRow? row = await connection.QueryFirstOrDefaultAsync<ExpenseRow>(new CommandDefinition(@"
                SELECT id AS Id, category AS Category, amount_cents AS AmountCents,
                       business_date AS BusinessDate
                  FROM expenses WHERE id = $id",
                new { id = expenseId }, tx, cancellationToken: ct));

            if (row is null)
            {
                throw new InvalidOperationException($"Expense {expenseId} not found.");
            }

            // Audit FIRST — captures the full details of what was deleted
            // before the row disappears. The audit row keeps entity_id as a
            // historical reference even though the expense row is gone.
            await connection.ExecuteAsync(new CommandDefinition(@"
                INSERT INTO audit_log(user_id, action, entity_type, entity_id, old_value, new_value)
                VALUES ($deletedBy, 'EXPENSE_DELETED', 'expense', $id, $detail, NULL)",
                new
                {
                    deletedBy = deletedByUserId,
                    id = expenseId,
                    detail = $"{row.Category} {row.AmountCents / 100m:0.00} on {row.BusinessDate}",
                },
                tx, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM expenses WHERE id = $id",
                new { id = expenseId }, tx, cancellationToken: ct));

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static void ValidateCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            throw new ArgumentException("Category is required.", nameof(category));
        }

        if (category.Trim().Length > 50)
        {
            throw new ArgumentException("Category must be 50 characters or fewer.", nameof(category));
        }
    }

    private static void ValidateAmount(long amountCents)
    {
        if (amountCents <= 0)
        {
            throw new ArgumentException("Amount must be greater than zero.", nameof(amountCents));
        }

        // Sanity cap: 100 million KES (100,000,000 * 100 cents).
        // A single expense larger than this is almost certainly a typo.
        if (amountCents > 10_000_000_000L)
        {
            throw new ArgumentException(
                "Amount is unreasonably large — did you type an extra zero?", nameof(amountCents));
        }
    }

    private static void ValidateDate(string businessDate)
    {
        if (string.IsNullOrWhiteSpace(businessDate) || !DatePattern().IsMatch(businessDate))
        {
            throw new ArgumentException("Date must be YYYY-MM-DD.", nameof(businessDate));
        }
    }

    [GeneratedRegex("^[0-9]{4}-[0-9]{2}-[0-9]{2}$")]
    private static partial Regex DatePattern();

    /// <summary>
    /// Private row for the delete path — we only need these four columns
    /// to build the audit detail before deleting. Not the same as the list DTO.
    /// </summary>
    private sealed class ExpenseRow
    {
        public int Id { get; set; }
        public string Category { get; set; } = "";
        public long AmountCents { get; set; }
        public string BusinessDate { get; set; } = "";
    }
}
