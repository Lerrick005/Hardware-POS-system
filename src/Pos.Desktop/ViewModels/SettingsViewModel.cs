using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Backup;
using Pos.Application.Settings;
using Pos.Desktop.Views;

namespace Pos.Desktop.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IBackupService _backups;
    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty] private string shopName = "";
    [ObservableProperty] private string address = "";
    [ObservableProperty] private string phone = "";
    [ObservableProperty] private string kraPin = "";
    [ObservableProperty] private string receiptFooter = "Thank you for your business!";
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private string errorMessage = "";
    [ObservableProperty] private bool isBusy;

    [ObservableProperty] private string lastBackupText = "Never backed up";
    [ObservableProperty] private string backupStatusText = "";
    [ObservableProperty] private bool isBackingUp;
    [ObservableProperty] private BackupInfo? selectedBackup;

    public ObservableCollection<BackupInfo> Backups { get; } = new();

    public event EventHandler? ProfileSaved;

    public SettingsViewModel(
        ISettingsService settings,
        IBackupService backups,
        ILogger<SettingsViewModel> logger)
    {
        _settings = settings;
        _backups = backups;
        _logger = logger;
    }

    public async Task LoadAsync()
    {
        try
        {
            IsBusy = true;

            ShopProfile profile = await _settings.GetShopProfileAsync();

            ShopName = profile.ShopName;
            Address = profile.Address;
            Phone = profile.Phone;
            KraPin = profile.KraPin;
            ReceiptFooter = string.IsNullOrWhiteSpace(profile.ReceiptFooter)
                ? "Thank you for your business!"
                : profile.ReceiptFooter;

            StatusText = string.IsNullOrWhiteSpace(profile.ShopName)
                ? "Shop profile not yet filled in."
                : "Shop profile loaded.";

            await RefreshBackupsInternalAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Settings load failed");
            ErrorMessage = "Could not load settings.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = "";

        if (string.IsNullOrWhiteSpace(ShopName))
        {
            ErrorMessage = "Shop name is required — it appears on every receipt.";
            return;
        }

        try
        {
            IsBusy = true;

            ShopProfile profile = new()
            {
                ShopName = ShopName.Trim(),
                Address = (Address ?? "").Trim(),
                Phone = (Phone ?? "").Trim(),
                KraPin = (KraPin ?? "").Trim(),
                ReceiptFooter = string.IsNullOrWhiteSpace(ReceiptFooter)
                    ? "Thank you for your business!"
                    : ReceiptFooter.Trim(),
            };

            await _settings.SaveShopProfileAsync(profile);
            StatusText = "Saved. New receipts will use these details.";
            ProfileSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Settings save failed");
            ErrorMessage = $"Could not save settings: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task BackupNowAsync()
    {
        BackupStatusText = "";

        try
        {
            IsBackingUp = true;
            BackupInfo created = await _backups.CreateBackupAsync("manual");
            BackupStatusText = $"Backup created: {created.FileName}";
            await RefreshBackupsInternalAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual backup failed");
            BackupStatusText = $"Backup failed: {ex.Message}";
        }
        finally
        {
            IsBackingUp = false;
        }
    }

    [RelayCommand]
    private async Task RestoreBackupAsync(BackupInfo? backup)
    {
        if (backup is null)
        {
            return;
        }

        string relative = FormatRelativeTime(backup.CreatedAtUtc);
        string size = FormatBytes(backup.SizeBytes);

        ConfirmDialog dlg = ConfirmDialog.CreateDanger(
            "Restore this backup?",
            $"This will replace all current data with the backup from {relative} ({size}). " +
            "A safety backup of your current data will be taken first. " +
            "You will need to close and reopen the app after the restore.",
            confirmLabel: "Restore backup");

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        try
        {
            IsBackingUp = true;
            await _backups.RestoreBackupAsync(backup.Path);

            System.Windows.MessageBox.Show(
                "Restore complete.\n\nPlease close and reopen the app for the restored data to take effect.",
                "Restore complete",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);

            await RefreshBackupsInternalAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Restore failed for {Path}", backup.Path);
            BackupStatusText = $"Restore failed: {ex.Message}";
        }
        finally
        {
            IsBackingUp = false;
        }
    }

    [RelayCommand]
    private async Task RefreshBackupsAsync()
    {
        await RefreshBackupsInternalAsync();
    }

    private async Task RefreshBackupsInternalAsync()
    {
        try
        {
            IReadOnlyList<BackupInfo> list = await _backups.ListBackupsAsync();

            Backups.Clear();
            foreach (BackupInfo b in list.Take(10))
            {
                Backups.Add(b);
            }

            DateTime? last = list.FirstOrDefault()?.CreatedAtUtc;
            LastBackupText = last is null
                ? "Never backed up — set one up below."
                : $"Last backup: {FormatRelativeTime(last.Value)}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list backups");
            LastBackupText = "Could not list backups.";
        }
    }

    private static string FormatRelativeTime(DateTime utcWhen)
    {
        TimeSpan age = DateTime.UtcNow - utcWhen;
        if (age.TotalSeconds < 30) return "just now";
        if (age.TotalMinutes < 60) return $"{(int)age.TotalMinutes} minutes ago";
        if (age.TotalHours < 24) return $"{(int)age.TotalHours} hours ago";
        if (age.TotalDays < 30) return $"{(int)age.TotalDays} days ago";
        return utcWhen.ToLocalTime().ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / (1024.0 * 1024.0):0.#} MB";
    }
}
