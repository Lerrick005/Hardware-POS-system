using Pos.Domain.Customers;

namespace Pos.Application.Customers;

/// <summary>
/// One row of the Customers list. Shaped for the grid — includes the joined
/// price tier name and the current balance so the list can sort by either
/// without N+1 queries.
///
/// A class with settable properties (not a positional record) so Dapper can
/// materialize it directly from raw SQLite types (Int64 → int/bool, NULL → nullable).
/// </summary>
public sealed class CustomerListItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public int? PriceTierId { get; set; }
    public string? PriceTierName { get; set; }
    public long CreditLimitCents { get; set; }
    public long BalanceCents { get; set; }
    public bool IsActive { get; set; }
}

public interface ICustomerRepository
{
    Task<IReadOnlyList<CustomerListItem>> ListAsync(
        bool includeInactive = false,
        bool onlyDebtors = false,
        CancellationToken ct = default);

    Task<IReadOnlyList<CustomerListItem>> SearchAsync(
        string term,
        int limit = 100,
        CancellationToken ct = default);

    Task<Customer?> GetByIdAsync(int id, CancellationToken ct = default);

    Task<int> InsertAsync(Customer customer, CancellationToken ct = default);

    Task UpdateAsync(Customer customer, CancellationToken ct = default);

    Task ArchiveAsync(int id, CancellationToken ct = default);

    Task<long> GetBalanceCentsAsync(int customerId, CancellationToken ct = default);

    Task<IReadOnlyList<CustomerLedgerEntry>> ListLedgerAsync(
        int customerId,
        CancellationToken ct = default);
}
