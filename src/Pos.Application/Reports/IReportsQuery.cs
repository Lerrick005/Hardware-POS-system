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

public interface IReportsQuery
{
    Task<SalesSummary> GetSalesSummaryAsync(string dateFrom, string dateTo, CancellationToken ct = default);
    Task<IReadOnlyList<ProductSalesRow>> GetTopProductsAsync(string dateFrom, string dateTo, int limit = 20, CancellationToken ct = default);
    Task<IReadOnlyList<StockValuationRow>> GetStockValuationAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DebtorRow>> GetDebtorsAsync(CancellationToken ct = default);
}
