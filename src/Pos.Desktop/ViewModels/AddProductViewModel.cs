using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Catalog;
using Pos.Application.Users;
using Pos.Domain.Catalog;

namespace Pos.Desktop.ViewModels;

public partial class AddProductViewModel : ObservableObject
{
    private readonly IProductRepository _products;
    private readonly IUnitRepository _units;
    private readonly ICategoryRepository _categories;
    private readonly IPriceTierRepository _tiers;
    private readonly IUserSession _session;
    private readonly ILogger<AddProductViewModel> _logger;

    private IReadOnlyList<Unit> _allUnits = Array.Empty<Unit>();
    private int _retailTierId = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditMode))]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    private int? productId;

    [ObservableProperty] private string name = "";
    [ObservableProperty] private string sku = "";
    [ObservableProperty] private string barcode = "";
    [ObservableProperty] private string existingBarcode = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AlternativeUnitsForPicker))]
    private Unit? selectedUnit;

    [ObservableProperty] private Category? selectedCategory;
    [ObservableProperty] private string reorderLevel = "0";
    [ObservableProperty] private string retailPriceText = "";
    [ObservableProperty] private string openingStockText = "";
    [ObservableProperty] private string openingCostText = "";
    [ObservableProperty] private string errorMessage = "";
    [ObservableProperty] private bool isSaving;

    public ObservableCollection<Unit> Units { get; } = new();
    public ObservableCollection<Category> Categories { get; } = new();
    public ObservableCollection<ProductUnitEditorViewModel> SellingUnits { get; } = new();

    public IReadOnlyList<Unit> AlternativeUnitsForPicker =>
        _allUnits.Where(u => SelectedUnit is null || u.Id != SelectedUnit.Id).ToList();

    public bool IsEditMode => ProductId is not null;
    public string WindowTitle => IsEditMode ? "Edit product" : "Add product";
    public string SaveButtonText => IsEditMode ? "Save changes" : "Save product";

    public event EventHandler<int>? Saved;

    public AddProductViewModel(
        IProductRepository products,
        IUnitRepository units,
        ICategoryRepository categories,
        IPriceTierRepository tiers,
        IUserSession session,
        ILogger<AddProductViewModel> logger)
    {
        _products = products;
        _units = units;
        _categories = categories;
        _tiers = tiers;
        _session = session;
        _logger = logger;
    }

    public async Task LoadAsync()
    {
        _allUnits = await _units.ListAsync(includeInactive: false);
        IReadOnlyList<PriceTier> tierList = await _tiers.ListAsync(includeInactive: false);
        _retailTierId = tierList.FirstOrDefault(t => string.Equals(t.Name, "Retail", StringComparison.OrdinalIgnoreCase))?.Id
                        ?? tierList.FirstOrDefault()?.Id ?? 1;

        Units.Clear();
        foreach (Unit u in _allUnits) Units.Add(u);

        Categories.Clear();
        foreach (Category c in await _categories.ListAsync(includeInactive: false)) Categories.Add(c);

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

        // Load base price (retail only)
        long? retail = await _products.GetPriceForUnitAsync(existing.Id, _retailTierId, existing.BaseUnitId);
        RetailPriceText = retail is null ? "" : CentsToShillingsString(retail.Value);

        // Load alternative units + their retail prices
        IReadOnlyList<ProductUnitInfo> unitInfos = await _products.ListProductUnitsAsync(existing.Id);
        IReadOnlyList<ProductPriceInfo> allPrices = await _products.ListAllPricesAsync(existing.Id);

        foreach (ProductUnitInfo u in unitInfos.Where(u => !u.IsBase))
        {
            ProductUnitEditorViewModel editor = new(AlternativeUnitsForPicker)
            {
                SelectedUnit = _allUnits.FirstOrDefault(x => x.Id == u.UnitId),
                FactorText = (u.FactorMilli / 1000m).ToString("0.###", CultureInfo.InvariantCulture),
            };

            ProductPriceInfo? retailPrice = allPrices.FirstOrDefault(p => p.TierId == _retailTierId && p.UnitId == u.UnitId);
            if (retailPrice is not null) editor.RetailPriceText = CentsToShillingsString(retailPrice.PriceCents);

            SellingUnits.Add(editor);
        }
    }

    [RelayCommand]
    private void AddSellingUnit()
    {
        ErrorMessage = "";

        if (SelectedUnit is null)
        {
            ErrorMessage = "Pick a sold-by unit first.";
            return;
        }

        List<Unit> available = _allUnits.Where(u => u.Id != SelectedUnit.Id).ToList();
        if (available.Count == 0)
        {
            ErrorMessage = "No alternative units are configured in the system.";
            return;
        }

        HashSet<int> used = SellingUnits
            .Where(e => e.SelectedUnit is not null)
            .Select(e => e.SelectedUnit!.Id)
            .ToHashSet();

        List<Unit> remaining = available.Where(u => !used.Contains(u.Id)).ToList();
        if (remaining.Count == 0)
        {
            ErrorMessage = "All available units are already added.";
            return;
        }

        ProductUnitEditorViewModel editor = new(available)
        {
            SelectedUnit = remaining[0],
        };
        SellingUnits.Add(editor);
    }

    [RelayCommand]
    private void RemoveSellingUnit(ProductUnitEditorViewModel? editor)
    {
        if (editor is null) return;
        SellingUnits.Remove(editor);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = "";

        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Product name is required.";
            return;
        }

        if (SelectedUnit is null)
        {
            ErrorMessage = "Sold by (base unit) is required.";
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
            ErrorMessage = "Barcode may contain letters, digits and dashes only.";
            return;
        }

        // Base price
        var basePrices = new Dictionary<int, long>();
        if (!string.IsNullOrWhiteSpace(RetailPriceText))
        {
            if (!TryParseShillingsToCents(RetailPriceText, out long cents, out string? err))
            {
                ErrorMessage = err ?? "Selling price is not valid.";
                return;
            }
            basePrices[_retailTierId] = cents;
        }

        // Alternative units
        var alternativeUnits = new List<ProductUnitSave>();
        var seenUnitIds = new HashSet<int>();
        foreach (ProductUnitEditorViewModel editor in SellingUnits)
        {
            if (editor.SelectedUnit is null)
            {
                ErrorMessage = "Every selling unit row must have a unit selected.";
                return;
            }

            if (editor.SelectedUnit.Id == SelectedUnit.Id)
            {
                ErrorMessage = "A selling unit cannot be the same as the sold-by unit.";
                return;
            }

            if (!seenUnitIds.Add(editor.SelectedUnit.Id))
            {
                ErrorMessage = $"Unit '{editor.SelectedUnit.Name}' is listed twice. Remove the duplicate.";
                return;
            }

            if (!decimal.TryParse(editor.FactorText?.Trim(), NumberStyles.Number,
                    CultureInfo.InvariantCulture, out decimal factorValue) || factorValue <= 0)
            {
                ErrorMessage = $"How many {SelectedUnit.Name} in one {editor.SelectedUnit.Name}? Enter a positive number.";
                return;
            }

            long factorMilli = (long)decimal.Round(factorValue * 1000m, 0, MidpointRounding.AwayFromZero);

            var prices = new List<ProductUnitPriceSave>();
            if (!string.IsNullOrWhiteSpace(editor.RetailPriceText))
            {
                if (!TryParseShillingsToCents(editor.RetailPriceText, out long c, out string? e))
                {
                    ErrorMessage = $"Price for '{editor.SelectedUnit.Name}': {e}";
                    return;
                }
                prices.Add(new ProductUnitPriceSave(_retailTierId, c));
            }

            alternativeUnits.Add(new ProductUnitSave(editor.SelectedUnit.Id, factorMilli, prices));
        }

        // Opening stock (create mode only)
        long openingStockMilli = 0;
        long openingCostCents = 0;

        if (!IsEditMode && !string.IsNullOrWhiteSpace(OpeningStockText))
        {
            if (!Pos.Domain.Quantity.TryParse(OpeningStockText, out Pos.Domain.Quantity qty, out string? qtyErr))
            {
                ErrorMessage = $"Opening stock: {qtyErr}";
                return;
            }
            openingStockMilli = qty.Milli;

            if (!string.IsNullOrWhiteSpace(OpeningCostText))
            {
                if (!TryParseShillingsToCents(OpeningCostText, out long c, out string? costErr))
                {
                    ErrorMessage = $"Opening cost: {costErr}";
                    return;
                }
                openingCostCents = c;
            }
        }

        try
        {
            IsSaving = true;

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

            int id;
            if (IsEditMode)
            {
                id = await _products.SaveProductWithAllPricesAsync(
                    product, trimmedBarcode, basePrices, alternativeUnits);
            }
            else
            {
                id = await _products.SaveProductWithAllPricesAndOpeningStockAsync(
                    product, trimmedBarcode, basePrices, alternativeUnits,
                    openingStockMilli, openingCostCents,
                    DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    _session.RequireUserId);
            }

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
        if (trimmed.Length == 0) return true;

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
