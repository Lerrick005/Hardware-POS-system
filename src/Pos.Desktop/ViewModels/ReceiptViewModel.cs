using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Pos.Application.Sales;
using Pos.Application.Settings;

namespace Pos.Desktop.ViewModels;

/// <summary>
/// One rendered line on the receipt. Includes the unit and a detail line
/// like "@ KES 400.00/KG" so the customer can verify the math.
/// </summary>
public sealed class SaleReceiptLine
{
    public string ProductName { get; init; } = "";
    public string QtyDisplay { get; init; } = "";
    public string UnitSymbol { get; init; } = "";
    public string TotalDisplay { get; init; } = "";

    /// <summary>
    /// Second line under the item, e.g. "(@ KES 400.00/KG)".
    /// Empty when the unit is the base unit AND the qty is a whole number,
    /// because the math is already obvious.
    /// </summary>
    public string UnitDetailLine { get; init; } = "";
}

public partial class ReceiptViewModel : ObservableObject
{
    public ReceiptViewModel(SaleResult sale, ShopProfile profile)
    {
        ShopName = string.IsNullOrWhiteSpace(profile.ShopName) ? "MY SHOP" : profile.ShopName;

        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(profile.Address)) lines.Add(profile.Address);
        if (!string.IsNullOrWhiteSpace(profile.Phone)) lines.Add("Tel: " + profile.Phone);
        if (!string.IsNullOrWhiteSpace(profile.KraPin)) lines.Add("PIN: " + profile.KraPin);
        ShopDetails = string.Join("\n", lines);

        ReceiptFooter = string.IsNullOrWhiteSpace(profile.ReceiptFooter)
            ? "Thank you for your business!"
            : profile.ReceiptFooter;

        ReceiptNumber = sale.ReceiptNumber;
        SubtotalDisplay = new Pos.Domain.Money(sale.SubtotalCents).ToString();
        TaxDisplay = new Pos.Domain.Money(sale.TaxCents).ToString();
        TotalDisplay = new Pos.Domain.Money(sale.TotalCents).ToString();
        PaidDisplay = new Pos.Domain.Money(sale.PaidCents).ToString();
        ChangeDisplay = new Pos.Domain.Money(sale.ChangeCents).ToString();
        HasChange = sale.ChangeCents > 0;
        CompletedAt = DateTime.Now.ToString("dd/MM/yyyy HH:mm");

        foreach (SaleResultLine l in sale.Lines)
        {
            string qtyDisplay = (l.QtyMilli / 1000m).ToString("0.###");
            string unitSymbol = string.IsNullOrEmpty(l.UnitSymbol) ? "pc" : l.UnitSymbol;
            string priceDisplay = (l.UnitPriceCents / 100m).ToString("0.00");

            // Detail line: "@ KES 400.00/KG". Only shown when the cashier
            // used a non-base unit or a fractional qty — when the math is
            // trivial (3 pieces @ 5 = 15) we skip it for cleaner paper.
            bool isBaseUnit = l.FactorMilli == 1000;
            bool isWholeQty = l.QtyMilli % 1000 == 0;

            string detail = "";
            if (!isBaseUnit)
            {
                // Non-base unit: always show the per-unit price so the
                // customer can verify the conversion (e.g. 0.75 KG @ 400 = 300).
                detail = $"(@ KES {priceDisplay}/{unitSymbol})";
            }
            else if (!isWholeQty)
            {
                // Base unit but fractional qty (rare but possible for Metre/Litre).
                detail = $"(@ KES {priceDisplay}/{unitSymbol})";
            }

            Lines.Add(new SaleReceiptLine
            {
                ProductName = l.ProductName,
                QtyDisplay = qtyDisplay,
                UnitSymbol = unitSymbol,
                TotalDisplay = (l.LineTotalCents / 100m).ToString("0.00"),
                UnitDetailLine = detail,
            });
        }

        foreach (SaleResultPayment p in sale.Payments)
        {
            Payments.Add($"{p.Method}: {(p.AmountCents / 100m):0.00}");
        }
    }

    public string ShopName { get; }
    public string ShopDetails { get; }
    public string ReceiptFooter { get; }
    public string ReceiptNumber { get; }
    public string CompletedAt { get; }
    public string SubtotalDisplay { get; }
    public string TaxDisplay { get; }
    public string TotalDisplay { get; }
    public string PaidDisplay { get; }
    public string ChangeDisplay { get; }
    public bool HasChange { get; }

    public ObservableCollection<SaleReceiptLine> Lines { get; } = new();
    public ObservableCollection<string> Payments { get; } = new();
}
