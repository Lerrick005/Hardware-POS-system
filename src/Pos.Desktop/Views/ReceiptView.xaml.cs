using System.Windows;
using System.Windows.Controls;
using Pos.Application.Sales;
using Pos.Application.Settings;
using Pos.Desktop.ViewModels;

namespace Pos.Desktop.Views;

public partial class ReceiptView : Window
{
    public ReceiptView(SaleResult sale, ShopProfile profile)
    {
        InitializeComponent();
        DataContext = new ReceiptViewModel(sale, profile);
        SourceInitialized += (_, _) => ClampToScreen();
    }

    private void OnPrint(object sender, RoutedEventArgs e)
    {
        PrintDialog printDialog = new PrintDialog();
        if (printDialog.ShowDialog() == true)
        {
            printDialog.PrintVisual(ReceiptPaper, "Receipt");
        }
    }

    private void OnNewSale(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ClampToScreen()
    {
        const double Buffer = 24;
        double maxW = SystemParameters.WorkArea.Width - Buffer;
        double maxH = SystemParameters.WorkArea.Height - Buffer;
        if (Width > maxW) Width = maxW;
        if (Height > maxH) Height = maxH;
    }
}
