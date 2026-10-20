using CommunityToolkit.Mvvm.ComponentModel;
using Pos.Domain.Catalog;

namespace Pos.Desktop.ViewModels;

/// <summary>
/// One editable row in the "Selling units" section of the product form.
/// Holds: which unit, the conversion factor, and prices per tier.
/// </summary>
public partial class ProductUnitEditorViewModel : ObservableObject
{
    public ProductUnitEditorViewModel(IReadOnlyList<Unit> availableUnits)
    {
        AvailableUnits = availableUnits;
    }

    public IReadOnlyList<Unit> AvailableUnits { get; }

    [ObservableProperty]
    private Unit? selectedUnit;

    /// <summary>Factor as text, e.g. "25" means 1 of this unit = 25 base units.</summary>
    [ObservableProperty]
    private string factorText = "";

    [ObservableProperty]
    private string retailPriceText = "";

    [ObservableProperty]
    private string wholesalePriceText = "";

    [ObservableProperty]
    private string contractorPriceText = "";
}
