using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Cash;
using Pos.Application.Users;
using Pos.Desktop.Views;

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
    private readonly ICashSessionQuery _cashQuery;
    private readonly IUserSession _session;
    private readonly ILogger<MainWindowViewModel> _logger;

    [ObservableProperty] private object? currentView;
    [ObservableProperty] private string currentTitle = "Dashboard";
    [ObservableProperty] private string selectedNav = "Dashboard";
    [ObservableProperty] private string userLabel = "Not signed in";
    [ObservableProperty] private string shiftLabel = "No shift";
    [ObservableProperty] private string shiftStatus = "None";
    [ObservableProperty] private bool hasOpenShift;
    [ObservableProperty] private string shiftExpectedDisplay = "0.00";

    public bool IsAdmin => string.Equals(_session.CurrentUserRole, "ADMIN", StringComparison.Ordinal);
    public bool IsManagerOrAdmin => IsAdmin || string.Equals(_session.CurrentUserRole, "MANAGER", StringComparison.Ordinal);

    public Func<OpenShiftView>? CreateOpenShiftDialog { get; set; }
    public Func<CloseShiftView>? CreateCloseShiftDialog { get; set; }

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
        ICashSessionQuery cashQuery,
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
        _cashQuery = cashQuery;
        _session = session;
        _logger = logger;

        _ = RefreshShiftAsync();
        ShowDashboard();
    }

    public async Task RefreshShiftAsync()
    {
        try
        {
            OpenCashSession? open = await _cashQuery.GetOpenForUserAsync(_session.RequireUserId);
            if (open is null)
            {
                HasOpenShift = false;
                ShiftLabel = "No shift open";
                ShiftStatus = "None";
                ShiftExpectedDisplay = "0.00";
            }
            else
            {
                HasOpenShift = true;
                ShiftLabel = $"Shift open · {open.UserName ?? "you"}";
                ShiftStatus = "Open";
                ShiftExpectedDisplay = new Pos.Domain.Money(open.ExpectedCashCents).ToString();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refresh shift failed");
        }
    }

    [RelayCommand]
    private async Task OpenShiftAsync()
    {
        if (CreateOpenShiftDialog is null) return;
        OpenShiftView dlg = CreateOpenShiftDialog();
        if (dlg.ShowDialog() == true) await RefreshShiftAsync();
    }

    [RelayCommand]
    private async Task CloseShiftAsync()
    {
        if (CreateCloseShiftDialog is null) return;
        CloseShiftView dlg = CreateCloseShiftDialog();
        if (dlg.ShowDialog() == true) await RefreshShiftAsync();
    }

    [RelayCommand] private void ShowDashboard() { _ = _dashboard.LoadAsync(); CurrentView = _dashboard; CurrentTitle = "Dashboard"; SelectedNav = "Dashboard"; }
    [RelayCommand] private async Task ShowSellAsync() { await _sell.LoadAsync(); await RefreshShiftAsync(); CurrentView = _sell; CurrentTitle = "Sell"; SelectedNav = "Sell"; }
    [RelayCommand] private async Task ShowProductsAsync() { await _products.RefreshAsync(); CurrentView = _products; CurrentTitle = "Products"; SelectedNav = "Products"; }
    [RelayCommand] private async Task ShowStockAsync() { await _receiveGoods.RefreshAsync(); CurrentView = _receiveGoods; CurrentTitle = "Stock"; SelectedNav = "Stock"; }
    [RelayCommand] private async Task ShowCustomersAsync() { await _customers.RefreshAsync(); CurrentView = _customers; CurrentTitle = "Customers"; SelectedNav = "Customers"; }
    [RelayCommand] private async Task ShowSuppliersAsync() { await _suppliers.RefreshAsync(); CurrentView = _suppliers; CurrentTitle = "Suppliers"; SelectedNav = "Suppliers"; }
    [RelayCommand] private async Task ShowReportsAsync() { await _reports.LoadAsync(); CurrentView = _reports; CurrentTitle = "Reports"; SelectedNav = "Reports"; }

    [RelayCommand]
    private async Task ShowExpensesAsync()
    {
        if (!IsManagerOrAdmin) return;
        await _expenses.LoadAsync();
        CurrentView = _expenses;
        CurrentTitle = "Expenses";
        SelectedNav = "Expenses";
    }

    [RelayCommand]
    private void ShowSettings()
    {
        if (!IsAdmin) return;
        _ = _settingsHub.LoadAsync();
        CurrentView = _settingsHub;
        CurrentTitle = "Settings";
        SelectedNav = "Settings";
    }
}

