using System.Collections.ObjectModel;
using System.Globalization;
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
    private readonly IPriceTierRepository _tiers;
    private readonly ILogger<AddProductViewModel> _logger;

    [ObservableProperty]
    private int? productId;

    [ObservableProperty]
    private string name = "";

    [ObservableProperty]
    private string sku = "";

    [ObservableProperty]
    private string barcode = "";

    [ObservableProperty]
    private string existingBarcode = "";

    [ObservableProperty]
    private Unit? selectedUnit;

    [ObservableProperty]
    private Category? selectedCategory;

    [ObservableProperty]
    private string reorderLevel = "0";

    [ObservableProperty]
    private string retailPriceText = "";

    [ObservableProperty]
    private string wholesalePriceText = "";

    [ObservableProperty]
    private string contractorPriceText = "";

    [ObservableProperty]
    private string errorMessage = "";

    [ObservableProperty]
    private bool isSaving;

    public ObservableCollection<Unit> Units { get; } = new();
    public ObservableCollection<Category> Categories { get; } = new();

    private int? _retailTierId;
    private int? _wholesaleTierId;
    private int? _contractorTierId;

    public bool IsEditMode => ProductId is not null;
    public string WindowTitle => IsEditMode ? "Edit product" : "Add product";
    public string SaveButtonText => IsEditMode ? "Save changes" : "Save product";

    public event EventHandler<int>? Saved;

    public AddProductViewModel(
        IProductRepository products,
        IUnitRepository units,
        ICategoryRepository categories,
        IPriceTierRepository tiers,
        ILogger<AddProductViewModel> logger)
    {
        _products = products;
        _units = units;
        _categories = categories;
        _tiers = tiers;
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

        IReadOnlyList<PriceTier> tiers = await _tiers.ListAsync(includeInactive: false);
        _retailTierId = tiers.FirstOrDefault(t => string.Equals(t.Name, "Retail", StringComparison.OrdinalIgnoreCase))?.Id;
        _wholesaleTierId = tiers.FirstOrDefault(t => string.Equals(t.Name, "Wholesale", StringComparison.OrdinalIgnoreCase))?.Id;
        _contractorTierId = tiers.FirstOrDefault(t => string.Equals(t.Name, "Contractor", StringComparison.OrdinalIgnoreCase))?.Id;

        if (ProductId is null)
        {
            SelectedUnit = Units.FirstOrDefault();
            return;
        }

        Product? existing = await _products.GetByIdAsync(ProductId.Value);
        if (existing is null)
        {
            ErrorMessage = "This product no longer exists.";
            return;
        }

        Name = existing.Name;
        Sku = existing.Sku ?? "";
        SelectedUnit = Units.FirstOrDefault(u => u.Id == existing.BaseUnitId) ?? Units.FirstOrDefault();
        SelectedCategory = Categories.FirstOrDefault(c => c.Id == existing.CategoryId);
        ReorderLevel = (existing.ReorderLevelMilli / 1000).ToString(CultureInfo.InvariantCulture);

        var barcodes = await _products.ListBarcodesAsync(existing.Id);
        ExistingBarcode = barcodes.FirstOrDefault()?.Barcode ?? "";

        if (_retailTierId is not null)
        {
            long? p = await _products.GetPriceAsync(existing.Id, _retailTierId.Value);
            RetailPriceText = p is null ? "" : CentsToShillingsString(p.Value);
        }
        if (_wholesaleTierId is not null)
        {
            long? p = await _products.GetPriceAsync(existing.Id, _wholesaleTierId.Value);
            WholesalePriceText = p is null ? "" : CentsToShillingsString(p.Value);
        }
        if (_contractorTierId is not null)
        {
            long? p = await _products.GetPriceAsync(existing.Id, _contractorTierId.Value);
            ContractorPriceText = p is null ? "" : CentsToShillingsString(p.Value);
        }

        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(SaveButtonText));
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
            ErrorMessage = "Sold by is required.";
            return;
        }

        if (!long.TryParse(ReorderLevel, out long reorderWhole) || reorderWhole < 0)
        {
            ErrorMessage = "Low stock alert must be a non-negative whole number.";
            return;
        }

        string? trimmedBarcode = string.IsNullOrWhiteSpace(Barcode) ? null : Barcode.Trim();
        if (!IsEditMode && trimmedBarcode is not null && !BarcodePattern().IsMatch(trimmedBarcode))
        {
            ErrorMessage = "Code may contain letters, digits and dashes only.";
            return;
        }

        var pricesByTier = new Dictionary<int, long>();
        if (_retailTierId is not null)
        {
            if (!TryParseShillingsToCents(RetailPriceText, out long cents, out string? err))
            {
                ErrorMessage = err ?? "Retail price is not valid.";
                return;
            }
            if (!string.IsNullOrWhiteSpace(RetailPriceText)) pricesByTier[_retailTierId.Value] = cents;
        }
        if (_wholesaleTierId is not null)
        {
            if (!TryParseShillingsToCents(WholesalePriceText, out long cents, out string? err))
            {
                ErrorMessage = err ?? "Wholesale price is not valid.";
                return;
            }
            if (!string.IsNullOrWhiteSpace(WholesalePriceText)) pricesByTier[_wholesaleTierId.Value] = cents;
        }
        if (_contractorTierId is not null)
        {
            if (!TryParseShillingsToCents(ContractorPriceText, out long cents, out string? err))
            {
                ErrorMessage = err ?? "Contractor price is not valid.";
                return;
            }
            if (!string.IsNullOrWhiteSpace(ContractorPriceText)) pricesByTier[_contractorTierId.Value] = cents;
        }

        try
        {
            IsSaving = true;

            if (!IsEditMode && trimmedBarcode is not null &&
                await _products.BarcodeExistsAsync(trimmedBarcode))
            {
                ErrorMessage = $"Code already exists on another product: {trimmedBarcode}";
                return;
            }

            Product product;
            if (IsEditMode)
            {
                product = (await _products.GetByIdAsync(ProductId!.Value))!;
                product.Name = Name.Trim();
                product.Sku = string.IsNullOrWhiteSpace(Sku) ? null : Sku.Trim();
                product.CategoryId = SelectedCategory?.Id;
                product.BaseUnitId = SelectedUnit.Id;
                product.ReorderLevelMilli = reorderWhole * 1000;
            }
            else
            {
                product = new Product
                {
                    Uuid = Guid.NewGuid().ToString("N"),
                    Name = Name.Trim(),
                    Sku = string.IsNullOrWhiteSpace(Sku) ? null : Sku.Trim(),
                    CategoryId = SelectedCategory?.Id,
                    BaseUnitId = SelectedUnit.Id,
                    ReorderLevelMilli = reorderWhole * 1000,
                    IsActive = true,
                };
            }

            int id = await _products.SaveProductWithPricesAsync(product, trimmedBarcode, pricesByTier);
            Saved?.Invoke(this, id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Save product failed for {Name}", Name);
            ErrorMessage = $"Could not save: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    private static string CentsToShillingsString(long cents) =>
        (cents / 100m).ToString("0.##", CultureInfo.InvariantCulture);

    private static bool TryParseShillingsToCents(string raw, out long cents, out string? error)
    {
        cents = 0;
        error = null;

        string trimmed = raw?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            return true;
        }

        if (!decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal shillings))
        {
            error = "Enter a price (e.g. 650 or 2,500.00).";
            return false;
        }

        if (shillings < 0)
        {
            error = "Price cannot be negative.";
            return false;
        }

        cents = (long)decimal.Round(shillings * 100m, 0, MidpointRounding.AwayFromZero);
        return true;
    }

    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$")]
    private static partial Regex BarcodePattern();
}
