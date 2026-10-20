using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Catalog;
using Pos.Application.Customers;
using Pos.Application.Sales;
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
    private readonly ILogger<SellViewModel> _logger;

    private CancellationTokenSource? _searchCts;
    private bool _loaded;

    [ObservableProperty]
    private string searchTerm = "";

    [ObservableProperty]
    private ProductListItem? pickerProduct;

    [ObservableProperty]
    private string quantityText = "1";

    [ObservableProperty]
    private string errorMessage = "";

    [ObservableProperty]
    private string statusText = "";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private long subtotalCents;

    [ObservableProperty]
    private long taxCents;

    [ObservableProperty]
    private long totalCents;

    [ObservableProperty]
    private long paidCents;

    [ObservableProperty]
    private long changeCents;

    [ObservableProperty]
    private string totalDisplay = "0.00";

    [ObservableProperty]
    private string paidDisplay = "0.00";

    [ObservableProperty]
    private string changeDisplay = "0.00";

    [ObservableProperty]
    private CustomerListItem? selectedCustomer;

    [ObservableProperty]
    private int? selectedPriceTierId;

    public ObservableCollection<ProductListItem> SearchResults { get; } = new();
    public ObservableCollection<CartLineViewModel> Lines { get; } = new();
    public ObservableCollection<PaymentLineViewModel> Payments { get; } = new();
    public ObservableCollection<CustomerListItem> Customers { get; } = new();

    public IReadOnlyList<PriceTierOption> PriceTiers { get; private set; } = Array.Empty<PriceTierOption>();

    public bool HasMultiplePayments => Payments.Count > 1;

    public Func<SaleResult, ReceiptView>? CreateReceiptDialog { get; set; }

    public SellViewModel(
        IProductRepository products,
        IUnitRepository units,
        IPriceTierRepository tiers,
        ICustomerRepository customers,
        ISellService sellService,
        ILogger<SellViewModel> logger)
    {
        _products = products;
        _units = units;
        _tiers = tiers;
        _customers = customers;
        _sellService = sellService;
        _logger = logger;

        Payments.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMultiplePayments));
    }

    public async Task LoadAsync()
    {
        if (_loaded) return;
        _loaded = true;

        IReadOnlyList<PriceTier> tierList = await _tiers.ListAsync(includeInactive: false);
        PriceTiers = tierList.Select(t => new PriceTierOption(t.Id, t.Name)).ToList();

        PriceTier defaultTier = tierList.FirstOrDefault(t => t.IsDefault) ?? tierList[0];
        SelectedPriceTierId = defaultTier.Id;

        IReadOnlyList<CustomerListItem> customerList = await _customers.ListAsync(includeInactive: false);
        Customers.Clear();
        foreach (CustomerListItem c in customerList)
        {
            Customers.Add(c);
        }

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

    partial void OnSelectedCustomerChanged(CustomerListItem? value)
    {
        if (value?.PriceTierId is not null)
        {
            SelectedPriceTierId = value.PriceTierId;
        }
    }

    partial void OnSelectedPriceTierIdChanged(int? value)
    {
        if (value is null) return;
        _ = RepriceCartAsync(value.Value);
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
            foreach (ProductListItem p in items)
            {
                SearchResults.Add(p);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Product search failed for {Term}", term);
        }
    }

    private async Task RepriceCartAsync(int tierId)
    {
        foreach (CartLineViewModel line in Lines)
        {
            long? price = await _products.GetPriceAsync(line.ProductId, tierId);
            if (price is not null)
            {
                line.SetUnitPrice(price.Value);
            }
        }
        Recalculate();
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

        if (!long.TryParse(QuantityText, out long qty) || qty <= 0)
        {
            ErrorMessage = "Quantity must be a positive whole number.";
            return;
        }

        int tierId = SelectedPriceTierId ?? 1;
        long? unitPrice = await _products.GetPriceAsync(PickerProduct.Id, tierId);
        if (unitPrice is null)
        {
            unitPrice = await _products.GetPriceAsync(PickerProduct.Id, PriceTiers.FirstOrDefault()?.Id ?? tierId);
        }
        if (unitPrice is null)
        {
            ErrorMessage = $"No price set for '{PickerProduct.Name}' at this price type. Edit the product first.";
            return;
        }

        Unit? baseUnit = await _units.GetByIdAsync(PickerProduct.BaseUnitId);

        CartLineViewModel? existing = Lines.FirstOrDefault(l => l.ProductId == PickerProduct.Id);
        if (existing is not null)
        {
            long newQty = (existing.QtyMilli / Pos.Domain.Quantity.MilliPerUnit) + qty;
            existing.QtyText = newQty.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            CartLineViewModel line = new(PickerProduct, PickerProduct.BaseUnitId, baseUnit?.Name ?? "pc", unitPrice.Value)
            {
                QtyText = qty.ToString(CultureInfo.InvariantCulture),
            };
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
    }

    [RelayCommand]
    private async Task CompleteSaleAsync()
    {
        _logger.LogInformation("CompleteSale clicked. Lines={Lines} Payments={Payments} Paid={Paid} Total={Total}", Lines.Count, Payments.Count, PaidCents, TotalCents);
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

        foreach (PaymentLineViewModel p in Payments)
        {
            if (!p.IsValid) { ErrorMessage = p.ValidationError; return; }
        }

        if (PaidCents < TotalCents)
        {
            ErrorMessage = $"Payment is short by {new Pos.Domain.Money(TotalCents - PaidCents)}.";
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
                CustomerId: SelectedCustomer?.Id,
                PriceTierId: SelectedPriceTierId,
                BusinessDate: DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Lines: lineReqs,
                Payments: payReqs,
                UserId: 1,
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
        SelectedCustomer = null;
        ErrorMessage = "";
        Recalculate();
    }

    public sealed record PriceTierOption(int Id, string Name);
}


