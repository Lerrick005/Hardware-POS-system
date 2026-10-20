using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Customers;

namespace Pos.Desktop.ViewModels;

public partial class CustomersViewModel : ObservableObject
{
    private readonly ICustomerRepository _repo;
    private readonly ILogger<CustomersViewModel> _logger;
    private CancellationTokenSource? _searchCts;

    [ObservableProperty]
    private string searchTerm = "";

    [ObservableProperty]
    private string statusText = "";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool onlyDebtors;

    public ObservableCollection<CustomerListItem> Customers { get; } = new();

    public CustomersViewModel(ICustomerRepository repo, ILogger<CustomersViewModel> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task RefreshAsync()
    {
        await LoadAsync(SearchTerm, OnlyDebtors);
    }

    partial void OnSearchTermChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        CancellationToken token = _searchCts.Token;
        _ = DebouncedLoadAsync(value, OnlyDebtors, token);
    }

    partial void OnOnlyDebtorsChanged(bool value)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        CancellationToken token = _searchCts.Token;
        _ = DebouncedLoadAsync(SearchTerm, value, token);
    }

    private async Task DebouncedLoadAsync(string term, bool debtorsOnly, CancellationToken ct)
    {
        try
        {
            await Task.Delay(200, ct);
            await LoadAsync(term, debtorsOnly, ct);
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer keystroke or toggle
        }
    }

    private async Task LoadAsync(string term, bool debtorsOnly, CancellationToken ct = default)
    {
        try
        {
            IsBusy = true;

            IReadOnlyList<CustomerListItem> items;
            if (debtorsOnly && string.IsNullOrWhiteSpace(term))
            {
                items = await _repo.ListAsync(includeInactive: false, onlyDebtors: true, ct);
            }
            else
            {
                items = await _repo.SearchAsync(term, limit: 500, ct);
                if (debtorsOnly)
                {
                    items = items
                        .Where(x => x.BalanceCents > 0)
                        .OrderByDescending(x => x.BalanceCents)
                        .ToList();
                }
            }

            if (ct.IsCancellationRequested)
            {
                return;
            }

            Customers.Clear();
            foreach (CustomerListItem item in items)
            {
                Customers.Add(item);
            }

            long totalOwed = items.Sum(x => Math.Max(0, x.BalanceCents));
            string suffix = debtorsOnly ? " with balance" : "";
            StatusText = items.Count == 1
                ? $"1 customer{suffix} · owed {new Pos.Domain.Money(totalOwed)}"
                : $"{items.Count} customers{suffix} · owed {new Pos.Domain.Money(totalOwed)}";
        }
        catch (OperationCanceledException)
        {
            // superseded
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Customers load failed for term {Term}", term);
            StatusText = "Could not load customers.";
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
}
