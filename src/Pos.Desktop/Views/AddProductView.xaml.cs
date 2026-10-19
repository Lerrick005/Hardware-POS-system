using System.Windows;
using Pos.Desktop.ViewModels;

namespace Pos.Desktop.Views;

public partial class AddProductView : Window
{
    public AddProductView(AddProductViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Saved += (_, _) =>
        {
            DialogResult = true;
            Close();
        };
        Loaded += async (_, _) => await viewModel.LoadAsync();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
