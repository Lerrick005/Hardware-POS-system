using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Catalog;
using Pos.Domain.Catalog;

namespace Pos.Desktop.ViewModels;

public partial class AddProductViewModel : ObservableObject
{
    private readonly IProductRepository _products;
    private readonly IUnitRepository _units;
    private readonly ICategoryRepository _categories;
    private readonly ILogger<AddProductViewModel> _logger;

    [ObservableProperty]
    private string name = "";

    [ObservableProperty]
    private string sku = "";

    [ObservableProperty]
    private string barcode = "";

    [ObservableProperty]
    private Unit? selectedUnit;

    [ObservableProperty]
    private Category? selectedCategory;

    [ObservableProperty]
    private string reorderLevel = "0";

    [ObservableProperty]
    private string errorMessage = "";

    [ObservableProperty]
    private bool isSaving;

    public ObservableCollection<Unit> Units { get; } = new();
    public ObservableCollection<Category> Categories { get; } = new();

    public event EventHandler<int>? Saved;

    public AddProductViewModel(
        IProductRepository products,
        IUnitRepository units,
        ICategoryRepository categories,
        ILogger<AddProductViewModel> logger)
    {
        _products = products;
        _units = units;
        _categories = categories;
        _logger = logger;
    }

    public async Task LoadAsync()
    {
        Units.Clear();
        foreach (Unit u in await _units.ListAsync(includeInactive: false))
        {
            Units.Add(u);
        }

        Categories.Clear();
        foreach (Category c in await _categories.ListAsync(includeInactive: false))
        {
            Categories.Add(c);
        }

        SelectedUnit = Units.FirstOrDefault();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = "";

        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Name is required.";
            return;
        }

        if (SelectedUnit is null)
        {
            ErrorMessage = "Base unit is required.";
            return;
        }

        if (!long.TryParse(ReorderLevel, out long reorderWhole) || reorderWhole < 0)
        {
            ErrorMessage = "Reorder level must be a non-negative whole number.";
            return;
        }

        string? trimmedBarcode = string.IsNullOrWhiteSpace(Barcode) ? null : Barcode.Trim();
        if (trimmedBarcode is not null && !BarcodePattern().IsMatch(trimmedBarcode))
        {
            ErrorMessage = "Barcode may contain letters, digits and dashes only.";
            return;
        }

        try
        {
            IsSaving = true;

            if (trimmedBarcode is not null && await _products.BarcodeExistsAsync(trimmedBarcode))
            {
                ErrorMessage = $"Barcode already exists on another product: {trimmedBarcode}";
                return;
            }

            Product product = new()
            {
                Uuid = Guid.NewGuid().ToString("N"),
                Name = Name.Trim(),
                Sku = string.IsNullOrWhiteSpace(Sku) ? null : Sku.Trim(),
                CategoryId = SelectedCategory?.Id,
                BaseUnitId = SelectedUnit.Id,
                ReorderLevelMilli = reorderWhole * 1000,
                IsActive = true,
            };

            int id = await _products.InsertAsync(product);

            if (trimmedBarcode is not null)
            {
                await _products.AddBarcodeAsync(new ProductBarcode
                {
                    Barcode = trimmedBarcode,
                    ProductId = id,
                    IsPrimary = true,
                });
            }

            Saved?.Invoke(this, id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AddProduct failed for {Name}", Name);
            ErrorMessage = $"Could not save: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$")]
    private static partial Regex BarcodePattern();
}
