using Dapper;
using Microsoft.Data.Sqlite;
using Pos.Application.Customers;
using Pos.Domain.Customers;
using Pos.Infrastructure.Customers;

namespace Pos.Infrastructure.Tests;

public sealed class CustomerRepositoryAtomicityTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly CustomerRepository _repo;

    public CustomerRepositoryAtomicityTests() => _repo = new CustomerRepository(_db.Factory);

    public void Dispose() => _db.Dispose();

    private static Customer NewCustomer(string name) => new()
    {
        Uuid = Guid.NewGuid().ToString("N"),
        Name = name,
        IsActive = true,
    };

    private async Task<(int customers, int ledger, int audit)> CountsAsync()
    {
        using SqliteConnection c = _db.Factory.CreateOpen();
        long customers = await c.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM customers");
        long ledger = await c.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM customer_ledger");
        long audit = await c.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM audit_log");
        return ((int)customers, (int)ledger, (int)audit);
    }

    [Fact]
    public async Task InsertWithOpeningBalance_NoBalance_CreatesCustomerOnly()
    {
        int id = await _repo.InsertWithOpeningBalanceAsync(
            NewCustomer("Cash only customer"),
            openingBalanceCents: 0,
            businessDate: "2026-10-20");

        var (customers, ledger, audit) = await CountsAsync();

        Assert.True(id > 0);
        Assert.Equal(1, customers);
        Assert.Equal(0, ledger);
        Assert.Equal(0, audit);
    }

    [Fact]
    public async Task InsertWithOpeningBalance_WithBalance_CreatesBoth()
    {
        int id = await _repo.InsertWithOpeningBalanceAsync(
            NewCustomer("Debtor from notebook"),
            openingBalanceCents: 1_250_000,   // KES 12,500
            businessDate: "2026-10-20");

        var (customers, ledger, audit) = await CountsAsync();

        Assert.True(id > 0);
        Assert.Equal(1, customers);
        Assert.Equal(1, ledger);
        Assert.Equal(1, audit);
        Assert.Equal(1_250_000, await _repo.GetBalanceCentsAsync(id));
    }

    [Fact]
    public async Task InsertWithOpeningBalance_DuplicateUuid_NoPartialWrite()
    {
        string sharedUuid = Guid.NewGuid().ToString("N");

        Customer first = NewCustomer("First");
        first.Uuid = sharedUuid;
        await _repo.InsertWithOpeningBalanceAsync(first, 500_000, "2026-10-20");

        var before = await CountsAsync();

        Customer second = NewCustomer("Should fail");
        second.Uuid = sharedUuid;

        await Assert.ThrowsAsync<SqliteException>(() =>
            _repo.InsertWithOpeningBalanceAsync(second, 300_000, "2026-10-20"));

        var after = await CountsAsync();

        // Atomicity proof: counts unchanged, no orphan ledger entry, no orphan audit.
        Assert.Equal(before, after);
        Assert.Equal(1, after.customers);
        Assert.Equal(1, after.ledger);
        Assert.Equal(1, after.audit);
    }

    [Fact]
    public async Task InsertWithOpeningBalance_NegativeBalance_ThrowsBeforeAnyWrite()
    {
        var before = await CountsAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _repo.InsertWithOpeningBalanceAsync(NewCustomer("Test"), -1, "2026-10-20"));

        var after = await CountsAsync();
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task InsertWithOpeningBalance_BadDate_ThrowsBeforeAnyWrite()
    {
        var before = await CountsAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _repo.InsertWithOpeningBalanceAsync(NewCustomer("Test"), 1000, "20-10-2026"));

        var after = await CountsAsync();
        Assert.Equal(before, after);
    }
}
