using Dapper;
using Microsoft.Data.Sqlite;
using Pos.Application.Cash;
using Pos.Infrastructure.Cash;
using Pos.Infrastructure.Sales;

namespace Pos.Infrastructure.Tests;

public sealed class CashSessionServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly CashSessionService _service;
    private readonly CashSessionQuery _query;
    private readonly SellService _sell;
    private readonly int _userId;
    private readonly int _productId;

    public CashSessionServiceTests()
    {
        _service = new CashSessionService(_db.Factory);
        _query = new CashSessionQuery(_db.Factory);
        _sell = new SellService(_db.Factory);

        _userId = InsertUser();
        _productId = InsertProductWithStock();
    }

    public void Dispose() => _db.Dispose();

    private int InsertUser()
    {
        using var c = _db.Factory.CreateOpen();
        return (int)c.ExecuteScalar<long>(
            "INSERT INTO users(name, role, pin_hash, is_active) VALUES ('Cashier', 'CASHIER', 'PIN_NOT_SET', 1) RETURNING id");
    }

    private int InsertProductWithStock()
    {
        using var c = _db.Factory.CreateOpen();
        int id = (int)c.ExecuteScalar<long>(@"
            INSERT INTO products(uuid, name, base_unit_id, avg_cost_cents, stock_milli, is_active)
            VALUES ($uuid, 'Test Item', 1, 5000, 100000, 1) RETURNING id",
            new { uuid = Guid.NewGuid().ToString("N") });
        c.Execute("INSERT INTO product_prices(product_id, tier_id, price_cents) VALUES ($id, 1, 10000)",
            new { id });
        return id;
    }

    private async Task MakeCashSaleAsync(int sessionId, long qtyMilli, long cashCents)
    {
        var request = new Application.Sales.SaleRequest(
            CustomerId: null,
            PriceTierId: 1,
            BusinessDate: "2026-10-20",
            Lines: new[] { new Application.Sales.SaleLineRequest(_productId, 1, qtyMilli, null, 0, null) },
            Payments: new[] { new Application.Sales.PaymentRequest("CASH", cashCents, null) },
            UserId: _userId,
            CashSessionId: sessionId);

        await _sell.CompleteSaleAsync(request);
    }

    private int CountAudit(string action)
    {
        using var c = _db.Factory.CreateOpen();
        return c.ExecuteScalar<int>(
            "SELECT COUNT(1) FROM audit_log WHERE action = $action", new { action });
    }

    // ===== Open shift =====

    [Fact]
    public async Task OpenShift_CreatesSession_AndAudits()
    {
        int id = await _service.OpenShiftAsync(new OpenShiftRequest(_userId, 10000));

        Assert.True(id > 0);
        Assert.Equal(1, CountAudit("SHIFT_OPENED"));

        OpenCashSession? open = await _query.GetOpenForUserAsync(_userId);
        Assert.NotNull(open);
        Assert.Equal(id, open!.Id);
        Assert.Equal(10000, open.OpeningFloatCents);
        Assert.Equal(10000, open.ExpectedCashCents);
    }

    [Fact]
    public async Task OpenShift_NegativeFloat_Throws()
    {
        await Assert.ThrowsAsync<CashSessionException>(() =>
            _service.OpenShiftAsync(new OpenShiftRequest(_userId, -100)));
    }

    [Fact]
    public async Task OpenShift_SecondOpenShift_Rejected()
    {
        await _service.OpenShiftAsync(new OpenShiftRequest(_userId, 10000));

        var ex = await Assert.ThrowsAsync<CashSessionException>(() =>
            _service.OpenShiftAsync(new OpenShiftRequest(_userId, 20000)));

        Assert.Contains("already have an open shift", ex.Message);
    }

    // ===== Expected cash computation =====

    [Fact]
    public async Task Query_ExpectedCash_TracksSalesAndMovements()
    {
        int sessionId = await _service.OpenShiftAsync(new OpenShiftRequest(_userId, 10000));

        // Cash sale of 1 item at 100 KES
        await MakeCashSaleAsync(sessionId, 1000, 10000);

        // Petty cash in
        await _service.RecordMovementAsync(sessionId, "IN", 5000, "Returned from courier", _userId, "2026-10-20");

        // Bank deposit out
        await _service.RecordMovementAsync(sessionId, "OUT", 8000, "Bank deposit", _userId, "2026-10-20");

        OpenCashSession? open = await _query.GetOpenForUserAsync(_userId);
        Assert.NotNull(open);
        Assert.Equal(10000, open!.CashSalesCents);
        Assert.Equal(5000, open.CashInCents);
        Assert.Equal(8000, open.CashOutCents);

        // Expected = float + sales + in - out = 10000 + 10000 + 5000 - 8000 = 17000
        Assert.Equal(17000, open.ExpectedCashCents);
    }

    [Fact]
    public async Task Query_ExcludesVoidedSales_FromCashSales()
    {
        int sessionId = await _service.OpenShiftAsync(new OpenShiftRequest(_userId, 10000));

        await MakeCashSaleAsync(sessionId, 1000, 10000);

        // Void the sale directly via SQL (void UI is later)
        using (var c = _db.Factory.CreateOpen())
        {
            c.Execute("UPDATE sales SET status = 'VOIDED' WHERE cash_session_id = $id", new { id = sessionId });
        }

        OpenCashSession? open = await _query.GetOpenForUserAsync(_userId);
        Assert.Equal(0, open!.CashSalesCents);
    }

    // ===== Record movement =====

    [Fact]
    public async Task RecordMovement_ValidatesDirection()
    {
        int sessionId = await _service.OpenShiftAsync(new OpenShiftRequest(_userId, 0));

        await Assert.ThrowsAsync<CashSessionException>(() =>
            _service.RecordMovementAsync(sessionId, "SIDEWAYS", 1000, "test", _userId, "2026-10-20"));
    }

    [Fact]
    public async Task RecordMovement_RequiresReason()
    {
        int sessionId = await _service.OpenShiftAsync(new OpenShiftRequest(_userId, 0));

        await Assert.ThrowsAsync<CashSessionException>(() =>
            _service.RecordMovementAsync(sessionId, "OUT", 1000, "", _userId, "2026-10-20"));
    }

    [Fact]
    public async Task RecordMovement_RequiresPositiveAmount()
    {
        int sessionId = await _service.OpenShiftAsync(new OpenShiftRequest(_userId, 0));

        await Assert.ThrowsAsync<CashSessionException>(() =>
            _service.RecordMovementAsync(sessionId, "OUT", 0, "test", _userId, "2026-10-20"));
    }

    [Fact]
    public async Task RecordMovement_OnClosedShift_Rejected()
    {
        int sessionId = await _service.OpenShiftAsync(new OpenShiftRequest(_userId, 0));
        await _service.CloseShiftAsync(sessionId, 0, null);

        await Assert.ThrowsAsync<CashSessionException>(() =>
            _service.RecordMovementAsync(sessionId, "IN", 1000, "test", _userId, "2026-10-20"));
    }

    // ===== Close shift =====

    [Fact]
    public async Task CloseShift_PerfectCount_ZeroVariance()
    {
        int sessionId = await _service.OpenShiftAsync(new OpenShiftRequest(_userId, 10000));
        await MakeCashSaleAsync(sessionId, 1000, 10000);

        // Expected = 10000 + 10000 = 20000. Count exactly.
        var result = await _service.CloseShiftAsync(sessionId, 20000, null);

        Assert.Equal(10000, result.OpeningFloatCents);
        Assert.Equal(10000, result.CashSalesCents);
        Assert.Equal(0, result.CashInCents);
        Assert.Equal(0, result.CashOutCents);
        Assert.Equal(20000, result.ExpectedCashCents);
        Assert.Equal(20000, result.CountedCashCents);
        Assert.Equal(0, result.VarianceCents);
    }

    [Fact]
    public async Task CloseShift_ShortDrawer_NegativeVariance()
    {
        int sessionId = await _service.OpenShiftAsync(new OpenShiftRequest(_userId, 10000));

        // Expected = 10000, counted 9500. Short by 500.
        var result = await _service.CloseShiftAsync(sessionId, 9500, "Torn note, told owner");

        Assert.Equal(10000, result.ExpectedCashCents);
        Assert.Equal(9500, result.CountedCashCents);
        Assert.Equal(-500, result.VarianceCents);
    }

    [Fact]
    public async Task CloseShift_OverDrawer_PositiveVariance()
    {
        int sessionId = await _service.OpenShiftAsync(new OpenShiftRequest(_userId, 10000));

        // Expected = 10000, counted 10500. Over by 500.
        var result = await _service.CloseShiftAsync(sessionId, 10500, null);

        Assert.Equal(500, result.VarianceCents);
    }

    [Fact]
    public async Task CloseShift_WritesAuditEntry()
    {
        int sessionId = await _service.OpenShiftAsync(new OpenShiftRequest(_userId, 10000));
        await _service.CloseShiftAsync(sessionId, 10000, null);

        Assert.Equal(1, CountAudit("SHIFT_CLOSED"));
    }

    [Fact]
    public async Task CloseShift_SecondAttempt_Rejected()
    {
        int sessionId = await _service.OpenShiftAsync(new OpenShiftRequest(_userId, 10000));
        await _service.CloseShiftAsync(sessionId, 10000, null);

        var ex = await Assert.ThrowsAsync<CashSessionException>(() =>
            _service.CloseShiftAsync(sessionId, 10000, null));

        Assert.Contains("already closed", ex.Message);
    }

    [Fact]
    public async Task CloseShift_UnknownSession_Throws()
    {
        await Assert.ThrowsAsync<CashSessionException>(() =>
            _service.CloseShiftAsync(99999, 0, null));
    }

    [Fact]
    public async Task CloseShift_StoresExpectedAndCountedForHistory()
    {
        int sessionId = await _service.OpenShiftAsync(new OpenShiftRequest(_userId, 10000));
        await _service.CloseShiftAsync(sessionId, 9500, null);

        var closed = await _query.ListRecentClosedAsync(limit: 10);

        Assert.Single(closed);
        Assert.Equal(10000, closed[0].ExpectedCashCents);
        Assert.Equal(9500, closed[0].CountedCashCents);
        Assert.Equal(-500, closed[0].VarianceCents);
    }

    // ===== After close =====

    [Fact]
    public async Task AfterClose_UserCanOpenNewShift()
    {
        int firstId = await _service.OpenShiftAsync(new OpenShiftRequest(_userId, 10000));
        await _service.CloseShiftAsync(firstId, 10000, null);

        int secondId = await _service.OpenShiftAsync(new OpenShiftRequest(_userId, 20000));

        Assert.True(secondId > firstId);

        OpenCashSession? open = await _query.GetOpenForUserAsync(_userId);
        Assert.NotNull(open);
        Assert.Equal(secondId, open!.Id);
        Assert.Equal(20000, open.OpeningFloatCents);
    }
}
