using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Expenses;
using Pos.Application.Users;
using Pos.Desktop.Views;

namespace Pos.Desktop.ViewModels;

public partial class ExpensesViewModel : ObservableObject
{
    private readonly IExpensesService _service;
    private readonly IUserSession _session;
    private readonly ILogger<ExpensesViewModel> _logger;

    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private bool isBusy;

    public ObservableCollection<ExpenseListItem> Expenses { get; } = new();

    public Func<AddExpenseView>? CreateAddExpenseDialog { get; set; }

    public ExpensesViewModel(
        IExpensesService service,
        IUserSession session,
        ILogger<ExpensesViewModel> logger)
    {
        _service = service;
        _session = session;
        _logger = logger;
    }

    public async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            IReadOnlyList<ExpenseListItem> items = await _service.ListRecentAsync(limit: 200);

            Expenses.Clear();
            foreach (ExpenseListItem e in items) Expenses.Add(e);

            long total = items.Sum(e => e.AmountCents);
            StatusText = items.Count == 1
                ? $"1 expense · {new Pos.Domain.Money(total)} KES total"
                : $"{items.Count} expenses · {new Pos.Domain.Money(total)} KES total";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Expenses load failed");
            StatusText = "Could not load expenses.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddExpenseAsync()
    {
        if (CreateAddExpenseDialog is null) return;

        AddExpenseView dialog = CreateAddExpenseDialog();
        if (dialog.ShowDialog() == true)
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task DeleteExpenseAsync(ExpenseListItem? item)
    {
        if (item is null) return;

        ConfirmDialog dlg = ConfirmDialog.CreateDanger(
            "Delete this expense?",
            $"Delete '{item.Category}' of {new Pos.Domain.Money(item.AmountCents)} KES " +
            $"recorded on {item.BusinessDate}? This cannot be undone, but is recorded in the audit log.",
            confirmLabel: "Delete expense");

        if (dlg.ShowDialog() != true) return;

        try
        {
            await _service.DeleteAsync(item.Id, _session.RequireUserId);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete expense {Id} failed", item.Id);
            StatusText = $"Could not delete: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();
}
