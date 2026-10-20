using Pos.Domain.Customers;

namespace Pos.Application.Customers;

/// <summary>
/// One row of the Customers list. Shaped for the grid — includes the joined
/// price tier name and the current balance so the list can sort by either
/// without N+1 queries.
/// </summary>
public sealed record CustomerListItem(
    int Id,
    string Name,
    string? Phone,
    int? PriceTierId,
    string? PriceTierName,
    long CreditLimitCents,
    long BalanceCents,
    bool IsActive);

public interface ICustomerRepository
{
    /// <summary>
    /// All customers with their current balance. When <paramref name="onlyDebtors"/>
    /// is true, only customers whose balance is strictly positive are returned
    /// (used by the debtors summary).
    /// </summary>
    Task<IReadOnlyList<CustomerListItem>> ListAsync(
        bool includeInactive = false,
        bool onlyDebtors = false,
        CancellationToken ct = default);

    /// <summary>Search by name or phone, includes balance.</summary>
    Task<IReadOnlyList<CustomerListItem>> SearchAsync(
        string term,
        int limit = 100,
        CancellationToken ct = default);

    /// <summary>Full customer entity for edit screens.</summary>
    Task<Customer?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>Returns the new customer's id.</summary>
    Task<int> InsertAsync(Customer customer, CancellationToken ct = default);

    Task UpdateAsync(Customer customer, CancellationToken ct = default);

    Task ArchiveAsync(int id, CancellationToken ct = default);

    /// <summary>Current balance in cents. Positive = customer owes the shop.</summary>
    Task<long> GetBalanceCentsAsync(int customerId, CancellationToken ct = default);

    /// <summary>
    /// Full ledger history for the statement, oldest first so the running
    /// balance can be computed in order.
    /// </summary>
    Task<IReadOnlyList<CustomerLedgerEntry>> ListLedgerAsync(
        int customerId,
        CancellationToken ct = default);
}
