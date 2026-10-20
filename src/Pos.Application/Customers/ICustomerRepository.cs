using Pos.Domain.Customers;

namespace Pos.Application.Customers;

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

    /// <summary>
    /// Inserts a customer and, when <paramref name="openingBalanceCents"/> is
    /// greater than zero, writes their opening balance (their old debt carried
    /// from the notebook) in the same transaction. If either insert fails,
    /// neither is persisted.
    /// </summary>
    Task<int> InsertWithOpeningBalanceAsync(
        Customer customer,
        long openingBalanceCents,
        string businessDate,
        int? userId = null,
        CancellationToken ct = default);

    Task UpdateAsync(Customer customer, CancellationToken ct = default);

    Task ArchiveAsync(int id, CancellationToken ct = default);

    Task<long> GetBalanceCentsAsync(int customerId, CancellationToken ct = default);

    Task<IReadOnlyList<CustomerLedgerEntry>> ListLedgerAsync(
        int customerId,
        CancellationToken ct = default);
}
