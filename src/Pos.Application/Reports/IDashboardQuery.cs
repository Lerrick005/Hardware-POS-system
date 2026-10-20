namespace Pos.Application.Reports;

/// <summary>
/// A single snapshot of the numbers the owner wants to see when they open
/// the app. Everything is "as of right now" — no date range picker.
///
/// Deliberately separate from IReportsQuery: Reports is for exploring history,
/// Dashboard is for "what needs my attention this second."
/// </summary>
public sealed class DashboardSnapshot
{
    public long TodaySalesCents { get; set; }
    public int TodayTransactionCount { get; set; }
    public long TodayCashCents { get; set; }
    public long TodayMpesaCents { get; set; }
    public long TodayCreditCents { get; set; }

    public int LowStockCount { get; set; }
    public int OutOfStockCount { get; set; }

    public long DebtorsTotalCents { get; set; }
    public int DebtorCount { get; set; }

    public bool HasAnyProducts { get; set; }
    public bool HasAnySales { get; set; }
    public bool HasAnyCustomers { get; set; }
}

public sealed class LowStockItem
{
    public string ProductName { get; set; } = "";
    public long StockMilli { get; set; }
    public long ReorderLevelMilli { get; set; }
}

public interface IDashboardQuery
{
    Task<DashboardSnapshot> GetSnapshotAsync(string todayBusinessDate, CancellationToken ct = default);

    Task<IReadOnlyList<LowStockItem>> GetTopLowStockAsync(int limit = 5, CancellationToken ct = default);
}
