using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Catalog;
using Pos.Domain.Catalog;
using Pos.Desktop.Views;

namespace Pos.Desktop.ViewModels;

public partial class CategoriesViewModel : ObservableObject
{
    private readonly ICategoryRepository _repo;
    private readonly ILogger<CategoriesViewModel> _logger;

    [ObservableProperty]
    private string nameInput = "";

    [ObservableProperty]
    private int? editingCategoryId;

    [ObservableProperty]
    private string errorMessage = "";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusText = "";

    public ObservableCollection<CategoryWithCount> Categories { get; } = new();

    public bool IsEditMode => EditingCategoryId is not null;
    public string PrimaryButtonText => IsEditMode ? "Save changes" : "Add category";
    public string CancelEditVisibility => IsEditMode ? "Visible" : "Collapsed";

    public event EventHandler? Changed;

    public CategoriesViewModel(ICategoryRepository repo, ILogger<CategoriesViewModel> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            IReadOnlyList<CategoryWithCount> items = await _repo.ListWithCountsAsync(includeInactive: false);
            Categories.Clear();
            foreach (CategoryWithCount item in items)
            {
                Categories.Add(item);
            }

            int totalProducts = items.Sum(x => x.ProductCount);
            StatusText = items.Count == 1
                ? $"1 category · {totalProducts} products"
                : $"{items.Count} categories · {totalProducts} products";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Categories load failed");
            StatusText = "Could not load categories.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddOrSaveAsync()
    {
        ErrorMessage = "";

        string trimmed = (NameInput ?? "").Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            ErrorMessage = "Enter a category name.";
            return;
        }

        // Duplicate check (case-insensitive) against the current list.
        CategoryWithCount? duplicate = Categories.FirstOrDefault(
            c => !string.Equals(c.Name, trimmed, StringComparison.OrdinalIgnoreCase) == false
                 && c.Id != (EditingCategoryId ?? 0));
        if (duplicate is not null)
        {
            ErrorMessage = $"A category named '{trimmed}' already exists.";
            return;
        }

        try
        {
            IsBusy = true;

            if (IsEditMode)
            {
                Category? existing = await _repo.GetByIdAsync(EditingCategoryId!.Value);
                if (existing is null)
                {
                    ErrorMessage = "This category no longer exists.";
                    return;
                }

                existing.Name = trimmed;
                await _repo.UpdateAsync(existing);
            }
            else
            {
                await _repo.InsertAsync(new Category { Name = trimmed, IsActive = true });
            }

            NameInput = "";
            EditingCategoryId = null;
            OnPropertyChanged(nameof(IsEditMode));
            OnPropertyChanged(nameof(PrimaryButtonText));
            OnPropertyChanged(nameof(CancelEditVisibility));

            await LoadAsync();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Save category failed for {Name}", trimmed);
            ErrorMessage = $"Could not save: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void StartEdit(CategoryWithCount? item)
    {
        if (item is null)
        {
            return;
        }

        EditingCategoryId = item.Id;
        NameInput = item.Name;
        ErrorMessage = "";

        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(PrimaryButtonText));
        OnPropertyChanged(nameof(CancelEditVisibility));
    }

    [RelayCommand]
    private void CancelEdit()
    {
        EditingCategoryId = null;
        NameInput = "";
        ErrorMessage = "";

        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(PrimaryButtonText));
        OnPropertyChanged(nameof(CancelEditVisibility));
    }

    [RelayCommand]
    private async Task ArchiveCategoryAsync(CategoryWithCount? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.ProductCount > 0)
        {
            ConfirmDialog.Create(
                "Cannot archive this category",
                $"{item.ProductCount} {(item.ProductCount == 1 ? "product is" : "products are")} " +
                $"still assigned to '{item.Name}'. Move them to another category or remove them first.",
                confirmLabel: "Understood")
                .ShowDialog();
            return;
        }

        ConfirmDialog dlg = ConfirmDialog.CreateDanger(
            "Archive this category?",
            $"'{item.Name}' will no longer appear in product forms. You can restore it later if needed.",
            confirmLabel: "Archive category");

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await _repo.ArchiveAsync(item.Id);
            await LoadAsync();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Archive category {Id} failed", item.Id);
            ErrorMessage = $"Could not archive: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task Refresh()
    {
        await LoadAsync();
    }
}
