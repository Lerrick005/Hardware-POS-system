using System.Windows;
using System.Windows.Controls;
using Pos.Desktop.ViewModels;

namespace Pos.Desktop.Views;

public partial class AddStaffView : Window
{
    private readonly AddStaffViewModel _vm;

    public AddStaffView(AddStaffViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        vm.PropertyChanged += OnViewModelPropertyChanged;

        vm.Saved += (_, _) =>
        {
            DialogResult = true;
            Close();
        };

        Loaded += async (_, _) =>
        {
            await vm.LoadAsync();
            PinBox.Focus();
        };
        SourceInitialized += (_, _) => ClampToScreen();
    }

    private void OnPinChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box) _vm.Pin = box.Password;
    }

    private void OnPinConfirmChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box) _vm.PinConfirm = box.Password;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AddStaffViewModel.Pin) && PinBox.Password != _vm.Pin)
        {
            PinBox.Password = _vm.Pin;
        }
        else if (e.PropertyName == nameof(AddStaffViewModel.PinConfirm) && PinConfirmBox.Password != _vm.PinConfirm)
        {
            PinConfirmBox.Password = _vm.PinConfirm;
        }
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
