using CommunityToolkit.Mvvm.ComponentModel;

namespace Pos.Desktop.ViewModels;

public partial class PlaceholderViewModel : ObservableObject
{
    public string Title { get; }
    public string Message { get; }

    public PlaceholderViewModel(string title, string message)
    {
        Title = title;
        Message = message;
    }
}
