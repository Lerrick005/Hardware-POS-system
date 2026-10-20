using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Users;

namespace Pos.Desktop.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly IUserRepository _users;
    private readonly IUserSession _session;
    private readonly ILogger<LoginViewModel> _logger;

    [ObservableProperty]
    private LoginUserRow? selectedStaff;

    [ObservableProperty]
    private string pin = "";

    [ObservableProperty]
    private string pinConfirm = "";

    [ObservableProperty]
    private string errorMessage = "";

    [ObservableProperty]
    private string statusText = "";

    [ObservableProperty]
    private bool isBusy;

    public ObservableCollection<LoginUserRow> Staff { get; } = new();

    /// <summary>Fired after a successful login. App.xaml.cs listens and opens MainWindow.</summary>
    public event EventHandler? LoggedIn;

    /// <summary>Fired when the user chooses to quit the app from the login screen.</summary>
    public event EventHandler? Cancelled;

    public bool HasSelection => SelectedStaff is not null;

    public bool NeedsPinSetup => SelectedStaff is not null && !SelectedStaff.HasPinSet;

    public bool NeedsPinEntry => SelectedStaff is not null && SelectedStaff.HasPinSet;

    public string HeadingText => NeedsPinSetup
        ? $"Welcome, {SelectedStaff?.Name}"
        : SelectedStaff is null
            ? "Who is using the till?"
            : $"Enter PIN for {SelectedStaff.Name}";

    public string SubheadingText => NeedsPinSetup
        ? "Set a PIN to protect this account. You'll use it every time you log in."
        : SelectedStaff is null
            ? "Tap your name to continue."
            : "Type your PIN and press Enter.";

    public string ActionButtonText => NeedsPinSetup ? "Set PIN and log in" : "Log in";

    public LoginViewModel(
        IUserRepository users,
        IUserSession session,
        ILogger<LoginViewModel> logger)
    {
        _users = users;
        _session = session;
        _logger = logger;
    }

    public async Task LoadAsync()
    {
        try
        {
            IsBusy = true;

            IReadOnlyList<LoginUserRow> list = await _users.ListForLoginAsync();

            Staff.Clear();
            foreach (LoginUserRow row in list)
            {
                Staff.Add(row);
            }

            if (Staff.Count == 0)
            {
                ErrorMessage = "No staff accounts found. The database may not be set up. Contact your installer.";
                StatusText = "";
                return;
            }

            // Auto-select if there's only one user — common case for small shops.
            if (Staff.Count == 1)
            {
                SelectStaff(Staff[0]);
            }
            else
            {
                StatusText = $"{Staff.Count} staff members on this till.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load staff list");
            ErrorMessage = "Could not load staff list. Try restarting the app.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SelectStaff(LoginUserRow? staff)
    {
        if (staff is null) return;

        SelectedStaff = staff;
        Pin = "";
        PinConfirm = "";
        ErrorMessage = "";
        StatusText = "";
        NotifyComputedChanged();
    }

    [RelayCommand]
    private void Back()
    {
        SelectedStaff = null;
        Pin = "";
        PinConfirm = "";
        ErrorMessage = "";
        StatusText = "";
        NotifyComputedChanged();
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task SubmitAsync()
    {
        ErrorMessage = "";

        if (SelectedStaff is null)
        {
            ErrorMessage = "Tap your name first.";
            return;
        }

        try
        {
            IsBusy = true;

            if (NeedsPinSetup)
            {
                await SetPinAndLoginAsync();
            }
            else
            {
                await LoginWithPinAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed for user {UserId}", SelectedStaff.Id);
            ErrorMessage = "Something went wrong. Try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoginWithPinAsync()
    {
        if (string.IsNullOrWhiteSpace(Pin))
        {
            ErrorMessage = "Enter your PIN.";
            return;
        }

        LoginResult result = await _users.LoginAsync(SelectedStaff!.Id, Pin);

        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage ?? "Login failed.";
            Pin = "";
            return;
        }

        // Establish the session in memory.
        _session.Start(result.UserId!.Value, result.UserName!, result.Role!);

        // Clear PIN from memory immediately after login succeeds.
        Pin = "";
        PinConfirm = "";

        LoggedIn?.Invoke(this, EventArgs.Empty);
    }

    private async Task SetPinAndLoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Pin))
        {
            ErrorMessage = "Choose a PIN (4 to 6 digits).";
            return;
        }

        if (Pin.Length < 4 || Pin.Length > 6 || !Pin.All(char.IsDigit))
        {
            ErrorMessage = "PIN must be 4 to 6 digits, all numbers.";
            return;
        }

        if (Pin != PinConfirm)
        {
            ErrorMessage = "PINs do not match. Try again.";
            PinConfirm = "";
            return;
        }

        await _users.SetPinAsync(SelectedStaff!.Id, Pin);

        // Reload the staff list so HasPinSet updates.
        IReadOnlyList<LoginUserRow> list = await _users.ListForLoginAsync();
        Staff.Clear();
        foreach (LoginUserRow row in list)
        {
            Staff.Add(row);
        }

        // Log in immediately after setting the PIN.
        LoginResult result = await _users.LoginAsync(SelectedStaff.Id, Pin);
        if (!result.Success)
        {
            ErrorMessage = "PIN was saved but login failed. Try again.";
            Pin = "";
            PinConfirm = "";
            return;
        }

        _session.Start(result.UserId!.Value, result.UserName!, result.Role!);

        Pin = "";
        PinConfirm = "";

        LoggedIn?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedStaffChanged(LoginUserRow? value)
    {
        NotifyComputedChanged();
    }

    private void NotifyComputedChanged()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(NeedsPinSetup));
        OnPropertyChanged(nameof(NeedsPinEntry));
        OnPropertyChanged(nameof(HeadingText));
        OnPropertyChanged(nameof(SubheadingText));
        OnPropertyChanged(nameof(ActionButtonText));
    }
}
