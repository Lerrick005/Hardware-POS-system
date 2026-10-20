using System.Windows;
using Pos.Desktop.ViewModels;

namespace Pos.Desktop.Views;

public partial class CategoriesView : Window
{
    public CategoriesView(CategoriesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Loaded += async (_, _) => await viewModel.LoadAsync();
        SourceInitialized += (_, _) => ClampToScreen();
    }

    private void OnClose(object sender, RoutedEventArgs e)
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
