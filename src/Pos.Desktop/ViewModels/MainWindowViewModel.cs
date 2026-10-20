using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Users;

namespace Pos.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly DashboardViewModel _dashboard;
    private readonly ProductsViewModel _products;
    private readonly ReceiveGoodsViewModel _receiveGoods;
    private readonly CustomersViewModel _customers;
    private readonly SuppliersViewModel _suppliers;
    private readonly SellViewModel _sell;
    private readonly ReportsViewModel _reports;
    private readonly SettingsHubViewModel _settingsHub;
    private readonly ExpensesViewModel _expenses;
    private readonly IUserSession _session;
    private readonly ILogger<MainWindowViewModel> _logger;

    [ObservableProperty] private object? currentView;
    [ObservableProperty] private string currentTitle = "Dashboard";
    [ObservableProperty] private string selectedNav = "Dashboard";
    [ObservableProperty] private string userLabel = "Not signed in";

    public bool IsAdmin => string.Equals(_session.CurrentUserRole, "ADMIN", StringComparison.Ordinal);
    public bool IsManagerOrAdmin => IsAdmin || string.Equals(_session.CurrentUserRole, "MANAGER", StringComparison.Ordinal);

    public MainWindowViewModel(
        DashboardViewModel dashboard,
        ProductsViewModel products,
        ReceiveGoodsViewModel receiveGoods,
        CustomersViewModel customers,
        SuppliersViewModel suppliers,
        SellViewModel sell,
        ReportsViewModel reports,
        SettingsHubViewModel settingsHub,
        ExpensesViewModel expenses,
        IUserSession session,
        ILogger<MainWindowViewModel> logger)
    {
        _dashboard = dashboard;
        _products = products;
        _receiveGoods = receiveGoods;
        _customers = customers;
        _suppliers = suppliers;
        _sell = sell;
        _reports = reports;
        _settingsHub = settingsHub;
        _expenses = expenses;
        _session = session;
        _logger = logger;
        ShowDashboard();
    }

    [RelayCommand] private void ShowDashboard() { _ = _dashboard.LoadAsync(); CurrentView = _dashboard; CurrentTitle = "Dashboard"; SelectedNav = "Dashboard"; }
    [RelayCommand] private async Task ShowSellAsync() { await _sell.LoadAsync(); CurrentView = _sell; CurrentTitle = "Sell"; SelectedNav = "Sell"; }
    [RelayCommand] private async Task ShowProductsAsync() { await _products.RefreshAsync(); CurrentView = _products; CurrentTitle = "Products"; SelectedNav = "Products"; }
    [RelayCommand] private async Task ShowStockAsync() { await _receiveGoods.RefreshAsync(); CurrentView = _receiveGoods; CurrentTitle = "Stock"; SelectedNav = "Stock"; }
    [RelayCommand] private async Task ShowCustomersAsync() { await _customers.RefreshAsync(); CurrentView = _customers; CurrentTitle = "Customers"; SelectedNav = "Customers"; }
    [RelayCommand] private async Task ShowSuppliersAsync() { await _suppliers.RefreshAsync(); CurrentView = _suppliers; CurrentTitle = "Suppliers"; SelectedNav = "Suppliers"; }
    [RelayCommand] private async Task ShowReportsAsync() { await _reports.LoadAsync(); CurrentView = _reports; CurrentTitle = "Reports"; SelectedNav = "Reports"; }

    [RelayCommand]
    private async Task ShowExpensesAsync()
    {
        if (!IsManagerOrAdmin)
        {
            _logger.LogWarning("Non-manager tried to open Expenses");
            return;
        }
        await _expenses.LoadAsync();
        CurrentView = _expenses;
        CurrentTitle = "Expenses";
        SelectedNav = "Expenses";
    }

    [RelayCommand]
    private void ShowSettings()
    {
        if (!IsAdmin)
        {
            _logger.LogWarning("Non-admin user attempted to open Settings");
            return;
        }
        _ = _settingsHub.LoadAsync();
        CurrentView = _settingsHub;
        CurrentTitle = "Settings";
        SelectedNav = "Settings";
    }
}
