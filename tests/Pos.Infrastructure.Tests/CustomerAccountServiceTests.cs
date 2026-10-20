using Dapper;
using Microsoft.Data.Sqlite;
using Pos.Application.Customers;
using Pos.Infrastructure.Customers;

namespace Pos.Infrastructure.Tests;

public sealed class CustomerAccountServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly CustomerRepository _customers;
    private readonly CustomerAccountService _service;
    private readonly int _customerId;

    public CustomerAccountServiceTests()
    {
        _customers = new CustomerRepository(_db.Factory);
        _service = new CustomerAccountService(_db.Factory);

        _customerId = _customers.InsertAsync(new()
        {
            Uuid = Guid.NewGuid().ToString("N"),
            Name = "Test Debtor",
            CreditLimitCents = 100_000,
            IsActive = true,
        }).GetAwaiter().GetResult();
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> CountLedgerEntriesAsync()
    {
        using SqliteConnection c = _db.Factory.CreateOpen();
        return (int)await c.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM customer_ledger");
    }

    private async Task<int> CountAuditEntriesAsync(string action)
    {
        using SqliteConnection c = _db.Factory.CreateOpen();
        return (int)await c.ExecuteScalarAsync<long>(
            "SELECT COUNT(1) FROM audit_log WHERE action = $action",
            new { action });
    }

    [Fact]
    public async Task RecordPayment_CreatesNegativeLedgerEntry()
    {
        var result = await _service.RecordPaymentAsync(new(
            CustomerId: _customerId, AmountCents: 30000, Method: "CASH",
            Reference: null, Notes: null, BusinessDate: "2026-10-20"));

        Assert.True(result.LedgerEntryId > 0);
        Assert.Equal(-30000, result.NewBalanceCents);

        using SqliteConnection c = _db.Factory.CreateOpen();
        long stored = await c.ExecuteScalarAsync<long>(
            "SELECT amount_cents FROM customer_ledger WHERE id = $id",
            new { id = result.LedgerEntryId });
        Assert.Equal(-30000, stored);
    }

    [Fact]
    public async Task RecordPayment_WritesAuditEntry()
    {
        await _service.RecordPaymentAsync(new(
            _customerId, 30000, "CASH", null, null, "2026-10-20"));

        Assert.Equal(1, await CountAuditEntriesAsync("CUSTOMER_PAYMENT"));
    }

    [Fact]
    public async Task RecordPayment_UnknownCustomer_RollsBack()
    {
        var before = await CountLedgerEntriesAsync();

        await Assert.ThrowsAsync<CustomerAccountException>(() =>
            _service.RecordPaymentAsync(new(
                CustomerId: 9999, AmountCents: 1000, Method: "CASH",
                Reference: null, Notes: null, BusinessDate: "2026-10-20")));

        Assert.Equal(before, await CountLedgerEntriesAsync());
    }

    [Theory]
    [InlineData("", "CASH")]
    [InlineData("2026-10-20", "")]
    [InlineData("2026-10-20", "BITCOIN")]
    [InlineData("20-10-2026", "CASH")]
    public async Task RecordPayment_BadInput_Rejected(string date, string method)
    {
        await Assert.ThrowsAsync<CustomerAccountException>(() =>
            _service.RecordPaymentAsync(new(
                _customerId, 1000, method, null, null, date)));
    }

    [Fact]
    public async Task RecordPayment_ZeroAmount_Rejected()
    {
        await Assert.ThrowsAsync<CustomerAccountException>(() =>
            _service.RecordPaymentAsync(new(
                _customerId, 0, "CASH", null, null, "2026-10-20")));
    }

    [Fact]
    public async Task RecordPayment_NegativeAmount_Rejected()
    {
        await Assert.ThrowsAsync<CustomerAccountException>(() =>
            _service.RecordPaymentAsync(new(
                _customerId, -500, "CASH", null, null, "2026-10-20")));
    }

    [Fact]
    public async Task SetOpeningBalance_CreatesEntry_Once()
    {
        var result = await _service.SetOpeningBalanceAsync(new(
            _customerId, 75000, "From notebook", "2026-10-20"));

        Assert.Equal(75000, result.NewBalanceCents);

        // Second attempt must fail.
        await Assert.ThrowsAsync<CustomerAccountException>(() =>
            _service.SetOpeningBalanceAsync(new(
                _customerId, 50000, null, "2026-10-20")));
    }

    [Fact]
    public async Task SetOpeningBalance_Zero_Rejected()
    {
        await Assert.ThrowsAsync<CustomerAccountException>(() =>
            _service.SetOpeningBalanceAsync(new(
                _customerId, 0, null, "2026-10-20")));
    }

    [Fact]
    public async Task SetOpeningBalance_WritesAuditEntry()
    {
        await _service.SetOpeningBalanceAsync(new(
            _customerId, 10000, null, "2026-10-20"));

        Assert.Equal(1, await CountAuditEntriesAsync("CUSTOMER_OPENING_BALANCE"));
    }
}
