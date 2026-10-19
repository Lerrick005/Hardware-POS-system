using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Stock;

namespace Pos.Desktop.ViewModels;

public partial class ReceiveGoodsViewModel : ObservableObject
{
    private readonly IReceiveGoodsQuery _query;
    private readonly ILogger<ReceiveGoodsViewModel> _logger;

    [ObservableProperty]
    private string statusText = "";

    [ObservableProperty]
    private bool isBusy;

    public ObservableCollection<GoodsReceiptSummary> Receipts { get; } = new();

    /// <summary>Set by App composition. Opens the compose dialog and returns true if a receipt was saved.</summary>
    public Func<bool>? CreateNewReceiptDialog { get; set; }

    public ReceiveGoodsViewModel(IReceiveGoodsQuery query, ILogger<ReceiveGoodsViewModel> logger)
    {
        _query = query;
        _logger = logger;
    }

    public async Task RefreshAsync()
    {
        try
        {
            IsBusy = true;
            IReadOnlyList<GoodsReceiptSummary> items = await _query.ListRecentAsync(limit: 100);
            Receipts.Clear();
            foreach (GoodsReceiptSummary item in items)
            {
                Receipts.Add(item);
            }
            StatusText = items.Count == 1 ? "1 receipt" : $"{items.Count} receipts";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Receive goods list failed");
            StatusText = "Could not load receipts.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task Refresh()
    {
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task NewReceiptAsync()
    {
        if (CreateNewReceiptDialog is null)
        {
            return;
        }

        bool saved = CreateNewReceiptDialog();
        if (saved)
        {
            await RefreshAsync();
        }
    }
}
