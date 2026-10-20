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

    private IReadOnlyList<Unit> _allUnits = Array.Empty<Unit>();
    private IReadOnlyList<PriceTier> _allTiers = Array.Empty<PriceTier>();

    private int? _retailTierId;
    private int? _wholesaleTierId;
    private int? _contractorTierId;

    [ObservableProperty] private int? productId;
    [ObservableProperty] private string name = "";
    [ObservableProperty] private string sku = "";
    [ObservableProperty] private string barcode = "";
    [ObservableProperty] private string existingBarcode = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AlternativeUnitsForPicker))]
    [NotifyPropertyChangedFor(nameof(IsEditMode))]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    private Unit? selectedUnit;

    [ObservableProperty] private Category? selectedCategory;
    [ObservableProperty] private string reorderLevel = "0";
    [ObservableProperty] private string retailPriceText = "";
    [ObservableProperty] private string wholesalePriceText = "";
    [ObservableProperty] private string contractorPriceText = "";
    [ObservableProperty] private string errorMessage = "";
    [ObservableProperty] private bool isSaving;

    public ObservableCollection<Unit> Units { get; } = new();
    public ObservableCollection<Category> Categories { get; } = new();
    public ObservableCollection<ProductUnitEditorViewModel> SellingUnits { get; } = new();

    /// <summary>Units available for a new alternative row — everything except the base unit.</summary>
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
        _allUnits = await _units.ListAsync(includeInactive: false);
        _allTiers = await _tiers.ListAsync(includeInactive: false);

        Units.Clear();
        foreach (Unit u in _allUnits) Units.Add(u);

        Categories.Clear();
        foreach (Category c in await _categories.ListAsync(includeInactive: false)) Categories.Add(c);

        _retailTierId = _allTiers.FirstOrDefault(t => string.Equals(t.Name, "Retail", StringComparison.OrdinalIgnoreCase))?.Id;
        _wholesaleTierId = _allTiers.FirstOrDefault(t => string.Equals(t.Name, "Wholesale", StringComparison.OrdinalIgnoreCase))?.Id;
        _contractorTierId = _allTiers.FirstOrDefault(t => string.Equals(t.Name, "Contractor", StringComparison.OrdinalIgnoreCase))?.Id;

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

        // Load base prices
        await LoadPriceIntoField(existing.Id, existing.BaseUnitId, _retailTierId, v => RetailPriceText = v);
        await LoadPriceIntoField(existing.Id, existing.BaseUnitId, _wholesaleTierId, v => WholesalePriceText = v);
        await LoadPriceIntoField(existing.Id, existing.BaseUnitId, _contractorTierId, v => ContractorPriceText = v);

        // Load existing alternative units and their prices
        IReadOnlyList<ProductUnitInfo> units = await _products.ListProductUnitsAsync(existing.Id);
        IReadOnlyList<ProductPriceInfo> allPrices = await _products.ListAllPricesAsync(existing.Id);

        foreach (ProductUnitInfo u in units.Where(u => !u.IsBase))
        {
            ProductUnitEditorViewModel editor = new(AlternativeUnitsForPicker)
            {
                SelectedUnit = _allUnits.FirstOrDefault(x => x.Id == u.UnitId),
                FactorText = (u.FactorMilli / 1000m).ToString("0.###", CultureInfo.InvariantCulture),
            };

            List<ProductPriceInfo> unitPrices = allPrices.Where(p => p.UnitId == u.UnitId).ToList();
            ProductPriceInfo? retail = unitPrices.FirstOrDefault(p => _retailTierId is not null && p.TierId == _retailTierId.Value);
            ProductPriceInfo? wholesale = unitPrices.FirstOrDefault(p => _wholesaleTierId is not null && p.TierId == _wholesaleTierId.Value);
            ProductPriceInfo? contractor = unitPrices.FirstOrDefault(p => _contractorTierId is not null && p.TierId == _contractorTierId.Value);

            if (retail is not null) editor.RetailPriceText = CentsToShillingsString(retail.PriceCents);
            if (wholesale is not null) editor.WholesalePriceText = CentsToShillingsString(wholesale.PriceCents);
            if (contractor is not null) editor.ContractorPriceText = CentsToShillingsString(contractor.PriceCents);

            SellingUnits.Add(editor);
        }
    }

    private async Task LoadPriceIntoField(int productId, int unitId, int? tierId, Action<string> assign)
    {
        if (tierId is null) return;
        long? p = await _products.GetPriceForUnitAsync(productId, tierId.Value, unitId);
        assign(p is null ? "" : CentsToShillingsString(p.Value));
    }

    [RelayCommand]
    private void AddSellingUnit()
    {
        ErrorMessage = "";

        if (SelectedUnit is null)
        {
            ErrorMessage = "Pick a base unit first.";
            return;
        }

        List<Unit> available = _allUnits.Where(u => u.Id != SelectedUnit.Id).ToList();
        if (available.Count == 0)
        {
            ErrorMessage = "No alternative units are configured in the system.";
            return;
        }

        HashSet<int> alreadyUsed = SellingUnits
            .Where(e => e.SelectedUnit is not null)
            .Select(e => e.SelectedUnit!.Id)
            .ToHashSet();

        List<Unit> remaining = available.Where(u => !alreadyUsed.Contains(u.Id)).ToList();
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

        // Parse base prices
        var basePrices = new Dictionary<int, long>();
        if (_retailTierId is not null && !string.IsNullOrWhiteSpace(RetailPriceText))
        {
            if (!TryParseShillingsToCents(RetailPriceText, out long cents, out string? err)) { ErrorMessage = err ?? "Retail price invalid."; return; }
            basePrices[_retailTierId.Value] = cents;
        }
        if (_wholesaleTierId is not null && !string.IsNullOrWhiteSpace(WholesalePriceText))
        {
            if (!TryParseShillingsToCents(WholesalePriceText, out long cents, out string? err)) { ErrorMessage = err ?? "Wholesale price invalid."; return; }
            basePrices[_wholesaleTierId.Value] = cents;
        }
        if (_contractorTierId is not null && !string.IsNullOrWhiteSpace(ContractorPriceText))
        {
            if (!TryParseShillingsToCents(ContractorPriceText, out long cents, out string? err)) { ErrorMessage = err ?? "Contractor price invalid."; return; }
            basePrices[_contractorTierId.Value] = cents;
        }

        // Parse alternative units
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
                ErrorMessage = "A selling unit cannot be the same as the base unit.";
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
                ErrorMessage = $"Factor for '{editor.SelectedUnit.Name}' must be a positive number (e.g. 25 for '1 kg = 25 pieces').";
                return;
            }

            long factorMilli = (long)decimal.Round(factorValue * 1000m, 0, MidpointRounding.AwayFromZero);

            var prices = new List<ProductUnitPriceSave>();
            if (_retailTierId is not null && !string.IsNullOrWhiteSpace(editor.RetailPriceText))
            {
                if (!TryParseShillingsToCents(editor.RetailPriceText, out long c, out string? e)) { ErrorMessage = $"Retail price for '{editor.SelectedUnit.Name}': {e}"; return; }
                prices.Add(new ProductUnitPriceSave(_retailTierId.Value, c));
            }
            if (_wholesaleTierId is not null && !string.IsNullOrWhiteSpace(editor.WholesalePriceText))
            {
                if (!TryParseShillingsToCents(editor.WholesalePriceText, out long c, out string? e)) { ErrorMessage = $"Wholesale price for '{editor.SelectedUnit.Name}': {e}"; return; }
                prices.Add(new ProductUnitPriceSave(_wholesaleTierId.Value, c));
            }
            if (_contractorTierId is not null && !string.IsNullOrWhiteSpace(editor.ContractorPriceText))
            {
                if (!TryParseShillingsToCents(editor.ContractorPriceText, out long c, out string? e)) { ErrorMessage = $"Contractor price for '{editor.SelectedUnit.Name}': {e}"; return; }
                prices.Add(new ProductUnitPriceSave(_contractorTierId.Value, c));
            }

            alternativeUnits.Add(new ProductUnitSave(editor.SelectedUnit.Id, factorMilli, prices));
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

            int id = await _products.SaveProductWithAllPricesAsync(
                product, trimmedBarcode, basePrices, alternativeUnits);
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
