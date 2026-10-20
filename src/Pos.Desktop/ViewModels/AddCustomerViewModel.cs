using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Catalog;
using Pos.Application.Customers;
using Pos.Application.Users;
using Pos.Domain.Catalog;
using Pos.Domain.Customers;

namespace Pos.Desktop.ViewModels;

public partial class AddCustomerViewModel : ObservableObject
{
    private readonly ICustomerRepository _customers;
    private readonly IPriceTierRepository _tiers;
    private readonly IUserSession _session;
    private readonly ILogger<AddCustomerViewModel> _logger;

    [ObservableProperty]
    private int? customerId;

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

    public bool IsEditMode => CustomerId is not null;
    public string WindowTitle => IsEditMode ? "Edit customer" : "Add customer";
    public string SaveButtonText => IsEditMode ? "Save changes" : "Save customer";

    public string OpeningBalanceLabel => IsEditMode ? "Current balance (KES)" : "Opening balance (KES)";

    public string OpeningBalanceHelp => IsEditMode
        ? "Shown for reference. Opening balance cannot be changed after the customer is created."
        : "Enter the amount this customer already owes you. Leave 0 for a new customer.";

    public event EventHandler<int>? Saved;

    public AddCustomerViewModel(
        ICustomerRepository customers,
        IPriceTierRepository tiers,
        IUserSession session,
        ILogger<AddCustomerViewModel> logger)
    {
        _customers = customers;
        _tiers = tiers;
        _session = session;
        _logger = logger;
    }

    public async Task LoadAsync()
    {
        PriceTiers.Clear();
        foreach (PriceTier t in await _tiers.ListAsync(includeInactive: false))
        {
            PriceTiers.Add(t);
        }

        if (CustomerId is null)
        {
            SelectedPriceTier = PriceTiers.FirstOrDefault(t => t.IsDefault) ?? PriceTiers.FirstOrDefault();
            return;
        }

        Customer? existing = await _customers.GetByIdAsync(CustomerId.Value);
        if (existing is null)
        {
            ErrorMessage = "This customer no longer exists.";
            return;
        }

        Name = existing.Name;
        Phone = existing.Phone ?? "";
        CreditLimitText = ShillingsFromCents(existing.CreditLimitCents);
        Notes = existing.Notes ?? "";
        SelectedPriceTier = PriceTiers.FirstOrDefault(t => t.Id == existing.PriceTierId)
                            ?? PriceTiers.FirstOrDefault();

        long balance = await _customers.GetBalanceCentsAsync(existing.Id);
        OpeningBalanceText = ShillingsFromCents(balance);

        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(SaveButtonText));
        OnPropertyChanged(nameof(OpeningBalanceLabel));
        OnPropertyChanged(nameof(OpeningBalanceHelp));
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

        if (!IsEditMode && !TryParseShillings(OpeningBalanceText, out _, out string? balanceErr))
        {
            ErrorMessage = balanceErr ?? "Opening balance is not valid.";
            return;
        }

        try
        {
            IsSaving = true;

            if (IsEditMode)
            {
                Customer? existing = await _customers.GetByIdAsync(CustomerId!.Value);
                if (existing is null)
                {
                    ErrorMessage = "This customer no longer exists.";
                    return;
                }

                existing.Name = Name.Trim();
                existing.Phone = string.IsNullOrWhiteSpace(Phone) ? null : Phone.Trim();
                existing.PriceTierId = SelectedPriceTier?.Id;
                existing.CreditLimitCents = creditLimitCents;
                existing.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();

                await _customers.UpdateAsync(existing);
                Saved?.Invoke(this, existing.Id);
            }
            else
            {
                TryParseShillings(OpeningBalanceText, out long openingBalanceCents, out _);

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
                    today,
                    userId: _session.RequireUserId);
                Saved?.Invoke(this, id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Save customer failed for {Name}", Name);
            ErrorMessage = $"Could not save: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    private static string ShillingsFromCents(long cents) =>
        (cents / 100m).ToString("0.##", CultureInfo.InvariantCulture);

    private static bool TryParseShillings(string raw, out long cents, out string? error)
    {
        cents = 0;
        error = null;

        string trimmed = raw?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            return true;
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
