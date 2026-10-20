using System.Windows;

namespace Pos.Desktop.Views;

public partial class ConfirmDialog : Window
{
    private ConfirmDialog() { }

    /// <summary>
    /// Confirmation dialog with a danger-styled (red) confirm button. Use for
    /// destructive actions: remove, void, restore-over-existing, reset.
    /// </summary>
    public static ConfirmDialog CreateDanger(string title, string message, string confirmLabel = "Continue")
        => CreateCore(title, message, confirmLabel, danger: true);

    /// <summary>
    /// Confirmation dialog with a primary-styled confirm button. Use for
    /// non-destructive but consequential actions.
    /// </summary>
    public static ConfirmDialog Create(string title, string message, string confirmLabel = "Continue")
        => CreateCore(title, message, confirmLabel, danger: false);

    private static ConfirmDialog CreateCore(string title, string message, string confirmLabel, bool danger)
    {
        ConfirmDialog dlg = new();
        dlg.InitializeComponent();

        dlg.Title = title;
        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;
        dlg.ConfirmButton.Content = confirmLabel;
        dlg.CancelButton.IsCancel = true;

        if (danger)
        {
            dlg.ConfirmButton.Style = (Style)dlg.FindResource("Button.Danger");
        }

        if (System.Windows.Application.Current?.MainWindow is Window owner && owner.IsLoaded)
        {
            dlg.Owner = owner;
        }

        return dlg;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
