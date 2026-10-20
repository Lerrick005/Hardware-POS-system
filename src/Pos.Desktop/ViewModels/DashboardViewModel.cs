using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Reports;
using Pos.Domain;

namespace Pos.Desktop.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IDashboardQuery _query;
    private readonly IReportsQuery _reports;
    private readonly ILogger<DashboardViewModel> _logger;

    [ObservableProperty] private string greeting = "Welcome";
    [ObservableProperty] private string todayLabel = "";
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool showSetupState = true;

    [ObservableProperty] private string todaySalesDisplay = "0.00";
    [ObservableProperty] private string todayTransactionsDisplay = "0";
    [ObservableProperty] private string todayCashDisplay = "0.00";
    [ObservableProperty] private string todayMpesaDisplay = "0.00";
    [ObservableProperty] private string todayCreditDisplay = "0.00";
    [ObservableProperty] private string lowStockCountDisplay = "0";
    [ObservableProperty] private string outOfStockCountDisplay = "0";
    [ObservableProperty] private string debtorsTotalDisplay = "0.00";
    [ObservableProperty] private string debtorCountDisplay = "0";

    [ObservableProperty] private string monthRevenueDisplay = "0.00";
    [ObservableProperty] private string monthExpensesDisplay = "0.00";
    [ObservableProperty] private string monthNetProfitDisplay = "0.00";
    [ObservableProperty] private bool monthNetProfitIsNegative;
    [ObservableProperty] private bool showProfitWarning;
    [ObservableProperty] private string profitWarningText = "";

    [ObservableProperty] private bool hasAnyProducts;
    [ObservableProperty] private bool hasAnyCustomers;
    [ObservableProperty] private bool hasAnySales;

    public ObservableCollection<LowStockItem> LowStock { get; } = new();

    public Action? NavigateToSell { get; set; }
    public Action? NavigateToStock { get; set; }
    public Action? NavigateToProducts { get; set; }
    public Action? NavigateToCustomers { get; set; }
    public Action? NavigateToReports { get; set; }

    public DashboardViewModel(
        IDashboardQuery query,
        IReportsQuery reports,
        ILogger<DashboardViewModel> logger)
    {
        _query = query;
        _reports = reports;
        _logger = logger;
    }

    public async Task LoadAsync()
    {
        try
        {
            IsBusy = true;

            DateTime now = DateTime.Now;
            TodayLabel = now.ToString("dddd, dd MMMM yyyy", CultureInfo.InvariantCulture);
            Greeting = now.Hour switch
            {
                < 12 => "Good morning",
                < 17 => "Good afternoon",
                _ => "Good evening",
            };

            string today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            DashboardSnapshot snapshot = await _query.GetSnapshotAsync(today);

            HasAnyProducts = snapshot.HasAnyProducts;
            HasAnyCustomers = snapshot.HasAnyCustomers;
            HasAnySales = snapshot.HasAnySales;
            ShowSetupState = !snapshot.HasAnyProducts && !snapshot.HasAnySales;

            TodaySalesDisplay = new Money(snapshot.TodaySalesCents).ToString();
            TodayTransactionsDisplay = snapshot.TodayTransactionCount.ToString("N0", CultureInfo.InvariantCulture);
            TodayCashDisplay = new Money(snapshot.TodayCashCents).ToString();
            TodayMpesaDisplay = new Money(snapshot.TodayMpesaCents).ToString();
            TodayCreditDisplay = new Money(snapshot.TodayCreditCents).ToString();
            LowStockCountDisplay = snapshot.LowStockCount.ToString("N0", CultureInfo.InvariantCulture);
            OutOfStockCountDisplay = snapshot.OutOfStockCount.ToString("N0", CultureInfo.InvariantCulture);
            DebtorsTotalDisplay = new Money(snapshot.DebtorsTotalCents).ToString();
            DebtorCountDisplay = snapshot.DebtorCount.ToString("N0", CultureInfo.InvariantCulture);

            IReadOnlyList<LowStockItem> low = await _query.GetTopLowStockAsync(limit: 5);
            LowStock.Clear();
            foreach (LowStockItem item in low) LowStock.Add(item);

            // Month-to-date profit
            string monthStart = new DateTime(now.Year, now.Month, 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            ProfitSummary profit = await _reports.GetProfitSummaryAsync(monthStart, today);

            MonthRevenueDisplay = new Money(profit.RevenueCents).ToString();
            MonthExpensesDisplay = new Money(profit.ExpensesCents).ToString();
            MonthNetProfitDisplay = new Money(profit.NetProfitCents).ToString();
            MonthNetProfitIsNegative = profit.NetProfitCents < 0;

            ShowProfitWarning = profit.UncategorizedCostLineCount > 0;
            if (ShowProfitWarning)
            {
                ProfitWarningText = $"{profit.UncategorizedCostLineCount} sale line(s) have no cost recorded. Net profit is estimated.";
            }

            StatusText = snapshot.TodayTransactionCount == 1
                ? "1 sale today"
                : $"{snapshot.TodayTransactionCount} sales today";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dashboard load failed");
            StatusText = "Could not load dashboard.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand] private async Task RefreshAsync() => await LoadAsync();
    [RelayCommand] private void GoSell() => NavigateToSell?.Invoke();
    [RelayCommand] private void GoStock() => NavigateToStock?.Invoke();
    [RelayCommand] private void GoProducts() => NavigateToProducts?.Invoke();
    [RelayCommand] private void GoCustomers() => NavigateToCustomers?.Invoke();
    [RelayCommand] private void GoReports() => NavigateToReports?.Invoke();
}
