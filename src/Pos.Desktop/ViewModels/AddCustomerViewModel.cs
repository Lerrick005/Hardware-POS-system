using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Catalog;
using Pos.Application.Customers;
using Pos.Domain.Catalog;
using Pos.Domain.Customers;

namespace Pos.Desktop.ViewModels;

public partial class AddCustomerViewModel : ObservableObject
{
    private readonly ICustomerRepository _customers;
    private readonly IPriceTierRepository _tiers;
    private readonly ILogger<AddCustomerViewModel> _logger;

    [ObservableProperty]
    private string name = "";

    [ObservableProperty]
    private string phone = "";

    [ObservableProperty]
    private PriceTier? selectedPriceTier;

    [ObservableProperty]
    private string creditLimitText = "0";

    [ObservableProperty]
    private string openingBalanceText = "0";

    [ObservableProperty]
    private string notes = "";

    [ObservableProperty]
    private string errorMessage = "";

    [ObservableProperty]
    private bool isSaving;

    public ObservableCollection<PriceTier> PriceTiers { get; } = new();

    public event EventHandler<int>? Saved;

    public AddCustomerViewModel(
        ICustomerRepository customers,
        IPriceTierRepository tiers,
        ILogger<AddCustomerViewModel> logger)
    {
        _customers = customers;
        _tiers = tiers;
        _logger = logger;
    }

    public async Task LoadAsync()
    {
        PriceTiers.Clear();
        foreach (PriceTier t in await _tiers.ListAsync(includeInactive: false))
        {
            PriceTiers.Add(t);
        }

        SelectedPriceTier = PriceTiers.FirstOrDefault(t => t.IsDefault) ?? PriceTiers.FirstOrDefault();
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

        if (!TryParseShillings(CreditLimitText, out long creditLimitCents, out string? creditErr))
        {
            ErrorMessage = creditErr ?? "Credit limit is not valid.";
            return;
        }

        if (!TryParseShillings(OpeningBalanceText, out long openingBalanceCents, out string? balanceErr))
        {
            ErrorMessage = balanceErr ?? "Opening balance is not valid.";
            return;
        }

        try
        {
            IsSaving = true;

            Customer customer = new()
            {
                Uuid = Guid.NewGuid().ToString("N"),
                Name = Name.Trim(),
                Phone = string.IsNullOrWhiteSpace(Phone) ? null : Phone.Trim(),
                PriceTierId = SelectedPriceTier?.Id,
                CreditLimitCents = creditLimitCents,
                Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
                IsActive = true,
            };

            string today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            int id = await _customers.InsertWithOpeningBalanceAsync(
                customer,
                openingBalanceCents,
                today);

            Saved?.Invoke(this, id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AddCustomer failed for {Name}", Name);
            ErrorMessage = $"Could not save: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>
    /// Parses a shillings amount the user types into integer cents.
    /// Accepts "0", "650", "650.00", "2,500.00". Rejects negatives and non-numbers.
    /// </summary>
    private static bool TryParseShillings(string raw, out long cents, out string? error)
    {
        cents = 0;
        error = null;

        string trimmed = raw?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            return true;   // treat empty as zero
        }

        if (!decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal shillings))
        {
            error = "Enter a number (e.g. 0, 500, 2,500.00).";
            return false;
        }

        if (shillings < 0)
        {
            error = "Amount cannot be negative.";
            return false;
        }

        cents = (long)decimal.Round(shillings * 100m, 0, MidpointRounding.AwayFromZero);
        return true;
    }
}
