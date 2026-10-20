using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Catalog;
using Pos.Application.Stock;
using Pos.Application.Suppliers;
using Pos.Application.Users;
using Pos.Domain;

namespace Pos.Desktop.ViewModels;

public partial class NewReceiptViewModel : ObservableObject
{
    private readonly IReceiveGoods _receiveGoods;
    private readonly IProductRepository _products;
    private readonly ISupplierRepository _suppliers;
    private readonly IUnitRepository _units;
    private readonly IUserSession _session;
    private readonly ILogger<NewReceiptViewModel> _logger;

    [ObservableProperty] private string businessDate = DateTime.Today.ToString("yyyy-MM-dd");
    [ObservableProperty] private SupplierListItem? selectedSupplier;
    [ObservableProperty] private string supplierRef = "";
    [ObservableProperty] private string paidText = "";
    [ObservableProperty] private string errorMessage = "";
    [ObservableProperty] private string totalDisplay = "0.00";
    [ObservableProperty] private string remainingDueDisplay = "0.00";
    [ObservableProperty] private bool isSaving;
    [ObservableProperty] private ProductListItem? pickerProduct;
    [ObservableProperty] private string pickerSearchTerm = "";

    public ObservableCollection<ReceiptLineViewModel> Lines { get; } = new();
    public ObservableCollection<ProductListItem> PickerResults { get; } = new();
    public ObservableCollection<SupplierListItem> Suppliers { get; } = new();

    public event EventHandler<int>? Saved;

    private CancellationTokenSource? _pickerCts;

    public NewReceiptViewModel(
        IReceiveGoods receiveGoods,
        IProductRepository products,
        ISupplierRepository suppliers,
        IUnitRepository units,
        IUserSession session,
        ILogger<NewReceiptViewModel> logger)
    {
        _receiveGoods = receiveGoods;
        _products = products;
        _suppliers = suppliers;
        _units = units;
        _session = session;
        _logger = logger;
    }

    public async Task LoadAsync()
    {
        IReadOnlyList<SupplierListItem> supplierList = await _suppliers.ListAsync(includeInactive: false);
        Suppliers.Clear();
        Suppliers.Add(new SupplierListItem { Id = 0, Name = "< No supplier >" });
        foreach (SupplierListItem s in supplierList) Suppliers.Add(s);
        SelectedSupplier = Suppliers[0];

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
        try { await Task.Delay(200, ct); await RunPickerSearchAsync(term, ct); }
        catch (OperationCanceledException) { }
    }

    private async Task RunPickerSearchAsync(string term, CancellationToken ct = default)
    {
        try
        {
            IReadOnlyList<ProductListItem> items = string.IsNullOrWhiteSpace(term)
                ? await _products.ListAsync(includeInactive: false, ct)
                : await _products.SearchAsync(term, limit: 50, ct);

            if (ct.IsCancellationRequested) return;

            PickerResults.Clear();
            foreach (ProductListItem p in items) PickerResults.Add(p);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Picker search failed for {Term}", term);
        }
    }

    [RelayCommand]
    private async Task AddLineAsync()
    {
        if (PickerProduct is null)
        {
            ErrorMessage = "Pick a product first.";
            return;
        }

        Pos.Domain.Catalog.Unit? baseUnit = await _units.GetByIdAsync(PickerProduct.BaseUnitId);

        ReceiptLineViewModel line = new()
        {
            Product = PickerProduct,
            UnitAllowsDecimal = baseUnit?.AllowsDecimal ?? false,
            ProductCurrentStockMilli = PickerProduct.StockMilli,
        };
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
        if (line is null) return;
        line.Changed -= OnLineChanged;
        Lines.Remove(line);
        RecalculateTotal();
    }

    private void OnLineChanged(object? sender, EventArgs e) => RecalculateTotal();

    private void RecalculateTotal()
    {
        long total = 0;
        foreach (ReceiptLineViewModel line in Lines) total = checked(total + line.LineTotalCents);
        TotalDisplay = new Money(total).ToString();

        long paid = 0;
        if (!string.IsNullOrWhiteSpace(PaidText) &&
            decimal.TryParse(PaidText.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal paidKes) &&
            paidKes >= 0)
        {
            paid = (long)decimal.Round(paidKes * 100m, 0, MidpointRounding.AwayFromZero);
        }

        long remaining = Math.Max(0, total - paid);
        RemainingDueDisplay = new Money(remaining).ToString();
    }

    partial void OnPaidTextChanged(string value) => RecalculateTotal();

    [RelayCommand]
    private void PayInFull()
    {
        long total = 0;
        foreach (ReceiptLineViewModel line in Lines) total = checked(total + line.LineTotalCents);
        PaidText = (total / 100m).ToString("0.##", CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    private async Task RepeatLastDeliveryAsync()
    {
        if (SelectedSupplier is null || SelectedSupplier.Id == 0)
        {
            ErrorMessage = "Pick a supplier to repeat their last delivery.";
            return;
        }

        try
        {
            // Last delivery from this supplier: read from goods_receipts
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                {
                    DataSource = Environment.ExpandEnvironmentVariables(@"%ProgramData%\MyPOS\pos.db")
                }.ToString());
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT gri.product_id, gri.qty_milli, gri.unit_cost_cents
                  FROM goods_receipt_items gri
                  JOIN goods_receipts gr ON gr.id = gri.receipt_id
                 WHERE gr.supplier_id = $sid
                   AND gr.id = (SELECT MAX(id) FROM goods_receipts WHERE supplier_id = $sid)";
            cmd.Parameters.AddWithValue("$sid", SelectedSupplier.Id);

            List<(int ProductId, long QtyMilli, long Cost)> items = new();
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    items.Add((reader.GetInt32(0), reader.GetInt64(1), reader.GetInt64(2)));
                }
            }

            if (items.Count == 0)
            {
                ErrorMessage = "This supplier has no previous deliveries.";
                return;
            }

            Lines.Clear();
            foreach (var item in items)
            {
                ProductListItem? product = PickerResults.FirstOrDefault(p => p.Id == item.ProductId)
                                           ?? (await _products.ListAsync(includeInactive: true))
                                                .FirstOrDefault(p => p.Id == item.ProductId);
                if (product is null) continue;

                Pos.Domain.Catalog.Unit? baseUnit = await _units.GetByIdAsync(product.BaseUnitId);
                ReceiptLineViewModel line = new()
                {
                    Product = product,
                    UnitAllowsDecimal = baseUnit?.AllowsDecimal ?? false,
                    ProductCurrentStockMilli = product.StockMilli,
                    QtyText = (item.QtyMilli / 1000m).ToString("0.###", CultureInfo.InvariantCulture),
                    UnitCostText = (item.Cost / 100m).ToString("0.##", CultureInfo.InvariantCulture),
                };
                line.Changed += OnLineChanged;
                Lines.Add(line);
            }

            ErrorMessage = "";
            RecalculateTotal();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Repeat last delivery failed");
            ErrorMessage = $"Could not repeat: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = "";

        if (!DateTime.TryParseExact(BusinessDate, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _))
        {
            ErrorMessage = "Date must be YYYY-MM-DD.";
            return;
        }

        if (Lines.Count == 0)
        {
            ErrorMessage = "Add at least one product.";
            return;
        }

        HashSet<int> seen = new();
        foreach (ReceiptLineViewModel line in Lines)
        {
            if (!line.IsValid)
            {
                ErrorMessage = string.IsNullOrEmpty(line.ValidationError)
                    ? "Every line needs a product, quantity and cost."
                    : line.ValidationError;
                return;
            }
            if (line.Product is not null && !seen.Add(line.Product.Id))
            {
                ErrorMessage = $"Product already on the list: {line.Product.Name}.";
                return;
            }
        }

        long totalCents = 0;
        foreach (ReceiptLineViewModel line in Lines) totalCents = checked(totalCents + line.LineTotalCents);

        long paidCents = totalCents;
        if (!string.IsNullOrWhiteSpace(PaidText))
        {
            if (!decimal.TryParse(PaidText.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal paidKes) || paidKes < 0)
            {
                ErrorMessage = "Paid amount must be a non-negative number.";
                return;
            }
            paidCents = (long)decimal.Round(paidKes * 100m, 0, MidpointRounding.AwayFromZero);
            if (paidCents > totalCents)
            {
                ErrorMessage = "Paid amount cannot exceed the delivery total.";
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
                SupplierId: SelectedSupplier?.Id == 0 ? null : SelectedSupplier?.Id,
                SupplierRef: string.IsNullOrWhiteSpace(SupplierRef) ? null : SupplierRef.Trim(),
                BusinessDate: BusinessDate,
                Lines: requestLines,
                UserId: _session.RequireUserId,
                PaidCents: paidCents);

            ReceiveGoodsResult result = await _receiveGoods.ExecuteAsync(request);
            Saved?.Invoke(this, result.ReceiptId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Save delivery failed");
            ErrorMessage = $"Could not save: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }
}
