using System.Windows;
using Pos.Desktop.ViewModels;

namespace Pos.Desktop.Views;

public partial class AddSupplierView : Window
{
    public AddSupplierView(AddSupplierViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.Saved += (_, _) =>
        {
            DialogResult = true;
            Close();
        };

        Loaded += async (_, _) => await viewModel.LoadAsync();
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
