using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Customers;
using Pos.Domain;
using Pos.Domain.Customers;

namespace Pos.Desktop.ViewModels;

/// <summary>
/// One row in the statement. Debit and credit are separate display strings
/// — only one is populated per row (empty on the other side). This is the
/// classic two-column ledger layout shop owners already know.
/// </summary>
public sealed record StatementRow(
    string Date,
    string Type,
    string Reference,
    string DebitDisplay,
    string CreditDisplay,
    string BalanceDisplay);

public partial class StatementViewModel : ObservableObject
{
    private readonly ICustomerRepository _customers;
    private readonly ILogger<StatementViewModel> _logger;

    [ObservableProperty]
    private int customerId;

    [ObservableProperty]
    private string customerName = "";

    [ObservableProperty]
    private string phone = "";

    [ObservableProperty]
    private string balanceDisplay = "0.00";

    [ObservableProperty]
    private string statusText = "";

    [ObservableProperty]
    private bool isBusy;

    public ObservableCollection<StatementRow> Rows { get; } = new();

    public StatementViewModel(ICustomerRepository customers, ILogger<StatementViewModel> logger)
    {
        _customers = customers;
        _logger = logger;
    }

    public async Task LoadAsync()
    {
        try
        {
            IsBusy = true;

            Customer? customer = await _customers.GetByIdAsync(CustomerId);
            if (customer is null)
            {
                StatusText = "Customer not found.";
                return;
            }

            CustomerName = customer.Name;
            Phone = customer.Phone ?? "";

            IReadOnlyList<CustomerLedgerEntry> ledger = await _customers.ListLedgerAsync(CustomerId);

            Rows.Clear();
            long running = 0;
            foreach (CustomerLedgerEntry entry in ledger)
            {
                running = checked(running + entry.AmountCents);
                Rows.Add(BuildRow(entry, running));
            }

            BalanceDisplay = new Money(running).ToString();
            StatusText = ledger.Count == 1
                ? $"1 entry · balance {BalanceDisplay} KES"
                : $"{ledger.Count} entries · balance {BalanceDisplay} KES";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Statement load failed for customer {Id}", CustomerId);
            StatusText = $"Could not load statement: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static StatementRow BuildRow(CustomerLedgerEntry entry, long runningBalanceCents)
    {
        string reference = BuildReference(entry);
        long abs = Math.Abs(entry.AmountCents);
        string absDisplay = new Money(abs).ToString();
        string runningDisplay = new Money(runningBalanceCents).ToString();

        return new StatementRow(
            Date: entry.BusinessDate,
            Type: entry.DisplayType,
            Reference: reference,
            DebitDisplay: entry.AmountCents > 0 ? absDisplay : "",
            CreditDisplay: entry.AmountCents < 0 ? absDisplay : "",
            BalanceDisplay: runningDisplay);
    }

    private static string BuildReference(CustomerLedgerEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Reference))
        {
            string method = string.IsNullOrWhiteSpace(entry.Method) ? "" : entry.Method + " ";
            return $"{method}{entry.Reference}".Trim();
        }

        if (!string.IsNullOrWhiteSpace(entry.Method) &&
            entry.EntryType == LedgerEntryType.Payment)
        {
            return entry.Method;
        }

        if (!string.IsNullOrWhiteSpace(entry.Notes))
        {
            return entry.Notes;
        }

        if (entry.SaleId is not null)
        {
            return $"Sale #{entry.SaleId}";
        }

        if (entry.ReturnId is not null)
        {
            return $"Return #{entry.ReturnId}";
        }

        return "";
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadAsync();
    }
}
