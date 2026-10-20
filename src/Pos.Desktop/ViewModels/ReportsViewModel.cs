using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Reports;
using Pos.Domain;

namespace Pos.Desktop.ViewModels;

public partial class ReportsViewModel : ObservableObject
{
    private readonly IReportsQuery _query;
    private readonly ILogger<ReportsViewModel> _logger;

    [ObservableProperty] private string dateFrom = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    [ObservableProperty] private string dateTo = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    [ObservableProperty] private string activeTab = "Sales";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusText = "";

    [ObservableProperty] private string totalSalesDisplay = "0.00";
    [ObservableProperty] private string transactionCountDisplay = "0";
    [ObservableProperty] private string avgTicketDisplay = "0.00";
    [ObservableProperty] private string cashDisplay = "0.00";
    [ObservableProperty] private string mpesaDisplay = "0.00";
    [ObservableProperty] private string creditDisplay = "0.00";
    [ObservableProperty] private string stockCostDisplay = "0.00";
    [ObservableProperty] private string stockRetailDisplay = "0.00";
    [ObservableProperty] private string debtorsTotalDisplay = "0.00";

    [ObservableProperty] private string revenueDisplay = "0.00";
    [ObservableProperty] private string cogsDisplay = "0.00";
    [ObservableProperty] private string grossProfitDisplay = "0.00";
    [ObservableProperty] private string expensesDisplay = "0.00";
    [ObservableProperty] private string netProfitDisplay = "0.00";
    [ObservableProperty] private bool netProfitIsNegative;
    [ObservableProperty] private bool showProfitWarning;
    [ObservableProperty] private string profitWarningText = "";

    public ObservableCollection<ProductSalesRow> TopProducts { get; } = new();
    public ObservableCollection<StockValuationRow> StockValuation { get; } = new();
    public ObservableCollection<DebtorRow> Debtors { get; } = new();
    public ObservableCollection<ProductProfitRow> ProductProfit { get; } = new();

    public ReportsViewModel(IReportsQuery query, ILogger<ReportsViewModel> logger)
    {
        _query = query;
        _logger = logger;
    }

    partial void OnActiveTabChanged(string value)
    {
        _ = LoadActiveTabAsync();
    }

    public async Task LoadAsync() => await LoadActiveTabAsync();

    [RelayCommand]
    private void SetToday()
    {
        string today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        DateFrom = today;
        DateTo = today;
        _ = LoadActiveTabAsync();
    }

    [RelayCommand]
    private void SetThisWeek()
    {
        DateTime today = DateTime.Today;
        int diff = (int)today.DayOfWeek;
        if (diff == 0) diff = 7;
        DateTime monday = today.AddDays(-(diff - 1));
        DateFrom = monday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        DateTo = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _ = LoadActiveTabAsync();
    }

    [RelayCommand]
    private void SetThisMonth()
    {
        DateTime today = DateTime.Today;
        DateTime first = new DateTime(today.Year, today.Month, 1);
        DateFrom = first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        DateTo = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _ = LoadActiveTabAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadActiveTabAsync();

    private async Task LoadActiveTabAsync()
    {
        switch (ActiveTab)
        {
            case "Sales": await LoadSalesAsync(); break;
            case "Stock": await LoadStockAsync(); break;
            case "Debtors": await LoadDebtorsAsync(); break;
            case "Profit": await LoadProfitAsync(); break;
        }
    }

    private async Task LoadSalesAsync()
    {
        try
        {
            IsBusy = true;

            SalesSummary summary = await _query.GetSalesSummaryAsync(DateFrom, DateTo);
            IReadOnlyList<ProductSalesRow> top = await _query.GetTopProductsAsync(DateFrom, DateTo);

            TotalSalesDisplay = new Money(summary.TotalCents).ToString();
            TransactionCountDisplay = summary.TransactionCount.ToString("N0", CultureInfo.InvariantCulture);
            CashDisplay = new Money(summary.CashCents).ToString();
            MpesaDisplay = new Money(summary.MpesaCents).ToString();
            CreditDisplay = new Money(summary.CreditCents).ToString();
            AvgTicketDisplay = summary.TransactionCount > 0
                ? new Money(summary.TotalCents / summary.TransactionCount).ToString()
                : "0.00";

            TopProducts.Clear();
            foreach (ProductSalesRow p in top) TopProducts.Add(p);

            StatusText = summary.TransactionCount == 1
                ? "1 sale in this period"
                : $"{summary.TransactionCount} sales in this period";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sales report load failed");
            StatusText = "Could not load sales report.";
        }
        finally { IsBusy = false; }
    }

    private async Task LoadStockAsync()
    {
        try
        {
            IsBusy = true;
            IReadOnlyList<StockValuationRow> rows = await _query.GetStockValuationAsync();

            StockValuation.Clear();
            long costTotal = 0, retailTotal = 0;
            foreach (StockValuationRow r in rows)
            {
                StockValuation.Add(r);
                costTotal = checked(costTotal + r.TotalCostCents);
                retailTotal = checked(retailTotal + r.TotalRetailCents);
            }

            StockCostDisplay = new Money(costTotal).ToString();
            StockRetailDisplay = new Money(retailTotal).ToString();
            StatusText = rows.Count == 1 ? "1 category in stock" : $"{rows.Count} categories in stock";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stock report load failed");
            StatusText = "Could not load stock report.";
        }
        finally { IsBusy = false; }
    }

    private async Task LoadDebtorsAsync()
    {
        try
        {
            IsBusy = true;
            IReadOnlyList<DebtorRow> rows = await _query.GetDebtorsAsync();

            Debtors.Clear();
            long total = 0;
            foreach (DebtorRow r in rows)
            {
                Debtors.Add(r);
                total = checked(total + r.BalanceCents);
            }

            DebtorsTotalDisplay = new Money(total).ToString();
            StatusText = rows.Count == 1 ? "1 debtor owes money" : $"{rows.Count} debtors owe money";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Debtors report load failed");
            StatusText = "Could not load debtors report.";
        }
        finally { IsBusy = false; }
    }

    private async Task LoadProfitAsync()
    {
        try
        {
            IsBusy = true;

            ProfitSummary summary = await _query.GetProfitSummaryAsync(DateFrom, DateTo);
            IReadOnlyList<ProductProfitRow> products = await _query.GetProductProfitAsync(DateFrom, DateTo);

            RevenueDisplay = new Money(summary.RevenueCents).ToString();
            CogsDisplay = new Money(summary.CostOfGoodsSoldCents).ToString();
            GrossProfitDisplay = new Money(summary.GrossProfitCents).ToString();
            ExpensesDisplay = new Money(summary.ExpensesCents).ToString();
            NetProfitDisplay = new Money(summary.NetProfitCents).ToString();
            NetProfitIsNegative = summary.NetProfitCents < 0;

            ProductProfit.Clear();
            foreach (ProductProfitRow p in products) ProductProfit.Add(p);

            // Warning if profit is estimated (missing costs would overstate profit)
            ShowProfitWarning = summary.UncategorizedCostLineCount > 0 || summary.ExpensesCents == 0;
            if (summary.UncategorizedCostLineCount > 0)
            {
                ProfitWarningText = $"Profit is estimated. {summary.UncategorizedCostLineCount} line(s) sold have no recorded cost, so profit looks higher than reality. Record goods receipts to fix this.";
            }
            else if (summary.ExpensesCents == 0 && summary.TransactionCount > 0)
            {
                ProfitWarningText = "Profit is estimated. No expenses recorded in this period — net profit likely looks higher than reality.";
            }

            StatusText = summary.TransactionCount == 1
                ? "1 sale in this period"
                : $"{summary.TransactionCount} sales in this period";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Profit report load failed");
            StatusText = "Could not load profit report.";
        }
        finally { IsBusy = false; }
    }
}
