using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Pos.Application.Catalog;
using Pos.Domain;

namespace Pos.Desktop.ViewModels;

public partial class ReceiptLineViewModel : ObservableObject
{
    [ObservableProperty] private ProductListItem? product;
    [ObservableProperty] private bool unitAllowsDecimal = true;
    [ObservableProperty] private string qtyText = "";
    [ObservableProperty] private string unitCostText = "";
    [ObservableProperty] private string lineTotalDisplay = "0.00";
    [ObservableProperty] private string validationError = "";

    public event EventHandler? Changed;

    public long QtyMilli { get; private set; }
    public long UnitCostCents { get; private set; }
    public long LineTotalCents { get; private set; }

    public bool IsValid =>
        Product is not null &&
        QtyMilli > 0 &&
        UnitCostCents >= 0 &&
        string.IsNullOrEmpty(ValidationError);

    partial void OnProductChanged(ProductListItem? value) => Recalculate();
    partial void OnUnitAllowsDecimalChanged(bool value) => Recalculate();
    partial void OnQtyTextChanged(string value) => Recalculate();
    partial void OnUnitCostTextChanged(string value) => Recalculate();

    private void Recalculate()
    {
        ValidationError = "";

        if (Product is null || string.IsNullOrWhiteSpace(QtyText))
        {
            QtyMilli = 0;
            UnitCostCents = 0;
            LineTotalCents = 0;
            LineTotalDisplay = "0.00";
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!Quantity.TryParse(QtyText, out Quantity qty, out string? qtyErr))
        {
            ValidationError = qtyErr ?? "Invalid quantity.";
            QtyMilli = 0;
        }
        else if (!UnitAllowsDecimal && !qty.IsWhole)
        {
            ValidationError = "This unit does not allow fractions — enter a whole number.";
            QtyMilli = 0;
        }
        else
        {
            QtyMilli = qty.Milli;
        }

        if (string.IsNullOrWhiteSpace(UnitCostText))
        {
            UnitCostCents = 0;
        }
        else if (!decimal.TryParse(UnitCostText.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal costShillings) ||
                 costShillings < 0)
        {
            ValidationError = string.IsNullOrEmpty(ValidationError)
                ? "Unit cost must be a non-negative amount (e.g. 650 or 2.50)."
                : ValidationError;
            UnitCostCents = 0;
        }
        else
        {
            UnitCostCents = (long)decimal.Round(costShillings * 100m, 0, MidpointRounding.AwayFromZero);
        }

        LineTotalCents = Money.RoundHalfUpDiv(checked(UnitCostCents * QtyMilli), Quantity.MilliPerUnit);
        LineTotalDisplay = new Money(LineTotalCents).ToString();

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
