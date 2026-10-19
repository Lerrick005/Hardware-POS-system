using CommunityToolkit.Mvvm.ComponentModel;

namespace Pos.Desktop.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    public string Greeting { get; } = "Welcome";
    public string Subtitle { get; } = "Get the shop ready to sell.";

    public IReadOnlyList<string> Checklist { get; } = new[]
    {
        "Enter shop details (name, phone, KRA PIN)",
        "Add products or import from Excel",
        "Add suppliers",
        "Add credit customers",
        "Set up automatic backups",
        "Print a test receipt",
    };
}
