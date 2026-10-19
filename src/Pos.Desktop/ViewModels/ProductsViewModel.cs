using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Catalog;
using Pos.Desktop.Views;
using Pos.Domain.Catalog;

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

    public ObservableCollection<Product> Products { get; } = new();

    /// <summary>Set by App composition so this VM does not depend on the DI container.</summary>
    public Func<AddProductView>? CreateAddProductDialog { get; set; }

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
            IReadOnlyList<Product> items = string.IsNullOrWhiteSpace(term)
                ? await _repo.ListAsync(includeInactive: false, ct)
                : await _repo.SearchAsync(term, limit: 500, ct);

            if (ct.IsCancellationRequested)
            {
                return;
            }

            Products.Clear();
            foreach (Product p in items)
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
        if (CreateAddProductDialog is null)
        {
            return;
        }

        AddProductView dialog = CreateAddProductDialog();
        bool? ok = dialog.ShowDialog();
        if (ok == true)
        {
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task Refresh()
    {
        await RefreshAsync();
    }
}
