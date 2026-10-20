using CommunityToolkit.Mvvm.ComponentModel;
using Pos.Application.Catalog;
using Pos.Domain;

namespace Pos.Desktop.ViewModels;

public sealed record CartUnitOption(
    int UnitId,
    string UnitName,
    string UnitSymbol,
    long FactorMilli,
    long PriceCents,
    bool AllowsDecimal);

public partial class CartLineViewModel : ObservableObject
{
    public CartLineViewModel(
        ProductListItem product,
        IReadOnlyList<CartUnitOption> unitOptions,
        CartUnitOption initialUnit)
    {
        ProductId = product.Id;
        ProductName = product.Name;
        BaseUnitId = product.BaseUnitId;
        this.unitOptions = unitOptions;
        selectedUnit = initialUnit;

        Recalculate();
    }

    public int ProductId { get; }
    public string ProductName { get; }
    public int BaseUnitId { get; }

    [ObservableProperty]
    private IReadOnlyList<CartUnitOption> unitOptions;

    [ObservableProperty]
    private CartUnitOption? selectedUnit;

    [ObservableProperty] private string qtyText = "1";
    [ObservableProperty] private string discountText = "";
    [ObservableProperty] private string lineTotalDisplay = "0.00";
    [ObservableProperty] private string validationError = "";

    public event EventHandler? Changed;

    public int UnitId => SelectedUnit?.UnitId ?? 0;
    public string UnitName => SelectedUnit?.UnitName ?? "";
    public string UnitSymbol => SelectedUnit?.UnitSymbol ?? "";
    public long UnitPriceCents => SelectedUnit?.PriceCents ?? 0;
    public string UnitPriceDisplay => (UnitPriceCents / 100m).ToString("0.00");

    public long QtyMilli { get; private set; }
    public long BaseQtyMilli { get; private set; }
    public long DiscountCents { get; private set; }
    public long LineTotalCents { get; private set; }

    public bool IsValid => QtyMilli > 0 && string.IsNullOrEmpty(ValidationError);

    partial void OnSelectedUnitChanged(CartUnitOption? value) => Recalculate();
    partial void OnQtyTextChanged(string value) => Recalculate();
    partial void OnDiscountTextChanged(string value) => Recalculate();

    public void SetQuantityMilli(long milli)
    {
        QtyText = (milli / (decimal)Quantity.MilliPerUnit).ToString("0.###",
            System.Globalization.CultureInfo.InvariantCulture);
    }

    public void UpdateUnitOptions(IReadOnlyList<CartUnitOption> newOptions)
    {
        if (newOptions.Count == 0) return;

        int currentUnitId = UnitId;
        UnitOptions = newOptions;

        CartUnitOption? matching = newOptions.FirstOrDefault(u => u.UnitId == currentUnitId)
                                   ?? newOptions.FirstOrDefault(u => u.UnitId == BaseUnitId)
                                   ?? newOptions[0];

        SelectedUnit = matching;
    }

    private void Recalculate()
    {
        ValidationError = "";

        if (SelectedUnit is null)
        {
            ValidationError = "No unit selected.";
            QtyMilli = 0;
            BaseQtyMilli = 0;
            DiscountCents = 0;
            LineTotalCents = 0;
            LineTotalDisplay = "0.00";
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!Quantity.TryParse(QtyText, out Quantity qty, out string? qtyErr))
        {
            ValidationError = qtyErr ?? "Invalid quantity.";
            QtyMilli = 0;
            BaseQtyMilli = 0;
            DiscountCents = 0;
            LineTotalCents = 0;
            LineTotalDisplay = "0.00";
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!SelectedUnit.AllowsDecimal && !qty.IsWhole)
        {
            ValidationError = $"'{ProductName}' is sold by {UnitName} — enter a whole number.";
            QtyMilli = 0;
            BaseQtyMilli = 0;
            DiscountCents = 0;
            LineTotalCents = 0;
            LineTotalDisplay = "0.00";
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        QtyMilli = qty.Milli;
        BaseQtyMilli = checked(qty.Milli * SelectedUnit.FactorMilli / Quantity.MilliPerUnit);

        long discountCents = 0;
        string d = (DiscountText ?? "").Trim();
        if (d.Length > 0)
        {
            if (!decimal.TryParse(d, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out decimal dShillings) || dShillings < 0)
            {
                ValidationError = "Discount must be a non-negative amount.";
                DiscountCents = 0;
                LineTotalCents = 0;
                LineTotalDisplay = "0.00";
                Changed?.Invoke(this, EventArgs.Empty);
                return;
            }
            discountCents = (long)decimal.Round(dShillings * 100m, 0, MidpointRounding.AwayFromZero);
        }
        DiscountCents = discountCents;

        long gross = Money.RoundHalfUpDiv(checked(SelectedUnit.PriceCents * QtyMilli), Quantity.MilliPerUnit);
        long net = gross - discountCents;

        if (net < 0)
        {
            ValidationError = "Discount exceeds price.";
            LineTotalCents = 0;
            LineTotalDisplay = "0.00";
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        LineTotalCents = net;
        LineTotalDisplay = new Money(net).ToString();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
