using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Catalog;
using Pos.Application.Stock;
using Pos.Application.Users;
using Pos.Domain;
using Pos.Domain.Catalog;

namespace Pos.Desktop.ViewModels;

public partial class AdjustStockViewModel : ObservableObject
{
    private readonly IProductRepository _products;
    private readonly IStockAdjustmentService _service;
    private readonly IUserSession _session;
    private readonly ILogger<AdjustStockViewModel> _logger;

    [ObservableProperty] private int productId;
    [ObservableProperty] private string productName = "";
    [ObservableProperty] private string baseUnitName = "";
    [ObservableProperty] private long currentStockMilli;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QuantityLabel))]
    [NotifyPropertyChangedFor(nameof(QuantityHelp))]
    private string reasonCategory = AdjustmentReasons.Damaged;

    [ObservableProperty] private string quantityText = "";
    [ObservableProperty] private string notes = "";
    [ObservableProperty] private string errorMessage = "";
    [ObservableProperty] private bool isSaving;

    public event EventHandler<StockAdjustmentResult>? Saved;

    public string CurrentStockDisplay =>
        $"{(CurrentStockMilli / 1000m).ToString("0.###")} {BaseUnitName}".Trim();

    public string QuantityLabel => ReasonCategory switch
    {
        AdjustmentReasons.Damaged => "How much was damaged?",
        AdjustmentReasons.Lost => "How much is missing?",
        AdjustmentReasons.Found => "How much was found?",
        AdjustmentReasons.Counted => "What did you count?",
        _ => "Quantity",
    };

    public string QuantityHelp => ReasonCategory switch
    {
        AdjustmentReasons.Damaged => "Stock will decrease by this amount.",
        AdjustmentReasons.Lost => "Stock will decrease by this amount.",
        AdjustmentReasons.Found => "Stock will increase by this amount.",
        AdjustmentReasons.Counted => "The total you counted on the shelf. Stock will be corrected to match.",
        _ => "",
    };

    public long DeltaMilli { get; private set; }
    public long NewStockMilli => CurrentStockMilli + DeltaMilli;
    public bool CanSave => DeltaMilli != 0 && !string.IsNullOrWhiteSpace(Notes);

    public string DeltaDisplay => DeltaMilli == 0
        ? "0"
        : (DeltaMilli > 0 ? "+" : "") + (DeltaMilli / 1000m).ToString("0.###");

    public string NewStockDisplay =>
        $"{(NewStockMilli / 1000m).ToString("0.###")} {BaseUnitName}".Trim();

    public AdjustStockViewModel(
        IProductRepository products,
        IStockAdjustmentService service,
        IUserSession session,
        ILogger<AdjustStockViewModel> logger)
    {
        _products = products;
        _service = service;
        _session = session;
        _logger = logger;
    }

    public async Task LoadAsync()
    {
        Pos.Domain.Catalog.Product? product = await _products.GetByIdAsync(ProductId);
        if (product is null)
        {
            ErrorMessage = "Product not found.";
            return;
        }

        // Need to look up the unit name — GetByIdAsync doesn't include it, so query
        // the ProductListItem which does.
        var items = await _products.ListAsync(includeInactive: true);
        ProductListItem? item = items.FirstOrDefault(p => p.Id == ProductId);

        ProductName = product.Name;
        CurrentStockMilli = product.StockMilli;
        BaseUnitName = item?.BaseUnitName ?? "unit";

        Recalculate();
    }

    partial void OnQuantityTextChanged(string value) => Recalculate();
    partial void OnReasonCategoryChanged(string value) => Recalculate();
    partial void OnNotesChanged(string value) => OnPropertyChanged(nameof(CanSave));

    [RelayCommand] private void SetReasonDamaged() => ReasonCategory = AdjustmentReasons.Damaged;
    [RelayCommand] private void SetReasonLost() => ReasonCategory = AdjustmentReasons.Lost;
    [RelayCommand] private void SetReasonFound() => ReasonCategory = AdjustmentReasons.Found;
    [RelayCommand] private void SetReasonCounted() => ReasonCategory = AdjustmentReasons.Counted;

    private void Recalculate()
    {
        ErrorMessage = "";

        if (!Quantity.TryParse(QuantityText, out Quantity qty, out _))
        {
            DeltaMilli = 0;
            NotifyPreviewChanged();
            return;
        }

        DeltaMilli = ReasonCategory switch
        {
            AdjustmentReasons.Damaged => -qty.Milli,
            AdjustmentReasons.Lost => -qty.Milli,
            AdjustmentReasons.Found => qty.Milli,
            AdjustmentReasons.Counted => qty.Milli - CurrentStockMilli,
            _ => 0,
        };

        if (NewStockMilli < 0)
        {
            ErrorMessage = $"Cannot adjust — that would leave stock at {NewStockDisplay}.";
        }

        NotifyPreviewChanged();
    }

    private void NotifyPreviewChanged()
    {
        OnPropertyChanged(nameof(DeltaMilli));
        OnPropertyChanged(nameof(NewStockMilli));
        OnPropertyChanged(nameof(DeltaDisplay));
        OnPropertyChanged(nameof(NewStockDisplay));
        OnPropertyChanged(nameof(CanSave));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = "";

        if (!Quantity.TryParse(QuantityText, out _, out string? qtyErr))
        {
            ErrorMessage = qtyErr ?? "Enter a valid quantity.";
            return;
        }

        if (DeltaMilli == 0)
        {
            ErrorMessage = "This adjustment would not change stock. Nothing to save.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Notes))
        {
            ErrorMessage = "Please add a short note explaining why. This helps you remember later.";
            return;
        }

        try
        {
            IsSaving = true;

            var request = new StockAdjustmentRequest(
                ProductId: ProductId,
                ReasonCategory: ReasonCategory,
                DeltaMilli: DeltaMilli,
                Notes: Notes.Trim(),
                UserId: _session.RequireUserId,
                BusinessDate: DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            StockAdjustmentResult result = await _service.AdjustAsync(request);
            Saved?.Invoke(this, result);
        }
        catch (StockAdjustmentException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stock adjustment failed for product {ProductId}", ProductId);
            ErrorMessage = $"Could not adjust: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }
}

