namespace Pos.Application.Expenses;

/// <summary>
/// Row shape for the expenses list. Class, not record (ADR-0011).
/// </summary>
public sealed class ExpenseListItem
{
    public int Id { get; set; }
    public string Category { get; set; } = "";
    public string? Description { get; set; }
    public long AmountCents { get; set; }
    public string BusinessDate { get; set; } = "";
    public string? Notes { get; set; }
    public string? CreatedAt { get; set; }

    /// <summary>Name of the staff member who recorded it. Null if user was deleted.</summary>
    public string? RecordedByName { get; set; }
}

/// <summary>
/// Aggregate totals by category for the report. Used by the Profit tab.
/// </summary>
public sealed class ExpenseCategoryTotal
{
    public string Category { get; set; } = "";
    public long TotalCents { get; set; }
    public int EntryCount { get; set; }
}

public interface IExpensesService
{
    /// <summary>
    /// Records a new expense. Amount must be positive.
    /// Business date must be YYYY-MM-DD.
    /// User id is required — every expense is attributed to a staff member.
    /// </summary>
    Task<int> RecordExpenseAsync(
        string category,
        string? description,
        long amountCents,
        string businessDate,
        int userId,
        string? notes = null,
        CancellationToken ct = default);

    /// <summary>
    /// List expenses between two dates (inclusive). Both dates required.
    /// Newest first.
    /// </summary>
    Task<IReadOnlyList<ExpenseListItem>> ListAsync(
        string dateFrom,
        string dateTo,
        CancellationToken ct = default);

    /// <summary>
    /// List the most recent N expenses regardless of date. Used by the Expenses page
    /// to show activity without forcing a date picker.
    /// </summary>
    Task<IReadOnlyList<ExpenseListItem>> ListRecentAsync(
        int limit = 100,
        CancellationToken ct = default);

    /// <summary>
    /// Total expenses in a date range, in cents.
    /// </summary>
    Task<long> GetTotalAsync(
        string dateFrom,
        string dateTo,
        CancellationToken ct = default);

    /// <summary>
    /// Total expenses grouped by category for a date range.
    /// Sorted by total descending — biggest cost first.
    /// </summary>
    Task<IReadOnlyList<ExpenseCategoryTotal>> GetTotalsByCategoryAsync(
        string dateFrom,
        string dateTo,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes an expense. Only admins should be able to call this
    /// (permission check is done at the UI/command layer, not here).
    /// Audit-logged.
    /// </summary>
    Task DeleteAsync(int expenseId, int deletedByUserId, CancellationToken ct = default);
}
