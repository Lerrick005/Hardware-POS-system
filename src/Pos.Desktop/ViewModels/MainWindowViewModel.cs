using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Pos.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly DashboardViewModel _dashboard;
    private readonly ProductsViewModel _products;
    private readonly ReceiveGoodsViewModel _receiveGoods;
    private readonly CustomersViewModel _customers;

    [ObservableProperty]
    private object? currentView;

    [ObservableProperty]
    private string currentTitle = "Dashboard";

    [ObservableProperty]
    private string selectedNav = "Dashboard";

    [ObservableProperty]
    private string userLabel = "Not signed in";

    public MainWindowViewModel(
        DashboardViewModel dashboard,
        ProductsViewModel products,
        ReceiveGoodsViewModel receiveGoods,
        CustomersViewModel customers)
    {
        _dashboard = dashboard;
        _products = products;
        _receiveGoods = receiveGoods;
        _customers = customers;
        ShowDashboard();
    }

    [RelayCommand]
    private void ShowDashboard()
    {
        CurrentView = _dashboard;
        CurrentTitle = "Dashboard";
        SelectedNav = "Dashboard";
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
    private void ShowSell() => NavigateToPlaceholder("Sell", "The selling screen is being built.");

    [RelayCommand]
    private void ShowReports() => NavigateToPlaceholder("Reports", "Daily sales, profit and stock reports will appear here.");

    [RelayCommand]
    private void ShowSettings() => NavigateToPlaceholder("Settings", "Shop profile, printer and backup settings will appear here.");

    private void NavigateToPlaceholder(string title, string message)
    {
        CurrentView = new PlaceholderViewModel(title, message);
        CurrentTitle = title;
        SelectedNav = title;
    }
}
