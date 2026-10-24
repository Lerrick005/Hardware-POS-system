using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Catalog;
using Pos.Desktop.Views;

namespace Pos.Desktop.ViewModels;

public partial class ProductsViewModel : ObservableObject
{
    private readonly IProductRepository _repo;
    private readonly ILogger<ProductsViewModel> _logger;
    private CancellationTokenSource? _searchCts;

    [ObservableProperty]
    private string searchTerm = "";

    [ObservableProperty]
    private string statusText = "";

    [ObservableProperty]
    private bool isBusy;

    public ObservableCollection<ProductListItem> Products { get; } = new();

    /// <summary>Set by App composition. Null parameter = create, id = edit.</summary>
    public Func<int?, AddProductView>? CreateProductDialog { get; set; }

    /// <summary>Set by App composition. Opens the categories management dialog.</summary>
    public Func<CategoriesView>? CreateCategoriesDialog { get; set; }

    /// <summary>Set by App composition. Takes a product id, returns the dialog ready to show.</summary>
    public Func<int, AdjustStockView>? CreateAdjustStockDialog { get; set; }

    public ProductsViewModel(IProductRepository repo, ILogger<ProductsViewModel> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task RefreshAsync()
    {
        await LoadAsync(SearchTerm);
    }

    partial void OnSearchTermChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        CancellationToken token = _searchCts.Token;
        _ = DebouncedLoadAsync(value, token);
    }

    private async Task DebouncedLoadAsync(string term, CancellationToken ct)
    {
        try
        {
            await Task.Delay(200, ct);
            await LoadAsync(term, ct);
        }
        catch (OperationCanceledException)
        {
            // superseded by newer keystroke
        }
    }

    private async Task LoadAsync(string term, CancellationToken ct = default)
    {
        try
        {
            IsBusy = true;
            IReadOnlyList<ProductListItem> items = string.IsNullOrWhiteSpace(term)
                ? await _repo.ListAsync(includeInactive: false, ct)
                : await _repo.SearchAsync(term, limit: 500, ct);

            if (ct.IsCancellationRequested)
            {
                return;
            }

            Products.Clear();
            foreach (ProductListItem p in items)
            {
                Products.Add(p);
            }

            StatusText = items.Count == 1 ? "1 product" : $"{items.Count} products";
        }
        catch (OperationCanceledException)
        {
            // superseded
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Products load failed for term {Term}", term);
            StatusText = "Could not load products.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddProductAsync()
    {
        if (CreateProductDialog is null)
        {
            return;
        }

        AddProductView dialog = CreateProductDialog(null);
        if (dialog.ShowDialog() == true)
        {
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task EditProductAsync(ProductListItem? item)
    {
        if (item is null || CreateProductDialog is null)
        {
            return;
        }

        AddProductView dialog = CreateProductDialog(item.Id);
        if (dialog.ShowDialog() == true)
        {
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task ArchiveProductAsync(ProductListItem? item)
    {
        if (item is null)
        {
            return;
        }

        string stockNote = item.StockMilli > 0
            ? $" This product still has {new Pos.Domain.Quantity(item.StockMilli)} units in stock."
            : "";

        ConfirmDialog dlg = ConfirmDialog.CreateDanger(
            "Remove this product?",
            $"{item.Name} will no longer appear in the list.{stockNote} " +
            "Stock history is kept and can be restored later.",
            confirmLabel: "Remove product");

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await _repo.ArchiveAsync(item.Id);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Archive product {Id} failed", item.Id);
            StatusText = $"Could not remove product: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task AdjustStockAsync(ProductListItem? item)
    {
        if (item is null || CreateAdjustStockDialog is null) return;

        AdjustStockView dialog = CreateAdjustStockDialog(item.Id);
        if (dialog.ShowDialog() == true)
        {
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private void ManageCategories()
    {
        if (CreateCategoriesDialog is null)
        {
            return;
        }

        CategoriesView dialog = CreateCategoriesDialog();
        dialog.ShowDialog();
    }

    [RelayCommand]
    private async Task Refresh()
    {
        await RefreshAsync();
    }
}

