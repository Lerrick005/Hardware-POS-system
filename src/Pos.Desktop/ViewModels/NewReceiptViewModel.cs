using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Catalog;
using Pos.Application.Stock;
using Pos.Domain;

namespace Pos.Desktop.ViewModels;

public partial class NewReceiptViewModel : ObservableObject
{
    private readonly IReceiveGoods _receiveGoods;
    private readonly IProductRepository _products;
    private readonly ILogger<NewReceiptViewModel> _logger;

    [ObservableProperty]
    private string businessDate = DateTime.Today.ToString("yyyy-MM-dd");

    [ObservableProperty]
    private string supplierRef = "";

    [ObservableProperty]
    private string errorMessage = "";

    [ObservableProperty]
    private string totalDisplay = "0.00";

    [ObservableProperty]
    private bool isSaving;

    [ObservableProperty]
    private ProductListItem? pickerProduct;

    [ObservableProperty]
    private string pickerSearchTerm = "";

    public ObservableCollection<ReceiptLineViewModel> Lines { get; } = new();
    public ObservableCollection<ProductListItem> PickerResults { get; } = new();

    public event EventHandler<int>? Saved;

    private CancellationTokenSource? _pickerCts;

    public NewReceiptViewModel(
        IReceiveGoods receiveGoods,
        IProductRepository products,
        ILogger<NewReceiptViewModel> logger)
    {
        _receiveGoods = receiveGoods;
        _products = products;
        _logger = logger;
    }

    public async Task LoadAsync()
    {
        await RunPickerSearchAsync("");
    }

    partial void OnPickerSearchTermChanged(string value)
    {
        _pickerCts?.Cancel();
        _pickerCts = new CancellationTokenSource();
        CancellationToken token = _pickerCts.Token;
        _ = DebouncePickerAsync(value, token);
    }

    private async Task DebouncePickerAsync(string term, CancellationToken ct)
    {
        try
        {
            await Task.Delay(200, ct);
            await RunPickerSearchAsync(term, ct);
        }
        catch (OperationCanceledException)
        {
            // superseded
        }
    }

    private async Task RunPickerSearchAsync(string term, CancellationToken ct = default)
    {
        try
        {
            IReadOnlyList<ProductListItem> items = string.IsNullOrWhiteSpace(term)
                ? await _products.ListAsync(includeInactive: false, ct)
                : await _products.SearchAsync(term, limit: 50, ct);

            if (ct.IsCancellationRequested)
            {
                return;
            }

            PickerResults.Clear();
            foreach (ProductListItem p in items)
            {
                PickerResults.Add(p);
            }
        }
        catch (OperationCanceledException)
        {
            // superseded
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Picker search failed for {Term}", term);
        }
    }

    [RelayCommand]
    private void AddLine()
    {
        if (PickerProduct is null)
        {
            ErrorMessage = "Pick a product first.";
            return;
        }

        ReceiptLineViewModel line = new() { Product = PickerProduct };
        line.Changed += OnLineChanged;
        Lines.Add(line);

        PickerProduct = null;
        PickerSearchTerm = "";
        ErrorMessage = "";
        RecalculateTotal();
    }

    [RelayCommand]
    private void RemoveLine(ReceiptLineViewModel? line)
    {
        if (line is null)
        {
            return;
        }

        line.Changed -= OnLineChanged;
        Lines.Remove(line);
        RecalculateTotal();
    }

    private void OnLineChanged(object? sender, EventArgs e) => RecalculateTotal();

    private void RecalculateTotal()
    {
        long totalCents = 0;
        foreach (ReceiptLineViewModel line in Lines)
        {
            totalCents = checked(totalCents + line.LineTotalCents);
        }

        TotalDisplay = new Money(totalCents).ToString();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = "";

        if (!DateTime.TryParseExact(BusinessDate, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _))
        {
            ErrorMessage = "Business date must be YYYY-MM-DD.";
            return;
        }

        if (Lines.Count == 0)
        {
            ErrorMessage = "Add at least one line.";
            return;
        }

        HashSet<int> seenProducts = new();
        foreach (ReceiptLineViewModel line in Lines)
        {
            if (!line.IsValid)
            {
                ErrorMessage = string.IsNullOrEmpty(line.ValidationError)
                    ? "Every line needs a product, quantity and unit cost."
                    : line.ValidationError;
                return;
            }

            if (line.Product is not null && !seenProducts.Add(line.Product.Id))
            {
                ErrorMessage = $"Duplicate product on receipt: {line.Product.Name}. Merge into one line.";
                return;
            }
        }

        try
        {
            IsSaving = true;

            ReceiveGoodsLine[] requestLines = Lines
                .Select(l => new ReceiveGoodsLine(l.Product!.Id, l.QtyMilli, l.UnitCostCents))
                .ToArray();

            ReceiveGoodsRequest request = new(
                SupplierId: null,
                SupplierRef: string.IsNullOrWhiteSpace(SupplierRef) ? null : SupplierRef.Trim(),
                BusinessDate: BusinessDate,
                Lines: requestLines,
                UserId: null);

            ReceiveGoodsResult result = await _receiveGoods.ExecuteAsync(request);
            Saved?.Invoke(this, result.ReceiptId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Save new receipt failed");
            ErrorMessage = $"Could not save: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }
}
