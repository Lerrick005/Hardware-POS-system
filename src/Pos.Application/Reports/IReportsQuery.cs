namespace Pos.Application.Reports;

public sealed class SalesSummary
{
    public long TotalCents { get; set; }
    public int TransactionCount { get; set; }
    public long CashCents { get; set; }
    public long MpesaCents { get; set; }
    public long CreditCents { get; set; }
    public long AvgTicketCents { get; set; }
}

public sealed class ProductSalesRow
{
    public string ProductName { get; set; } = "";
    public long QtySoldMilli { get; set; }
    public long RevenueCents { get; set; }
}

public sealed class StockValuationRow
{
    public string CategoryName { get; set; } = "(Uncategorised)";
    public int ProductCount { get; set; }
    public long TotalCostCents { get; set; }
    public long TotalRetailCents { get; set; }
}

public sealed class DebtorRow
{
    public string CustomerName { get; set; } = "";
    public string Phone { get; set; } = "";
    public long BalanceCents { get; set; }
    public string LastActivityDate { get; set; } = "";
}

/// <summary>
/// Full profit picture for a date range. Revenue and COGS come from sales
/// (with cost snapshotted at sale time). Expenses come from the expenses table.
///
/// NetProfit can be negative — a loss. That is a real, honest answer.
/// </summary>
public sealed class ProfitSummary
{
    public long RevenueCents { get; set; }
    public long CostOfGoodsSoldCents { get; set; }
    public long GrossProfitCents { get; set; }
    public long ExpensesCents { get; set; }
    public long NetProfitCents { get; set; }
    public int TransactionCount { get; set; }

    /// <summary>Products sold with no recorded cost. Drives the "profit is estimated" warning.</summary>
    public int UncategorizedCostLineCount { get; set; }
}

/// <summary>
/// Per-product profit row. Sorted by profit descending (not revenue) —
/// the "what makes me the most money" question.
/// </summary>
public sealed class ProductProfitRow
{
    public string ProductName { get; set; } = "";
    public long QtySoldMilli { get; set; }
    public long RevenueCents { get; set; }
    public long CostCents { get; set; }
    public long ProfitCents { get; set; }

    /// <summary>Margin as percentage of revenue. Zero when revenue is zero.</summary>
    public decimal MarginPercent { get; set; }
}

/// <summary>
/// Product not sold in N days. Used by the dead-stock report.
/// </summary>
public sealed class DeadStockRow
{
    public string ProductName { get; set; } = "";
    public long StockMilli { get; set; }
    public long AvgCostCents { get; set; }
    public long TotalCostCents { get; set; }

    /// <summary>Last sale date (YYYY-MM-DD), or null if never sold.</summary>
    public string? LastSoldDate { get; set; }

    /// <summary>Days since last sale, or -1 if never sold.</summary>
    public int DaysSinceLastSale { get; set; }
}

public interface IReportsQuery
{
    Task<SalesSummary> GetSalesSummaryAsync(string dateFrom, string dateTo, CancellationToken ct = default);
    Task<IReadOnlyList<ProductSalesRow>> GetTopProductsAsync(string dateFrom, string dateTo, int limit = 20, CancellationToken ct = default);
    Task<IReadOnlyList<StockValuationRow>> GetStockValuationAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DebtorRow>> GetDebtorsAsync(CancellationToken ct = default);

    /// <summary>
    /// Full profit summary: revenue - COGS - expenses = net profit.
    /// Negative net = the shop lost money in that period. Honest answer.
    /// </summary>
    Task<ProfitSummary> GetProfitSummaryAsync(
        string dateFrom, string dateTo, CancellationToken ct = default);

    /// <summary>
    /// Per-product profit, sorted by total profit descending.
    /// </summary>
    Task<IReadOnlyList<ProductProfitRow>> GetProductProfitAsync(
        string dateFrom, string dateTo, int limit = 50, CancellationToken ct = default);

    /// <summary>
    /// Products with stock &gt; 0 that have not sold in the given number of days.
    /// Includes never-sold products with DaysSinceLastSale = -1.
    /// </summary>
    Task<IReadOnlyList<DeadStockRow>> GetDeadStockAsync(
        int daysThreshold = 90, CancellationToken ct = default);
}

