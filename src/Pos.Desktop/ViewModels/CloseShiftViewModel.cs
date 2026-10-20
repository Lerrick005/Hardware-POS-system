using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Cash;
using Pos.Application.Users;
using Pos.Domain;

namespace Pos.Desktop.ViewModels;

public partial class CloseShiftViewModel : ObservableObject
{
    private readonly ICashSessionService _service;
    private readonly ICashSessionQuery _query;
    private readonly IUserSession _session;
    private readonly ILogger<CloseShiftViewModel> _logger;

    [ObservableProperty] private int sessionId;
    [ObservableProperty] private string openingFloatDisplay = "0.00";
    [ObservableProperty] private string cashSalesDisplay = "0.00";
    [ObservableProperty] private string cashInDisplay = "0.00";
    [ObservableProperty] private string cashOutDisplay = "0.00";
    [ObservableProperty] private string expectedCashDisplay = "0.00";
    [ObservableProperty] private string countedCashText = "";
    [ObservableProperty] private string varianceDisplay = "0.00";
    [ObservableProperty] private bool varianceIsNegative;
    [ObservableProperty] private string notes = "";
    [ObservableProperty] private string errorMessage = "";
    [ObservableProperty] private bool isSaving;

    public event EventHandler<CloseShiftResult>? Closed;

    public CloseShiftViewModel(
        ICashSessionService service,
        ICashSessionQuery query,
        IUserSession session,
        ILogger<CloseShiftViewModel> logger)
    {
        _service = service;
        _query = query;
        _session = session;
        _logger = logger;
    }

    public async Task LoadAsync()
    {
        try
        {
            OpenCashSession? open = await _query.GetOpenForUserAsync(_session.RequireUserId);
            if (open is null)
            {
                ErrorMessage = "No open shift to close.";
                return;
            }

            SessionId = open.Id;
            OpeningFloatDisplay = new Money(open.OpeningFloatCents).ToString();
            CashSalesDisplay = new Money(open.CashSalesCents).ToString();
            CashInDisplay = new Money(open.CashInCents).ToString();
            CashOutDisplay = new Money(open.CashOutCents).ToString();
            ExpectedCashDisplay = new Money(open.ExpectedCashCents).ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Close shift load failed");
            ErrorMessage = "Could not load shift details.";
        }
    }

    partial void OnCountedCashTextChanged(string value)
    {
        if (!TryParseShillings(value, out long counted, out _))
        {
            VarianceDisplay = "—";
            VarianceIsNegative = false;
            return;
        }

        if (!decimal.TryParse(ExpectedCashDisplay, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal expectedShillings))
        {
            VarianceDisplay = "—";
            return;
        }

        long expectedCents = (long)decimal.Round(expectedShillings * 100m, 0, MidpointRounding.AwayFromZero);
        long variance = counted - expectedCents;
        VarianceDisplay = new Money(variance).ToString();
        VarianceIsNegative = variance < 0;
    }

    [RelayCommand]
    private async Task CloseAsync()
    {
        ErrorMessage = "";

        if (!TryParseShillings(CountedCashText, out long counted, out string? err))
        {
            ErrorMessage = err ?? "Counted cash is not valid.";
            return;
        }

        try
        {
            IsSaving = true;
            CloseShiftResult result = await _service.CloseShiftAsync(
                SessionId, counted,
                string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim());
            Closed?.Invoke(this, result);
        }
        catch (CashSessionException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Close shift failed");
            ErrorMessage = $"Could not close shift: {ex.Message}";
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
            error = "Enter the cash you counted.";
            return false;
        }

        if (!decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal shillings) || shillings < 0)
        {
            error = "Enter a non-negative amount (e.g. 500, 2,500.00).";
            return false;
        }

        cents = (long)decimal.Round(shillings * 100m, 0, MidpointRounding.AwayFromZero);
        return true;
    }
}
