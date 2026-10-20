namespace Pos.Application.Customers;

/// <summary>Payment received from a customer against their account.</summary>
public sealed record RecordPaymentRequest(
    int CustomerId,
    long AmountCents,
    string Method,          // CASH, MPESA, CARD, BANK, OTHER
    string? Reference,
    string? Notes,
    string BusinessDate,
    int? UserId = null,
    int? CashSessionId = null);

public sealed record RecordPaymentResult(
    int LedgerEntryId,
    long NewBalanceCents);

/// <summary>Old balance carried into the system when a customer is migrated from a notebook.</summary>
public sealed record SetOpeningBalanceRequest(
    int CustomerId,
    long AmountCents,
    string? Notes,
    string BusinessDate,
    int? UserId = null);

public sealed record SetOpeningBalanceResult(
    int LedgerEntryId,
    long NewBalanceCents);

public sealed class CustomerAccountException : Exception
{
    public CustomerAccountException(string message) : base(message) { }
}

public interface ICustomerAccountService
{
    /// <summary>
    /// Records a payment against a customer's account. Creates a negative
    /// customer_ledger entry. Runs in one transaction with an audit entry.
    /// </summary>
    Task<RecordPaymentResult> RecordPaymentAsync(
        RecordPaymentRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Sets the customer's opening balance (their existing debt carried from the
    /// notebook). Only allowed once per customer; a second attempt throws.
    /// </summary>
    Task<SetOpeningBalanceResult> SetOpeningBalanceAsync(
        SetOpeningBalanceRequest request,
        CancellationToken ct = default);
}
