using System.Windows;
using System.Windows.Controls;
using Pos.Desktop.ViewModels;

namespace Pos.Desktop.Views;

public partial class LoginView : Window
{
    private readonly LoginViewModel _vm;

    public LoginView(LoginViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        vm.PropertyChanged += OnViewModelPropertyChanged;
        vm.LoggedIn += OnLoggedIn;
        vm.Cancelled += OnCancelled;
        SourceInitialized += (_, _) => ClampToScreen();
        Loaded += async (_, _) =>
        {
            await vm.LoadAsync();
            PinBox.Focus();
        };
    }

    private void OnPinChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box)
        {
            _vm.Pin = box.Password;
        }
    }

    private void OnPinConfirmChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box)
        {
            _vm.PinConfirm = box.Password;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Keep PasswordBox in sync when the VM clears the PIN
        // (e.g. after a wrong PIN, after successful login).
        if (e.PropertyName == nameof(LoginViewModel.Pin) && PinBox.Password != _vm.Pin)
        {
            PinBox.Password = _vm.Pin;
        }
        else if (e.PropertyName == nameof(LoginViewModel.PinConfirm) && PinConfirmBox.Password != _vm.PinConfirm)
        {
            PinConfirmBox.Password = _vm.PinConfirm;
        }
    }

    private void OnLoggedIn(object? sender, EventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancelled(object? sender, EventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnQuit(object sender, RoutedEventArgs e)
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
