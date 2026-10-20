using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
    private readonly SettingsViewModel _settings;

    [ObservableProperty] private object? currentView;
    [ObservableProperty] private string currentTitle = "Dashboard";
    [ObservableProperty] private string selectedNav = "Dashboard";
    [ObservableProperty] private string userLabel = "Not signed in";

    public MainWindowViewModel(
        DashboardViewModel dashboard,
        ProductsViewModel products,
        ReceiveGoodsViewModel receiveGoods,
        CustomersViewModel customers,
        SuppliersViewModel suppliers,
        SellViewModel sell,
        ReportsViewModel reports,
        SettingsViewModel settings)
    {
        _dashboard = dashboard;
        _products = products;
        _receiveGoods = receiveGoods;
        _customers = customers;
        _suppliers = suppliers;
        _sell = sell;
        _reports = reports;
        _settings = settings;
        ShowDashboard();
    }

    [RelayCommand]
    private void ShowDashboard()
    {
        _ = _dashboard.LoadAsync();
        CurrentView = _dashboard;
        CurrentTitle = "Dashboard";
        SelectedNav = "Dashboard";
    }

    [RelayCommand]
    private async Task ShowSellAsync()
    {
        await _sell.LoadAsync();
        CurrentView = _sell;
        CurrentTitle = "Sell";
        SelectedNav = "Sell";
    }

    [RelayCommand]
    private async Task ShowProductsAsync()
    {
        await _products.RefreshAsync();
        CurrentView = _products;
        CurrentTitle = "Products";
        SelectedNav = "Products";
    }

    [RelayCommand]
    private async Task ShowStockAsync()
    {
        await _receiveGoods.RefreshAsync();
        CurrentView = _receiveGoods;
        CurrentTitle = "Stock";
        SelectedNav = "Stock";
    }

    [RelayCommand]
    private async Task ShowCustomersAsync()
    {
        await _customers.RefreshAsync();
        CurrentView = _customers;
        CurrentTitle = "Customers";
        SelectedNav = "Customers";
    }

    [RelayCommand]
    private async Task ShowSuppliersAsync()
    {
        await _suppliers.RefreshAsync();
        CurrentView = _suppliers;
        CurrentTitle = "Suppliers";
        SelectedNav = "Suppliers";
    }

    [RelayCommand]
    private async Task ShowReportsAsync()
    {
        await _reports.LoadAsync();
        CurrentView = _reports;
        CurrentTitle = "Reports";
        SelectedNav = "Reports";
    }

    [RelayCommand]
    private void ShowSettings()
    {
        _ = _settings.LoadAsync();
        CurrentView = _settings;
        CurrentTitle = "Settings";
        SelectedNav = "Settings";
    }
}
