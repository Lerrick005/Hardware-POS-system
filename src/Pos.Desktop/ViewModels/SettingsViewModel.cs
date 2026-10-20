using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Settings;

namespace Pos.Desktop.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty]
    private string shopName = "";

    [ObservableProperty]
    private string address = "";

    [ObservableProperty]
    private string phone = "";

    [ObservableProperty]
    private string kraPin = "";

    [ObservableProperty]
    private string receiptFooter = "Thank you for your business!";

    [ObservableProperty]
    private string statusText = "";

    [ObservableProperty]
    private string errorMessage = "";

    [ObservableProperty]
    private bool isBusy;

    /// <summary>
    /// Fired after a successful save so ReceiptView can reload the shop profile
    /// from the DB before opening the next receipt.
    /// </summary>
    public event EventHandler? ProfileSaved;

    public SettingsViewModel(ISettingsService settings, ILogger<SettingsViewModel> logger)
    {
        _settings = settings;
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
}
