using System.Windows;
using Pos.Application.Cash;
using Pos.Desktop.ViewModels;

namespace Pos.Desktop.Views;

public partial class CloseShiftView : Window
{
    public CloseShiftView(CloseShiftViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        vm.Closed += (_, result) =>
        {
            ShowSummary(result);
            DialogResult = true;
            Close();
        };

        Loaded += async (_, _) => await vm.LoadAsync();
        SourceInitialized += (_, _) => ClampToScreen();
    }

    private void ShowSummary(CloseShiftResult result)
    {
        string varianceText = result.VarianceCents == 0
            ? "Perfect count — no variance."
            : result.VarianceCents < 0
                ? $"Short by {new Pos.Domain.Money(Math.Abs(result.VarianceCents))} KES."
                : $"Over by {new Pos.Domain.Money(result.VarianceCents)} KES.";

        MessageBox.Show(
            $"Shift closed.\n\nExpected: {new Pos.Domain.Money(result.ExpectedCashCents)} KES\n" +
            $"Counted:  {new Pos.Domain.Money(result.CountedCashCents)} KES\n\n" +
            varianceText,
            "Shift closed",
            MessageBoxButton.OK,
            result.VarianceCents == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void OnCancel(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    private void ClampToScreen()
    {
        const double Buffer = 24;
        double maxW = SystemParameters.WorkArea.Width - Buffer;
        double maxH = SystemParameters.WorkArea.Height - Buffer;
        if (Width > maxW) Width = maxW;
        if (Height > maxH) Height = maxH;
    }
}
