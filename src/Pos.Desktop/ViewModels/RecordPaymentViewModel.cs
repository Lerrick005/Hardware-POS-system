using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Customers;
using Pos.Domain.Customers;

namespace Pos.Desktop.ViewModels;

public partial class RecordPaymentViewModel : ObservableObject
{
    private static readonly string[] PaymentMethods = { "CASH", "MPESA", "CARD", "BANK", "OTHER" };

    private readonly ICustomerRepository _customers;
    private readonly ICustomerAccountService _account;
    private readonly ILogger<RecordPaymentViewModel> _logger;

    [ObservableProperty]
    private int customerId;

    [ObservableProperty]
    private string customerName = "";

    [ObservableProperty]
    private string currentBalanceDisplay = "0.00";

    [ObservableProperty]
    private string amountText = "";

    [ObservableProperty]
    private string selectedMethod = "CASH";

    [ObservableProperty]
    private string reference = "";

    [ObservableProperty]
    private string notes = "";

    [ObservableProperty]
    private string errorMessage = "";

    [ObservableProperty]
    private bool isSaving;

    public IReadOnlyList<string> Methods => PaymentMethods;

    public string ReferenceLabel => SelectedMethod switch
    {
        "CASH" => "Reference (optional)",
        "MPESA" => "M-Pesa code",
        "CARD" => "Card reference",
        "BANK" => "Bank reference",
        _ => "Reference",
    };

    public bool ReferenceRequired => SelectedMethod is "MPESA" or "CARD" or "BANK";

    public event EventHandler<long>? Saved;

    public RecordPaymentViewModel(
        ICustomerRepository customers,
        ICustomerAccountService account,
        ILogger<RecordPaymentViewModel> logger)
    {
        _customers = customers;
        _account = account;
        _logger = logger;
    }

    public async Task LoadAsync()
    {
        Customer? existing = await _customers.GetByIdAsync(CustomerId);
        if (existing is null)
        {
            ErrorMessage = "This customer no longer exists.";
            return;
        }

        CustomerName = existing.Name;
        long balance = await _customers.GetBalanceCentsAsync(CustomerId);
        CurrentBalanceDisplay = new Pos.Domain.Money(balance).ToString();
    }

    partial void OnSelectedMethodChanged(string value)
    {
        OnPropertyChanged(nameof(ReferenceLabel));
        OnPropertyChanged(nameof(ReferenceRequired));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = "";

        if (!TryParseShillings(AmountText, out long amountCents, out string? amountErr))
        {
            ErrorMessage = amountErr ?? "Amount is not valid.";
            return;
        }

        if (amountCents <= 0)
        {
            ErrorMessage = "Amount must be greater than zero.";
            return;
        }

        if (ReferenceRequired && string.IsNullOrWhiteSpace(Reference))
        {
            ErrorMessage = $"{ReferenceLabel} is required for {SelectedMethod}.";
            return;
        }

        try
        {
            IsSaving = true;

            RecordPaymentResult result = await _account.RecordPaymentAsync(new(
                CustomerId: CustomerId,
                AmountCents: amountCents,
                Method: SelectedMethod,
                Reference: string.IsNullOrWhiteSpace(Reference) ? null : Reference.Trim(),
                Notes: string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
                BusinessDate: DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                UserId: null,
                CashSessionId: null));

            Saved?.Invoke(this, result.NewBalanceCents);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Record payment failed for customer {Id}", CustomerId);
            ErrorMessage = $"Could not record payment: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    private static bool TryParseShillings(string raw, out long cents, out string? error)
    {
        cents = 0;
        error = null;

        string trimmed = raw?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            error = "Enter the amount received.";
            return false;
        }

        if (!decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal shillings))
        {
            error = "Enter a number (e.g. 500, 2,500.00).";
            return false;
        }

        if (shillings <= 0)
        {
            error = "Amount must be greater than zero.";
            return false;
        }

        cents = (long)decimal.Round(shillings * 100m, 0, MidpointRounding.AwayFromZero);
        return true;
    }
}
