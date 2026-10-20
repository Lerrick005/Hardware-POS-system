using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Catalog;
using Pos.Application.Customers;
using Pos.Application.Sales;
using Pos.Application.Users;
using Pos.Desktop.Views;
using Pos.Domain.Catalog;

namespace Pos.Desktop.ViewModels;

public partial class SellViewModel : ObservableObject
{
    private readonly IProductRepository _products;
    private readonly IUnitRepository _units;
    private readonly IPriceTierRepository _tiers;
    private readonly ICustomerRepository _customers;
    private readonly ISellService _sellService;
    private readonly IUserSession _session;
    private readonly ILogger<SellViewModel> _logger;

    private CancellationTokenSource? _searchCts;
    private bool _loaded;
    private int _defaultTierId = 1;

    [ObservableProperty] private string searchTerm = "";
    [ObservableProperty] private ProductListItem? pickerProduct;
    [ObservableProperty] private string quantityText = "1";
    [ObservableProperty] private string errorMessage = "";
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private bool isBusy;

    [ObservableProperty] private long subtotalCents;
    [ObservableProperty] private long taxCents;
    [ObservableProperty] private long totalCents;
    [ObservableProperty] private long paidCents;
    [ObservableProperty] private long changeCents;

    [ObservableProperty] private string totalDisplay = "0.00";
    [ObservableProperty] private string paidDisplay = "0.00";
    [ObservableProperty] private string changeDisplay = "0.00";

    [ObservableProperty] private CustomerListItem? selectedCustomer;

    public ObservableCollection<ProductListItem> SearchResults { get; } = new();
    public ObservableCollection<CartLineViewModel> Lines { get; } = new();
    public ObservableCollection<PaymentLineViewModel> Payments { get; } = new();
    public ObservableCollection<CustomerListItem> Customers { get; } = new();

    public bool HasMultiplePayments => Payments.Count > 1;
    public bool HasStockWarning => Lines.Any(l => l.IsStockShort);

    public Func<SaleResult, ReceiptView>? CreateReceiptDialog { get; set; }

    public SellViewModel(
        IProductRepository products,
        IUnitRepository units,
        IPriceTierRepository tiers,
        ICustomerRepository customers,
        ISellService sellService,
        IUserSession session,
        ILogger<SellViewModel> logger)
    {
        _products = products;
        _units = units;
        _tiers = tiers;
        _customers = customers;
        _sellService = sellService;
        _session = session;
        _logger = logger;

        Payments.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMultiplePayments));
    }

    public async Task LoadAsync()
    {
        if (_loaded) return;
        _loaded = true;

        PriceTier? defaultTier = await _tiers.GetDefaultAsync();
        _defaultTierId = defaultTier?.Id ?? 1;

        IReadOnlyList<CustomerListItem> customerList = await _customers.ListAsync(includeInactive: false);

        Customers.Clear();
        Customers.Add(new CustomerListItem { Id = 0, Name = "< Walk-in Customer >", BalanceCents = 0 });
        foreach (CustomerListItem c in customerList)
        {
            Customers.Add(c);
        }

        SelectedCustomer = Customers[0];

        ResetPaymentsForCash();
        Recalculate();
        await RunSearchAsync("");
    }

    partial void OnSearchTermChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        CancellationToken token = _searchCts.Token;
        _ = DebounceSearchAsync(value, token);
    }

    private async Task DebounceSearchAsync(string term, CancellationToken ct)
    {
        try
        {
            await Task.Delay(200, ct);
            await RunSearchAsync(term, ct);
        }
        catch (OperationCanceledException) { }
    }

    private async Task RunSearchAsync(string term, CancellationToken ct = default)
    {
        try
        {
            IReadOnlyList<ProductListItem> items = string.IsNullOrWhiteSpace(term)
                ? await _products.ListAsync(includeInactive: false, ct)
                : await _products.SearchAsync(term, limit: 30, ct);

            if (ct.IsCancellationRequested) return;

            SearchResults.Clear();
            foreach (ProductListItem p in items) SearchResults.Add(p);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Product search failed for {Term}", term);
        }
    }

    [RelayCommand]
    private async Task AddToCartAsync()
    {
        ErrorMessage = "";

        if (PickerProduct is null)
        {
            ErrorMessage = "Pick a product first.";
            return;
        }

        if (!Pos.Domain.Quantity.TryParse(QuantityText, out Pos.Domain.Quantity qty, out string? qtyErr))
        {
            ErrorMessage = qtyErr ?? "Invalid quantity.";
            return;
        }
        if (qty.Milli <= 0)
        {
            ErrorMessage = "Quantity must be greater than zero.";
            return;
        }

        // Build the available units for this product at the default (Retail) tier.
        IReadOnlyList<ProductUnitInfo> unitInfos = await _products.ListProductUnitsAsync(PickerProduct.Id);
        IReadOnlyList<ProductPriceInfo> allPrices = await _products.ListAllPricesAsync(PickerProduct.Id);

        var unitOptions = new List<CartUnitOption>();
        foreach (ProductUnitInfo ui in unitInfos)
        {
            long? price = allPrices
                .FirstOrDefault(p => p.TierId == _defaultTierId && p.UnitId == ui.UnitId)?.PriceCents;

            if (price is null) continue;

            Unit? unitDetail = await _units.GetByIdAsync(ui.UnitId);
            bool allowsDecimal = unitDetail?.AllowsDecimal ?? false;

            unitOptions.Add(new CartUnitOption(
                UnitId: ui.UnitId,
                UnitName: ui.UnitName,
                UnitSymbol: ui.UnitSymbol,
                FactorMilli: ui.FactorMilli,
                PriceCents: price.Value,
                AllowsDecimal: allowsDecimal));
        }

        if (unitOptions.Count == 0)
        {
            ErrorMessage = $"'{PickerProduct.Name}' has no price set. Edit the product first.";
            return;
        }

        CartUnitOption defaultUnit = unitOptions.FirstOrDefault(u => u.UnitId == PickerProduct.BaseUnitId)
                                     ?? unitOptions[0];

        CartLineViewModel? existing = Lines.FirstOrDefault(l =>
            l.ProductId == PickerProduct.Id && l.UnitId == defaultUnit.UnitId);

        if (existing is not null)
        {
            existing.SetQuantityMilli(existing.QtyMilli + qty.Milli);
        }
        else
        {
            CartLineViewModel line = new(
                PickerProduct,
                unitOptions,
                defaultUnit,
                PickerProduct.BaseUnitName,
                PickerProduct.StockMilli);
            line.SetQuantityMilli(qty.Milli);
            line.Changed += OnLineChanged;
            Lines.Add(line);
        }

        PickerProduct = null;
        QuantityText = "1";
        SearchTerm = "";
        await RunSearchAsync("");
        Recalculate();
    }

    [RelayCommand]
    private void RemoveLine(CartLineViewModel? line)
    {
        if (line is null) return;
        line.Changed -= OnLineChanged;
        Lines.Remove(line);
        Recalculate();
    }

    [RelayCommand]
    private void AddPayment()
    {
        PaymentLineViewModel p = new();
        p.Changed += OnPaymentChanged;
        Payments.Add(p);
        Recalculate();
    }

    [RelayCommand]
    private void RemovePayment(PaymentLineViewModel? payment)
    {
        if (payment is null) return;
        payment.Changed -= OnPaymentChanged;
        Payments.Remove(payment);
        Recalculate();
    }

    [RelayCommand]
    private void SetExactCash()
    {
        if (Payments.Count == 0) return;
        Payments[0].AmountText = (TotalCents / 100m).ToString("0.##", CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    private void AddCashTender(string amountKes)
    {
        if (!decimal.TryParse(amountKes, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal kes) || kes <= 0)
            return;

        long cents = (long)(kes * 100m);

        // Find the first CASH payment, or create one.
        PaymentLineViewModel? cashLine = Payments.FirstOrDefault(p => p.MethodCode == "CASH");
        if (cashLine is null)
        {
            cashLine = new PaymentLineViewModel { MethodCode = "CASH" };
            cashLine.Changed += OnPaymentChanged;
            Payments.Add(cashLine);
        }

        long newTotal = cashLine.AmountCents + cents;
        cashLine.AmountText = (newTotal / 100m).ToString("0.##", CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    private void ClearCashTender()
    {
        PaymentLineViewModel? cashLine = Payments.FirstOrDefault(p => p.MethodCode == "CASH");
        if (cashLine is null) return;
        cashLine.AmountText = "";
    }

    private void ResetPaymentsForCash()
    {
        foreach (PaymentLineViewModel p in Payments) p.Changed -= OnPaymentChanged;
        Payments.Clear();
        PaymentLineViewModel cash = new() { MethodCode = "CASH", AmountText = "" };
        cash.Changed += OnPaymentChanged;
        Payments.Add(cash);
    }

    private void OnLineChanged(object? sender, EventArgs e) => Recalculate();
    private void OnPaymentChanged(object? sender, EventArgs e) => Recalculate();

    private void Recalculate()
    {
        long total = 0;
        foreach (CartLineViewModel line in Lines)
        {
            total = checked(total + line.LineTotalCents);
        }
        SubtotalCents = total;
        TaxCents = 0;
        TotalCents = total;

        long paid = 0;
        foreach (PaymentLineViewModel p in Payments)
        {
            paid = checked(paid + p.AmountCents);
        }
        PaidCents = paid;
        ChangeCents = Math.Max(0, paid - total);

        TotalDisplay = new Pos.Domain.Money(total).ToString();
        PaidDisplay = new Pos.Domain.Money(paid).ToString();
        ChangeDisplay = new Pos.Domain.Money(ChangeCents).ToString();

        StatusText = Lines.Count == 1 ? "1 line" : $"{Lines.Count} lines";

        OnPropertyChanged(nameof(HasStockWarning));
    }

    [RelayCommand]
    private async Task CompleteSaleAsync()
    {
        ErrorMessage = "";

        if (Lines.Count == 0)
        {
            ErrorMessage = "Cart is empty.";
            return;
        }

        foreach (CartLineViewModel l in Lines)
        {
            if (!l.IsValid) { ErrorMessage = l.ValidationError; return; }
        }

        if (Lines.Any(l => l.IsStockShort))
        {
            ErrorMessage = "Some items don't have enough stock. Reduce the quantity or remove the line.";
            return;
        }

        foreach (PaymentLineViewModel p in Payments)
        {
            if (!p.IsValid) { ErrorMessage = p.ValidationError; return; }
        }

        if (PaidCents < TotalCents)
        {
            ErrorMessage = $"Payment is short by {new Pos.Domain.Money(TotalCents - PaidCents)}.";
            return;
        }

        bool hasCredit = Payments.Any(p => p.MethodCode == "CREDIT" && p.AmountCents > 0);
        int? customerId = SelectedCustomer?.Id == 0 ? null : SelectedCustomer?.Id;

        if (hasCredit && customerId is null)
        {
            ErrorMessage = "Credit sales require a named customer. Select one from the dropdown.";
            return;
        }

        try
        {
            IsBusy = true;

            SaleLineRequest[] lineReqs = Lines.Select(l =>
                new SaleLineRequest(
                    ProductId: l.ProductId,
                    UnitId: l.UnitId,
                    QtyMilli: l.QtyMilli,
                    UnitPriceCentsOverride: l.UnitPriceCents,
                    DiscountCents: l.DiscountCents,
                    Notes: null)).ToArray();

            PaymentRequest[] payReqs = Payments
                .Where(p => p.AmountCents > 0)
                .Select(p => new PaymentRequest(
                    Method: p.MethodCode,
                    AmountCents: p.AmountCents,
                    Reference: string.IsNullOrWhiteSpace(p.Reference) ? null : p.Reference.Trim()))
                .ToArray();

            SaleRequest request = new(
                CustomerId: customerId,
                PriceTierId: _defaultTierId,
                BusinessDate: DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Lines: lineReqs,
                Payments: payReqs,
                UserId: _session.RequireUserId,
                CashSessionId: null);

            SaleResult result = await _sellService.CompleteSaleAsync(request);

            if (CreateReceiptDialog is not null)
            {
                ReceiptView receipt = CreateReceiptDialog(result);
                receipt.ShowDialog();
            }

            ClearCart();
        }
        catch (SaleValidationException ex)
        {
            _logger.LogWarning("Sale rejected: {Message}", ex.Message);
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Complete sale failed");
            ErrorMessage = $"Could not complete sale: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ClearCart()
    {
        foreach (CartLineViewModel l in Lines) l.Changed -= OnLineChanged;
        Lines.Clear();
        ResetPaymentsForCash();
        SelectedCustomer = Customers.FirstOrDefault();
        ErrorMessage = "";
        Recalculate();
    }
}
