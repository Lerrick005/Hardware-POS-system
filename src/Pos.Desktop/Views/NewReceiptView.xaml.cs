using System.Windows;
using Pos.Desktop.ViewModels;

namespace Pos.Desktop.Views;

public partial class NewReceiptView : Window
{
    public NewReceiptView(NewReceiptViewModel viewModel)
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

    /// <summary>
    /// Shrinks the window if it would exceed the primary screen's working area
    /// (which already accounts for taskbar). SystemParameters.WorkArea is in DIPs,
    /// so no DPI math is required.
    /// Limitation: clamps against the primary screen only; multi-monitor owners
    /// who drag the dialog to a smaller secondary monitor will not be re-clamped.
    /// </summary>
    private void ClampToScreen()
    {
        const double Buffer = 24;
        double maxW = SystemParameters.WorkArea.Width - Buffer;
        double maxH = SystemParameters.WorkArea.Height - Buffer;

        if (Width > maxW)
        {
            Width = maxW;
        }

        if (Height > maxH)
        {
            Height = maxH;
        }
    }
}
