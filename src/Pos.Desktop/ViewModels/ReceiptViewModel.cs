using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Pos.Application.Sales;
using Pos.Application.Settings;

namespace Pos.Desktop.ViewModels;

public sealed class SaleReceiptLine
{
    public string ProductName { get; init; } = "";
    public string QtyDisplay { get; init; } = "";
    public string PriceDisplay { get; init; } = "";
    public string TotalDisplay { get; init; } = "";
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

        foreach (var l in sale.Lines)
        {
            Lines.Add(new SaleReceiptLine
            {
                ProductName = l.ProductName,
                QtyDisplay = (l.QtyMilli / 1000m).ToString("0.###"),
                PriceDisplay = (l.UnitPriceCents / 100m).ToString("0.00"),
                TotalDisplay = (l.LineTotalCents / 100m).ToString("0.00")
            });
        }

        foreach (var p in sale.Payments)
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
