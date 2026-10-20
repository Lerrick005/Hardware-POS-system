using CommunityToolkit.Mvvm.ComponentModel;
using Pos.Application.Catalog;

namespace Pos.Desktop.ViewModels;

public partial class CartLineViewModel : ObservableObject
{
    private long _unitPriceCents;

    public CartLineViewModel(ProductListItem product, int unitId, string unitName, long unitPriceCents)
    {
        ProductId = product.Id;
        ProductName = product.Name;
        UnitId = unitId;
        UnitName = unitName;
        _unitPriceCents = unitPriceCents;
        UnitPriceText = (unitPriceCents / 100m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }

    public int ProductId { get; }
    public string ProductName { get; }
    public int UnitId { get; }
    public string UnitName { get; }

    [ObservableProperty]
    private string qtyText = "1";

    [ObservableProperty]
    private string unitPriceText = "";

    [ObservableProperty]
    private string discountText = "";

    [ObservableProperty]
    private string lineTotalDisplay = "0.00";

    [ObservableProperty]
    private string validationError = "";

    public event EventHandler? Changed;

    public long QtyMilli { get; private set; }
    public long UnitPriceCents => _unitPriceCents;
    public long DiscountCents { get; private set; }
    public long LineTotalCents { get; private set; }

    public bool IsValid => QtyMilli > 0 && string.IsNullOrEmpty(ValidationError);

    partial void OnQtyTextChanged(string value) => Recalculate();
    partial void OnDiscountTextChanged(string value) => Recalculate();

    public void SetUnitPrice(long cents)
    {
        _unitPriceCents = cents;
        UnitPriceText = (cents / 100m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        Recalculate();
    }

    private void Recalculate()
    {
        ValidationError = "";

        if (!long.TryParse(QtyText?.Trim(), out long qtyWhole) || qtyWhole <= 0)
        {
            ValidationError = "Quantity must be positive.";
            QtyMilli = 0;
            DiscountCents = 0;
            LineTotalCents = 0;
            LineTotalDisplay = "0.00";
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }
        QtyMilli = qtyWhole * Pos.Domain.Quantity.MilliPerUnit;

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

        long gross = Pos.Domain.Money.RoundHalfUpDiv(
            checked(_unitPriceCents * QtyMilli), Pos.Domain.Quantity.MilliPerUnit);
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
        LineTotalDisplay = new Pos.Domain.Money(net).ToString();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
