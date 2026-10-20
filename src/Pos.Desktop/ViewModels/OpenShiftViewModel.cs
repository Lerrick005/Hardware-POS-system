using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Cash;
using Pos.Application.Users;

namespace Pos.Desktop.ViewModels;

public partial class OpenShiftViewModel : ObservableObject
{
    private readonly ICashSessionService _service;
    private readonly IUserSession _session;
    private readonly ILogger<OpenShiftViewModel> _logger;

    [ObservableProperty] private string openingFloatText = "0";
    [ObservableProperty] private string errorMessage = "";
    [ObservableProperty] private bool isSaving;

    public event EventHandler<int>? Opened;

    public OpenShiftViewModel(
        ICashSessionService service,
        IUserSession session,
        ILogger<OpenShiftViewModel> logger)
    {
        _service = service;
        _session = session;
        _logger = logger;
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        ErrorMessage = "";

        if (!TryParseShillings(OpeningFloatText, out long floatCents, out string? err))
        {
            ErrorMessage = err ?? "Opening float is not valid.";
            return;
        }

        try
        {
            IsSaving = true;
            int id = await _service.OpenShiftAsync(new OpenShiftRequest(_session.RequireUserId, floatCents));
            Opened?.Invoke(this, id);
        }
        catch (CashSessionException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open shift failed");
            ErrorMessage = $"Could not open shift: {ex.Message}";
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
        if (trimmed.Length == 0) return true; // empty = 0 float

        if (!decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal shillings) || shillings < 0)
        {
            error = "Enter a non-negative amount (e.g. 0, 500, 2,500.00).";
            return false;
        }

        cents = (long)decimal.Round(shillings * 100m, 0, MidpointRounding.AwayFromZero);
        return true;
    }
}
