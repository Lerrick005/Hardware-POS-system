using System.Windows;
using Pos.Application.Stock;
using Pos.Desktop.ViewModels;

namespace Pos.Desktop.Views;

public partial class AdjustStockView : Window
{
    public AdjustStockView(AdjustStockViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        vm.Saved += (_, result) =>
        {
            MessageBox.Show(
                $"Stock adjusted.\n\n{result.PreviousStockMilli / 1000m:0.###} → {result.NewStockMilli / 1000m:0.###}",
                "Stock adjusted",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            DialogResult = true;
            Close();
        };

        Loaded += async (_, _) => await vm.LoadAsync();
        SourceInitialized += (_, _) => ClampToScreen();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
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
