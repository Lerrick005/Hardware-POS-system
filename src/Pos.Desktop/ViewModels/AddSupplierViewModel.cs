using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pos.Application.Suppliers;
using Pos.Domain.Suppliers;

namespace Pos.Desktop.ViewModels;

public partial class AddSupplierViewModel : ObservableObject
{
    private readonly ISupplierRepository _repo;
    private readonly ILogger<AddSupplierViewModel> _logger;

    [ObservableProperty]
    private int? supplierId;

    [ObservableProperty]
    private string name = "";

    [ObservableProperty]
    private string phone = "";

    [ObservableProperty]
    private string email = "";

    [ObservableProperty]
    private string notes = "";

    [ObservableProperty]
    private string errorMessage = "";

    [ObservableProperty]
    private bool isSaving;

    public bool IsEditMode => SupplierId is not null;
    public string WindowTitle => IsEditMode ? "Edit supplier" : "Add supplier";
    public string SaveButtonText => IsEditMode ? "Save changes" : "Save supplier";

    public event EventHandler<int>? Saved;

    public AddSupplierViewModel(ISupplierRepository repo, ILogger<AddSupplierViewModel> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task LoadAsync()
    {
        if (SupplierId is null) return;

        Supplier? existing = await _repo.GetByIdAsync(SupplierId.Value);
        if (existing is null)
        {
            ErrorMessage = "This supplier no longer exists.";
            return;
        }

        Name = existing.Name;
        Phone = existing.Phone ?? "";
        Email = existing.Email ?? "";
        Notes = existing.Notes ?? "";

        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(SaveButtonText));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = "";

        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Supplier name is required.";
            return;
        }

        try
        {
            IsSaving = true;

            if (IsEditMode)
            {
                Supplier? existing = await _repo.GetByIdAsync(SupplierId!.Value);
                if (existing is null)
                {
                    ErrorMessage = "This supplier no longer exists.";
                    return;
                }

                existing.Name = Name.Trim();
                existing.Phone = string.IsNullOrWhiteSpace(Phone) ? null : Phone.Trim();
                existing.Email = string.IsNullOrWhiteSpace(Email) ? null : Email.Trim();
                existing.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();

                await _repo.UpdateAsync(existing);
                Saved?.Invoke(this, existing.Id);
            }
            else
            {
                Supplier supplier = new()
                {
                    Uuid = Guid.NewGuid().ToString("N"),
                    Name = Name.Trim(),
                    Phone = string.IsNullOrWhiteSpace(Phone) ? null : Phone.Trim(),
                    Email = string.IsNullOrWhiteSpace(Email) ? null : Email.Trim(),
                    Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
                    IsActive = true,
                };

                int id = await _repo.InsertAsync(supplier);
                Saved?.Invoke(this, id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Save supplier failed for {Name}", Name);
            ErrorMessage = $"Could not save: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }
}
