using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Users;

namespace Pos.Desktop.ViewModels;

public partial class AddStaffViewModel : ObservableObject
{
    private readonly IUserAdminService _admin;
    private readonly ILogger<AddStaffViewModel> _logger;

    [ObservableProperty]
    private int? staffId;

    [ObservableProperty]
    private string name = "";

    [ObservableProperty]
    private string selectedRole = "CASHIER";

    [ObservableProperty]
    private string pin = "";

    [ObservableProperty]
    private string pinConfirm = "";

    [ObservableProperty]
    private string errorMessage = "";

    [ObservableProperty]
    private bool isSaving;

    public IReadOnlyList<string> AvailableRoles { get; } = new[] { "CASHIER", "MANAGER", "ADMIN" };

    public bool IsEditMode => StaffId is not null;
    public string WindowTitle => IsEditMode ? "Edit staff member" : "Add staff member";
    public string SaveButtonText => IsEditMode ? "Save changes" : "Add staff";
    public string PinLabel => IsEditMode ? "New PIN (leave blank to keep current)" : "PIN *";
    public string PinHelp => IsEditMode
        ? "Only fill in if the staff member wants to change their PIN. 4 to 6 digits."
        : "4 to 6 digits. The staff member will type this at the login screen.";

    public event EventHandler<int>? Saved;

    public AddStaffViewModel(IUserAdminService admin, ILogger<AddStaffViewModel> logger)
    {
        _admin = admin;
        _logger = logger;
    }

    public async Task LoadAsync()
    {
        if (StaffId is null) return;

        StaffMember? existing = await _admin.GetByIdAsync(StaffId.Value);
        if (existing is null)
        {
            ErrorMessage = "This staff member no longer exists.";
            return;
        }

        Name = existing.Name;
        SelectedRole = existing.Role;
        // PIN fields left blank — only fill if changing.

        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(SaveButtonText));
        OnPropertyChanged(nameof(PinLabel));
        OnPropertyChanged(nameof(PinHelp));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = "";

        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Name is required.";
            return;
        }

        // PIN validation is conditional:
        // - Create mode: PIN and confirm required
        // - Edit mode: PIN optional; if either field is filled, both must match and be valid
        bool pinProvided = !string.IsNullOrWhiteSpace(Pin) || !string.IsNullOrWhiteSpace(PinConfirm);

        if (!IsEditMode && !pinProvided)
        {
            ErrorMessage = "PIN is required when adding a staff member.";
            return;
        }

        if (pinProvided)
        {
            if (string.IsNullOrWhiteSpace(Pin) || Pin.Length < 4 || Pin.Length > 6 || !Pin.All(char.IsDigit))
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
        }

        try
        {
            IsSaving = true;

            if (IsEditMode)
            {
                await _admin.UpdateStaffAsync(StaffId!.Value, Name.Trim(), SelectedRole);

                if (pinProvided)
                {
                    await _admin.ChangePinAsync(StaffId.Value, Pin);
                }

                Saved?.Invoke(this, StaffId.Value);
            }
            else
            {
                int id = await _admin.AddStaffAsync(Name.Trim(), SelectedRole, Pin);
                Saved?.Invoke(this, id);
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Staff save refused: {Message}", ex.Message);
            ErrorMessage = ex.Message;
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Staff save validation: {Message}", ex.Message);
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Save staff failed for {Name}", Name);
            ErrorMessage = $"Could not save: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }
}
