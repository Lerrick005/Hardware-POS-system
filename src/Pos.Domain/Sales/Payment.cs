namespace Pos.Domain.Sales;

public enum PaymentMethod
{
    Cash,
    Mpesa,
    Card,
    Bank,
    Credit,
    Other,
}

/// <summary>
/// One payment against a sale. A single sale may have multiple payments
/// (split payment: part cash, part M-Pesa, part on credit). The sum of
/// all payments must equal the sale total — enforced by the sale service,
/// not here.
/// </summary>
public sealed class Payment
{
    public int Id { get; set; }
    public int SaleId { get; set; }
    public PaymentMethod Method { get; set; }

    /// <summary>Payment amount in cents. Always positive — sign is contextual.</summary>
    public long AmountCents { get; set; }

    /// <summary>M-Pesa code, card reference, bank reference, etc.</summary>
    public string? Reference { get; set; }

    public string BusinessDate { get; set; } = "";
    public string? CreatedAt { get; set; }

    public Money Amount => new(AmountCents);

    /// <summary>DB-format method string. Matches customer_ledger.method and payments.method.</summary>
    public string MethodCode => Method switch
    {
        PaymentMethod.Cash => "CASH",
        PaymentMethod.Mpesa => "MPESA",
        PaymentMethod.Card => "CARD",
        PaymentMethod.Bank => "BANK",
        PaymentMethod.Credit => "CREDIT",
        PaymentMethod.Other => "OTHER",
        _ => "OTHER",
    };

    /// <summary>Shop-language label for UI display.</summary>
    public string MethodDisplay => Method switch
    {
        PaymentMethod.Cash => "Cash",
        PaymentMethod.Mpesa => "M-Pesa",
        PaymentMethod.Card => "Card",
        PaymentMethod.Bank => "Bank",
        PaymentMethod.Credit => "Credit",
        PaymentMethod.Other => "Other",
        _ => "Other",
    };
}
