using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Backup;
using Pos.Application.Settings;
using Pos.Application.Users;

namespace Pos.Desktop.ViewModels;

public partial class SettingsHubViewModel : ObservableObject
{
    private readonly IUserAdminService _staff;
    private readonly ISettingsService _profile;
    private readonly IBackupService _backups;
    private readonly ILogger<SettingsHubViewModel> _logger;

    private readonly StaffViewModel _staffVm;
    private readonly SettingsViewModel _settingsVm;
    private readonly AuditViewModel _auditVm;
    private readonly AboutViewModel _aboutVm;

    [ObservableProperty]
    private object? currentSubView;

    [ObservableProperty]
    private string staffSummary = "Loading...";

    [ObservableProperty]
    private string shopSummary = "Loading...";

    [ObservableProperty]
    private string auditSummary = "Loading...";

    [ObservableProperty]
    private string aboutSummary = "Loading...";

    public bool IsAtHub => CurrentSubView is null;

    public SettingsHubViewModel(
        IUserAdminService staff,
        ISettingsService profile,
        IBackupService backups,
        StaffViewModel staffVm,
        SettingsViewModel settingsVm,
        AuditViewModel auditVm,
        AboutViewModel aboutVm,
        ILogger<SettingsHubViewModel> logger)
    {
        _staff = staff;
        _profile = profile;
        _backups = backups;
        _staffVm = staffVm;
        _settingsVm = settingsVm;
        _auditVm = auditVm;
        _aboutVm = aboutVm;
        _logger = logger;
    }

    public async Task LoadAsync()
    {
        // Returning to Settings always lands on the hub — never on a stale
        // sub-view from a previous visit.
        CurrentSubView = null;
        try
        {
            IReadOnlyList<StaffMember> staffList = await _staff.ListStaffAsync(includeArchived: false);
            int admins = staffList.Count(s => s.Role == "ADMIN");
            StaffSummary = staffList.Count == 1
                ? $"1 member · {admins} admin"
                : $"{staffList.Count} members · {admins} admin{(admins == 1 ? "" : "s")}";

            ShopProfile shop = await _profile.GetShopProfileAsync();
            IReadOnlyList<BackupInfo> backups = await _backups.ListBackupsAsync();
            string shopName = string.IsNullOrWhiteSpace(shop.ShopName) ? "Profile not set" : shop.ShopName;
            string backupText = backups.Count == 0
                ? "No backups yet"
                : $"{backups.Count} backup{(backups.Count == 1 ? "" : "s")} · last {Relative(backups[0].CreatedAtUtc)}";
            ShopSummary = $"{shopName} · {backupText}";

            AuditSummary = await _auditVm.GetSummaryAsync();

            AboutSummary = _aboutVm.GetSummary();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Settings hub load failed");
        }
    }

    partial void OnCurrentSubViewChanged(object? value)
    {
        OnPropertyChanged(nameof(IsAtHub));
    }

    [RelayCommand]
    private async Task OpenStaffAsync()
    {
        await _staffVm.LoadAsync();
        CurrentSubView = _staffVm;
    }

    [RelayCommand]
    private async Task OpenShopAsync()
    {
        await _settingsVm.LoadAsync();
        CurrentSubView = _settingsVm;
    }

    [RelayCommand]
    private async Task OpenAuditAsync()
    {
        await _auditVm.LoadAsync();
        CurrentSubView = _auditVm;
    }

    [RelayCommand]
    private void OpenAbout()
    {
        _aboutVm.Refresh();
        CurrentSubView = _aboutVm;
    }

    [RelayCommand]
    private void BackToHub()
    {
        CurrentSubView = null;
    }

    private static string Relative(DateTime utc)
    {
        TimeSpan age = DateTime.UtcNow - utc;
        if (age.TotalMinutes < 60) return $"{(int)age.TotalMinutes}m ago";
        if (age.TotalHours < 24) return $"{(int)age.TotalHours}h ago";
        return $"{(int)age.TotalDays}d ago";
    }
}

