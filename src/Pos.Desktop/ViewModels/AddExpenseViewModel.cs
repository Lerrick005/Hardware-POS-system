using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Expenses;
using Pos.Application.Users;

namespace Pos.Desktop.ViewModels;

public partial class AddExpenseViewModel : ObservableObject
{
    private readonly IExpensesService _service;
    private readonly IUserSession _session;
    private readonly ILogger<AddExpenseViewModel> _logger;

    [ObservableProperty] private string category = "Rent";
    [ObservableProperty] private string description = "";
    [ObservableProperty] private string amountText = "";
    [ObservableProperty] private string businessDate = DateTime.Today.ToString("yyyy-MM-dd");
    [ObservableProperty] private string errorMessage = "";
    [ObservableProperty] private bool isSaving;

    public IReadOnlyList<string> CommonCategories { get; } = new[]
    {
        "Rent",
        "Salary",
        "Transport",
        "Utilities",
        "Licenses",
        "Supplies",
        "Repairs",
        "Other",
    };

    public event EventHandler<int>? Saved;

    public AddExpenseViewModel(
        IExpensesService service,
        IUserSession session,
        ILogger<AddExpenseViewModel> logger)
    {
        _service = service;
        _session = session;
        _logger = logger;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = "";

        if (string.IsNullOrWhiteSpace(Category))
        {
            ErrorMessage = "Category is required.";
            return;
        }

        if (!TryParseShillings(AmountText, out long amountCents, out string? err))
        {
            ErrorMessage = err ?? "Amount is not valid.";
            return;
        }

        if (amountCents <= 0)
        {
            ErrorMessage = "Amount must be greater than zero.";
            return;
        }

        try
        {
            IsSaving = true;
            int id = await _service.RecordExpenseAsync(
                Category.Trim(),
                string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
                amountCents,
                BusinessDate,
                _session.RequireUserId);
            Saved?.Invoke(this, id);
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Save expense failed");
            ErrorMessage = $"Could not save: {ex.Message}";
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
            error = "Enter the amount spent.";
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
