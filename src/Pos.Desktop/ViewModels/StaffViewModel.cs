using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Users;
using Pos.Desktop.Views;

namespace Pos.Desktop.ViewModels;

public partial class StaffViewModel : ObservableObject
{
    private readonly IUserAdminService _admin;
    private readonly IUserSession _session;
    private readonly ILogger<StaffViewModel> _logger;

    [ObservableProperty]
    private string statusText = "";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool showArchived;

    public ObservableCollection<StaffMember> Staff { get; } = new();

    /// <summary>Set by App composition — (staffId, isEditMode) → dialog.</summary>
    public Func<int?, AddStaffView>? CreateAddStaffDialog { get; set; }

    public StaffViewModel(
        IUserAdminService admin,
        IUserSession session,
        ILogger<StaffViewModel> logger)
    {
        _admin = admin;
        _session = session;
        _logger = logger;
    }

    public async Task LoadAsync()
    {
        try
        {
            IsBusy = true;

            IReadOnlyList<StaffMember> list = await _admin.ListStaffAsync(includeArchived: ShowArchived);

            Staff.Clear();
            foreach (StaffMember m in list)
            {
                Staff.Add(m);
            }

            int active = list.Count(x => x.IsActive);
            int admins = list.Count(x => x.IsActive && x.Role == "ADMIN");
            StatusText = $"{active} active · {admins} admin{(admins == 1 ? "" : "s")}" +
                         (ShowArchived ? $" · showing archived" : "");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Staff list load failed");
            StatusText = "Could not load staff list.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnShowArchivedChanged(bool value)
    {
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task AddStaffAsync()
    {
        if (CreateAddStaffDialog is null) return;

        AddStaffView dialog = CreateAddStaffDialog(null);
        if (dialog.ShowDialog() == true)
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task EditStaffAsync(StaffMember? member)
    {
        if (member is null || CreateAddStaffDialog is null) return;

        AddStaffView dialog = CreateAddStaffDialog(member.Id);
        if (dialog.ShowDialog() == true)
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task ArchiveStaffAsync(StaffMember? member)
    {
        if (member is null) return;

        if (member.Id == _session.CurrentUserId)
        {
            ConfirmDialog.Create(
                "Cannot remove yourself",
                "You can't remove the account you're currently logged in as. Ask another Admin to do it.",
                confirmLabel: "Understood")
                .ShowDialog();
            return;
        }

        if (member.Role == "ADMIN")
        {
            int otherActiveAdmins = Staff.Count(x => x.IsActive && x.Role == "ADMIN" && x.Id != member.Id);
            if (otherActiveAdmins == 0)
            {
                ConfirmDialog.Create(
                    "Cannot remove the last Admin",
                    "This is the only active Admin. Promote another staff member to Admin first, then try again.",
                    confirmLabel: "Understood")
                    .ShowDialog();
                return;
            }
        }

        ConfirmDialog dlg = ConfirmDialog.CreateDanger(
            "Remove this staff member?",
            $"{member.Name} will no longer be able to log in. " +
            "Their past sales, deliveries and audit entries remain intact.",
            confirmLabel: "Remove staff");

        if (dlg.ShowDialog() != true) return;

        try
        {
            IsBusy = true;
            await _admin.ArchiveStaffAsync(member.Id);
            await LoadAsync();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Archive refused: {Message}", ex.Message);
            ConfirmDialog.Create(
                "Cannot remove",
                ex.Message,
                confirmLabel: "Understood")
                .ShowDialog();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Archive staff {Id} failed", member.Id);
            StatusText = $"Could not remove: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UnarchiveStaffAsync(StaffMember? member)
    {
        if (member is null) return;

        try
        {
            IsBusy = true;
            await _admin.UnarchiveStaffAsync(member.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unarchive staff {Id} failed", member.Id);
            StatusText = $"Could not restore: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadAsync();
    }
}
