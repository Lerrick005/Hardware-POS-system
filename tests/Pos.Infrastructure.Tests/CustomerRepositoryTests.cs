using Microsoft.Data.Sqlite;
using Pos.Domain.Customers;
using Pos.Infrastructure.Customers;

namespace Pos.Infrastructure.Tests;

public sealed class CustomerRepositoryTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly CustomerRepository _repo;
    private readonly CustomerAccountService _account;

    public CustomerRepositoryTests()
    {
        _repo = new CustomerRepository(_db.Factory);
        _account = new CustomerAccountService(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    private static Customer NewCustomer(string name, string? phone = null, long creditLimit = 0) => new()
    {
        Uuid = Guid.NewGuid().ToString("N"),
        Name = name,
        Phone = phone,
        CreditLimitCents = creditLimit,
        IsActive = true,
    };

    [Fact]
    public async Task Insert_ThenGet_ReturnsSameCustomer()
    {
        int id = await _repo.InsertAsync(NewCustomer("Mary Wanjiku", "0722000111"));

        Customer? loaded = await _repo.GetByIdAsync(id);

        Assert.NotNull(loaded);
        Assert.Equal("Mary Wanjiku", loaded!.Name);
        Assert.Equal("0722000111", loaded.Phone);
        Assert.True(loaded.IsActive);
    }

    [Fact]
    public async Task List_ReturnsNewestAlphabetical_AndExcludesArchived()
    {
        int z = await _repo.InsertAsync(NewCustomer("Zawadi"));
        int a = await _repo.InsertAsync(NewCustomer("Achieng"));
        int m = await _repo.InsertAsync(NewCustomer("Mwangi"));
        await _repo.ArchiveAsync(z);

        var list = await _repo.ListAsync();
        var all = await _repo.ListAsync(includeInactive: true);

        Assert.Equal(2, list.Count);
        Assert.Equal("Achieng", list[0].Name);
        Assert.Equal("Mwangi", list[1].Name);
        Assert.Equal(3, all.Count);
        _ = m;
    }

    [Fact]
    public async Task Search_MatchesNameAndPhone_AnyOrder()
    {
        await _repo.InsertAsync(NewCustomer("John Kamau", "0722123456"));
        await _repo.InsertAsync(NewCustomer("Jane Njeri", "0722999888"));
        await _repo.InsertAsync(NewCustomer("Peter Mwangi", "0733000111"));

        var byName = await _repo.SearchAsync("kamau");
        var byNameTwo = await _repo.SearchAsync("john kamau");
        var byPhone = await _repo.SearchAsync("0722");

        Assert.Single(byName);
        Assert.Single(byNameTwo);
        Assert.Equal(2, byPhone.Count);
    }

    [Fact]
    public async Task Search_EmptyTerm_ReturnsFullActiveList()
    {
        await _repo.InsertAsync(NewCustomer("A"));
        await _repo.InsertAsync(NewCustomer("B"));

        var result = await _repo.SearchAsync("");

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetBalance_NoLedger_ReturnsZero()
    {
        int id = await _repo.InsertAsync(NewCustomer("Empty"));

        Assert.Equal(0, await _repo.GetBalanceCentsAsync(id));
    }

    [Fact]
    public async Task GetBalance_SumsLedgerEntries()
    {
        int id = await _repo.InsertAsync(NewCustomer("Borrower"));
        await _account.SetOpeningBalanceAsync(new(
            CustomerId: id, AmountCents: 50000, Notes: "From notebook",
            BusinessDate: "2026-10-20"));
        await _account.RecordPaymentAsync(new(
            CustomerId: id, AmountCents: 20000, Method: "CASH",
            Reference: null, Notes: null, BusinessDate: "2026-10-20"));

        Assert.Equal(30000, await _repo.GetBalanceCentsAsync(id));
    }

    [Fact]
    public async Task OnlyDebtors_SortsByBalanceDescending()
    {
        int small = await _repo.InsertAsync(NewCustomer("Small debtor"));
        int big = await _repo.InsertAsync(NewCustomer("Big debtor"));
        int zero = await _repo.InsertAsync(NewCustomer("Clean"));

        await _account.SetOpeningBalanceAsync(new(small, 5000, null, "2026-10-20"));
        await _account.SetOpeningBalanceAsync(new(big, 80000, null, "2026-10-20"));

        var debtors = await _repo.ListAsync(onlyDebtors: true);

        Assert.Equal(2, debtors.Count);
        Assert.Equal("Big debtor", debtors[0].Name);
        Assert.Equal("Small debtor", debtors[1].Name);
        _ = zero;
    }

    [Fact]
    public async Task ListLedger_ReturnsEntriesInDateOrder()
    {
        int id = await _repo.InsertAsync(NewCustomer("History"));
        await _account.SetOpeningBalanceAsync(new(id, 10000, null, "2026-10-01"));
        await _account.RecordPaymentAsync(new(id, 3000, "CASH", null, null, "2026-10-05"));
        await _account.RecordPaymentAsync(new(id, 2000, "MPESA", "QWE123", null, "2026-10-15"));

        var entries = await _repo.ListLedgerAsync(id);

        Assert.Equal(3, entries.Count);
        Assert.Equal(LedgerEntryType.Opening, entries[0].EntryType);
        Assert.Equal(LedgerEntryType.Payment, entries[1].EntryType);
        Assert.Equal(LedgerEntryType.Payment, entries[2].EntryType);
        Assert.Equal(10000, entries[0].AmountCents);
        Assert.Equal(-3000, entries[1].AmountCents);
        Assert.Equal(-2000, entries[2].AmountCents);
    }
}
