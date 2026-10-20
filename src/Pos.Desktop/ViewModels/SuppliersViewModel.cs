using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Suppliers;
using Pos.Desktop.Views;

namespace Pos.Desktop.ViewModels;

public partial class SuppliersViewModel : ObservableObject
{
    private readonly ISupplierRepository _repo;
    private readonly ILogger<SuppliersViewModel> _logger;
    private CancellationTokenSource? _searchCts;

    [ObservableProperty]
    private string searchTerm = "";

    [ObservableProperty]
    private string statusText = "";

    [ObservableProperty]
    private bool isBusy;

    public ObservableCollection<SupplierListItem> Suppliers { get; } = new();

    public Func<int?, AddSupplierView>? CreateSupplierDialog { get; set; }

    public SuppliersViewModel(ISupplierRepository repo, ILogger<SuppliersViewModel> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task RefreshAsync()
    {
        await LoadAsync(SearchTerm);
    }

    partial void OnSearchTermChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        CancellationToken token = _searchCts.Token;
        _ = DebouncedLoadAsync(value, token);
    }

    private async Task DebouncedLoadAsync(string term, CancellationToken ct)
    {
        try
        {
            await Task.Delay(200, ct);
            await LoadAsync(term, ct);
        }
        catch (OperationCanceledException) { }
    }

    private async Task LoadAsync(string term, CancellationToken ct = default)
    {
        try
        {
            IsBusy = true;

            IReadOnlyList<SupplierListItem> items = string.IsNullOrWhiteSpace(term)
                ? await _repo.ListAsync(includeInactive: false, ct)
                : await _repo.SearchAsync(term, limit: 500, ct);

            if (ct.IsCancellationRequested) return;

            Suppliers.Clear();
            foreach (SupplierListItem item in items)
            {
                Suppliers.Add(item);
            }

            long totalOwed = items.Sum(x => Math.Max(0, x.BalanceCents));
            StatusText = items.Count == 1
                ? $"1 supplier · owed {new Pos.Domain.Money(totalOwed)}"
                : $"{items.Count} suppliers · owed {new Pos.Domain.Money(totalOwed)}";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Suppliers load failed for term {Term}", term);
            StatusText = "Could not load suppliers.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddSupplierAsync()
    {
        if (CreateSupplierDialog is null) return;

        AddSupplierView dialog = CreateSupplierDialog(null);
        if (dialog.ShowDialog() == true)
        {
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task EditSupplierAsync(SupplierListItem? item)
    {
        if (item is null || CreateSupplierDialog is null) return;

        AddSupplierView dialog = CreateSupplierDialog(item.Id);
        if (dialog.ShowDialog() == true)
        {
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task ArchiveSupplierAsync(SupplierListItem? item)
    {
        if (item is null) return;

        if (item.BalanceCents > 0)
        {
            ConfirmDialog.Create(
                "Cannot remove this supplier",
                $"{item.Name} still has {new Pos.Domain.Money(item.BalanceCents)} in unpaid deliveries. " +
                "Pay all deliveries before removing.",
                confirmLabel: "Understood")
                .ShowDialog();
            return;
        }

        ConfirmDialog dlg = ConfirmDialog.CreateDanger(
            "Remove this supplier?",
            $"{item.Name} will no longer appear in the supplier list. Past delivery records are kept.",
            confirmLabel: "Remove supplier");

        if (dlg.ShowDialog() != true) return;

        try
        {
            await _repo.ArchiveAsync(item.Id);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Archive supplier {Id} failed", item.Id);
            StatusText = $"Could not remove supplier: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task Refresh()
    {
        await RefreshAsync();
    }
}
