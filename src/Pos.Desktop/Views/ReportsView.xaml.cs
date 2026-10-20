using System.Windows;
using System.Windows.Controls;
using Pos.Desktop.ViewModels;

namespace Pos.Desktop.Views;

public partial class ReportsView : UserControl
{
    public ReportsView() => InitializeComponent();

    private void OnTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag && DataContext is ReportsViewModel vm)
        {
            vm.ActiveTab = tag;
        }
    }
}
