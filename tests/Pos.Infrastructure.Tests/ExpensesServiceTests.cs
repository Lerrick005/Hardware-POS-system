using Dapper;
using Pos.Infrastructure.Expenses;

namespace Pos.Infrastructure.Tests;

public sealed class ExpensesServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly ExpensesService _service;
    private readonly int _userId;

    public ExpensesServiceTests()
    {
        _service = new ExpensesService(_db.Factory);
        _userId = InsertUser();
    }

    public void Dispose() => _db.Dispose();

    private int InsertUser()
    {
        using var c = _db.Factory.CreateOpen();
        return (int)c.ExecuteScalar<long>(@"
            INSERT INTO users(name, role, pin_hash, is_active)
            VALUES ('Test User', 'ADMIN', 'PIN_NOT_SET', 1) RETURNING id");
    }

    private int CountExpenses()
    {
        using var c = _db.Factory.CreateOpen();
        return c.ExecuteScalar<int>("SELECT COUNT(1) FROM expenses");
    }

    private int CountAudit(string action)
    {
        using var c = _db.Factory.CreateOpen();
        return c.ExecuteScalar<int>(
            "SELECT COUNT(1) FROM audit_log WHERE action = $action", new { action });
    }

    // ===== Record =====

    [Fact]
    public async Task RecordExpense_WritesRow_AndAudit()
    {
        int id = await _service.RecordExpenseAsync(
            "Rent", "Monthly shop rent", 4_000_000, "2026-10-20", _userId);

        Assert.True(id > 0);
        Assert.Equal(1, CountExpenses());
        Assert.Equal(1, CountAudit("EXPENSE_RECORDED"));

        using var c = _db.Factory.CreateOpen();
        long amount = c.ExecuteScalar<long>(
            "SELECT amount_cents FROM expenses WHERE id = $id", new { id });
        Assert.Equal(4_000_000, amount);
    }

    [Fact]
    public async Task RecordExpense_TrimsCategoryAndDescription()
    {
        int id = await _service.RecordExpenseAsync(
            "  Rent  ", "  Rent for October  ", 1000, "2026-10-20", _userId);

        using var c = _db.Factory.CreateOpen();
        var row = c.QuerySingle<(string Category, string? Description)>(
            "SELECT category, description FROM expenses WHERE id = $id", new { id });

        Assert.Equal("Rent", row.Category);
        Assert.Equal("Rent for October", row.Description);
    }

    [Fact]
    public async Task RecordExpense_AllowsNullDescriptionAndNotes()
    {
        int id = await _service.RecordExpenseAsync(
            "Salary", null, 50000, "2026-10-20", _userId, null);

        Assert.True(id > 0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("this category name is definitely longer than fifty characters which is not allowed")]
    public async Task RecordExpense_BadCategory_Throws(string category)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.RecordExpenseAsync(category, null, 1000, "2026-10-20", _userId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public async Task RecordExpense_NonPositiveAmount_Throws(long amount)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.RecordExpenseAsync("Test", null, amount, "2026-10-20", _userId));
    }

    [Fact]
    public async Task RecordExpense_AbsurdAmount_Throws()
    {
        // > 100 million KES = likely typo with extra zeros
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.RecordExpenseAsync("Test", null, 20_000_000_000L, "2026-10-20", _userId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("20-10-2026")]
    [InlineData("2026/10/20")]
    [InlineData("not-a-date")]
    public async Task RecordExpense_BadDate_Throws(string date)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.RecordExpenseAsync("Test", null, 1000, date, _userId));
    }

    // ===== List =====

    [Fact]
    public async Task List_ReturnsExpensesInDateRange()
    {
        await _service.RecordExpenseAsync("Rent", null, 40000, "2026-10-01", _userId);
        await _service.RecordExpenseAsync("Salary", null, 35000, "2026-10-15", _userId);
        await _service.RecordExpenseAsync("Transport", null, 5000, "2026-11-01", _userId);

        var october = await _service.ListAsync("2026-10-01", "2026-10-31");
        var november = await _service.ListAsync("2026-11-01", "2026-11-30");
        var all = await _service.ListAsync("2026-01-01", "2026-12-31");

        Assert.Equal(2, october.Count);
        Assert.Single(november);
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public async Task List_OrdersNewestFirst()
    {
        await _service.RecordExpenseAsync("A", null, 100, "2026-10-01", _userId);
        await _service.RecordExpenseAsync("B", null, 200, "2026-10-15", _userId);
        await _service.RecordExpenseAsync("C", null, 300, "2026-10-10", _userId);

        var all = await _service.ListAsync("2026-10-01", "2026-10-31");

        Assert.Equal("B", all[0].Category);   // 2026-10-15 first
        Assert.Equal("C", all[1].Category);   // 2026-10-10 second
        Assert.Equal("A", all[2].Category);   // 2026-10-01 last
    }

    [Fact]
    public async Task List_IncludesRecordedByName()
    {
        await _service.RecordExpenseAsync("Test", null, 100, "2026-10-20", _userId);

        var list = await _service.ListAsync("2026-10-20", "2026-10-20");

        Assert.Single(list);
        Assert.Equal("Test User", list[0].RecordedByName);
    }

    [Fact]
    public async Task ListRecent_LimitsResults()
    {
        for (int i = 0; i < 5; i++)
        {
            await _service.RecordExpenseAsync($"Cat{i}", null, 100, "2026-10-20", _userId);
        }

        var recent = await _service.ListRecentAsync(limit: 3);

        Assert.Equal(3, recent.Count);
    }

    // ===== Totals =====

    [Fact]
    public async Task GetTotal_SumsDateRange()
    {
        await _service.RecordExpenseAsync("A", null, 10000, "2026-10-01", _userId);
        await _service.RecordExpenseAsync("B", null, 25000, "2026-10-15", _userId);
        await _service.RecordExpenseAsync("C", null, 50000, "2026-11-01", _userId);

        long octoberTotal = await _service.GetTotalAsync("2026-10-01", "2026-10-31");
        long allTotal = await _service.GetTotalAsync("2026-01-01", "2026-12-31");
        long emptyRange = await _service.GetTotalAsync("2025-01-01", "2025-12-31");

        Assert.Equal(35000, octoberTotal);
        Assert.Equal(85000, allTotal);
        Assert.Equal(0, emptyRange);
    }

    [Fact]
    public async Task GetTotalsByCategory_GroupsAndSorts()
    {
        await _service.RecordExpenseAsync("Rent", null, 40000, "2026-10-01", _userId);
        await _service.RecordExpenseAsync("Rent", null, 40000, "2026-11-01", _userId);
        await _service.RecordExpenseAsync("Salary", null, 35000, "2026-10-15", _userId);
        await _service.RecordExpenseAsync("Transport", null, 5000, "2026-10-20", _userId);

        var totals = await _service.GetTotalsByCategoryAsync("2026-01-01", "2026-12-31");

        Assert.Equal(3, totals.Count);
        Assert.Equal("Rent", totals[0].Category);
        Assert.Equal(80000, totals[0].TotalCents);
        Assert.Equal(2, totals[0].EntryCount);
        Assert.Equal("Salary", totals[1].Category);
        Assert.Equal(35000, totals[1].TotalCents);
        Assert.Equal("Transport", totals[2].Category);
    }

    // ===== Delete =====

    [Fact]
    public async Task Delete_RemovesRow_AndWritesAudit()
    {
        int id = await _service.RecordExpenseAsync(
            "Mistake", "Typo", 5000, "2026-10-20", _userId);

        await _service.DeleteAsync(id, _userId);

        Assert.Equal(0, CountExpenses());
        Assert.Equal(1, CountAudit("EXPENSE_RECORDED"));  // creation still logged
        Assert.Equal(1, CountAudit("EXPENSE_DELETED"));   // deletion logged
    }

    [Fact]
    public async Task Delete_AuditCapturesDetailsBeforeRowDisappears()
    {
        int id = await _service.RecordExpenseAsync(
            "Rent", "October", 40000, "2026-10-15", _userId);

        await _service.DeleteAsync(id, _userId);

        using var c = _db.Factory.CreateOpen();
        string? auditDetail = c.ExecuteScalar<string>(
            "SELECT old_value FROM audit_log WHERE action = 'EXPENSE_DELETED'");

        Assert.NotNull(auditDetail);
        Assert.Contains("Rent", auditDetail);
        Assert.Contains("400.00", auditDetail);      // 40000 cents = 400.00 KES
        Assert.Contains("2026-10-15", auditDetail);
    }

    [Fact]
    public async Task Delete_UnknownId_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.DeleteAsync(99999, _userId));
    }

    [Fact]
    public async Task Delete_RollsBack_OnInvalidUser()
    {
        int id = await _service.RecordExpenseAsync(
            "Test", null, 1000, "2026-10-20", _userId);

        // delete with invalid user id
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _service.DeleteAsync(id, deletedByUserId: 0));

        // Expense must still exist
        Assert.Equal(1, CountExpenses());
    }
}
