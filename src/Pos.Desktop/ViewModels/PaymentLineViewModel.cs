using CommunityToolkit.Mvvm.ComponentModel;

namespace Pos.Desktop.ViewModels;

public partial class PaymentLineViewModel : ObservableObject
{
    [ObservableProperty]
    private string methodCode = "CASH";

    [ObservableProperty]
    private string methodDisplay = "Cash";

    [ObservableProperty]
    private string amountText = "";

    [ObservableProperty]
    private string reference = "";

    [ObservableProperty]
    private string validationError = "";

    public event EventHandler? Changed;

    public long AmountCents { get; private set; }

    public bool ReferenceRequired => MethodCode is "MPESA" or "CARD" or "BANK";

    public bool IsValid =>
        AmountCents > 0 &&
        (!ReferenceRequired || !string.IsNullOrWhiteSpace(Reference)) &&
        string.IsNullOrEmpty(ValidationError);

    public IReadOnlyList<string> AvailableMethods { get; } =
        new[] { "CASH", "MPESA", "CARD", "BANK", "CREDIT", "OTHER" };

    partial void OnAmountTextChanged(string value) => Recalculate();
    partial void OnMethodCodeChanged(string value)
    {
        MethodDisplay = MethodCode switch
        {
            "CASH" => "Cash",
            "MPESA" => "M-Pesa",
            "CARD" => "Card",
            "BANK" => "Bank",
            "CREDIT" => "Credit",
            _ => "Other",
        };
        OnPropertyChanged(nameof(ReferenceRequired));
        Recalculate();
    }
    partial void OnReferenceChanged(string value) => Recalculate();

    private void Recalculate()
    {
        ValidationError = "";

        if (string.IsNullOrWhiteSpace(AmountText))
        {
            AmountCents = 0;
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!decimal.TryParse(AmountText.Trim(), System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out decimal shillings) || shillings <= 0)
        {
            ValidationError = "Amount must be greater than zero.";
            AmountCents = 0;
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        AmountCents = (long)decimal.Round(shillings * 100m, 0, MidpointRounding.AwayFromZero);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
