using Dapper;
using Microsoft.Data.Sqlite;
using Pos.Application.Stock;
using Pos.Infrastructure.Stock;

namespace Pos.Infrastructure.Tests;

public sealed class StockAdjustmentServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly StockAdjustmentService _service;
    private readonly int _userId;
    private readonly int _productId;

    public StockAdjustmentServiceTests()
    {
        _service = new StockAdjustmentService(_db.Factory);

        using var c = _db.Factory.CreateOpen();
        _userId = (int)c.ExecuteScalar<long>(
            "INSERT INTO users(name, role, pin_hash, is_active) VALUES ('Admin', 'ADMIN', 'PIN_NOT_SET', 1) RETURNING id");

        _productId = (int)c.ExecuteScalar<long>(@"
            INSERT INTO products(uuid, name, base_unit_id, stock_milli, is_active)
            VALUES ($uuid, 'Cement 50kg', 1, 50000, 1) RETURNING id",
            new { uuid = Guid.NewGuid().ToString("N") });
    }

    public void Dispose() => _db.Dispose();

    private long CurrentStock()
    {
        using var c = _db.Factory.CreateOpen();
        return c.ExecuteScalar<long>("SELECT stock_milli FROM products WHERE id = $id", new { id = _productId });
    }

    private string? LastMovementType()
    {
        using var c = _db.Factory.CreateOpen();
        return c.ExecuteScalar<string>(
            "SELECT movement_type FROM stock_movements WHERE product_id = $id ORDER BY id DESC LIMIT 1",
            new { id = _productId });
    }

    private string? LastMovementReason()
    {
        using var c = _db.Factory.CreateOpen();
        return c.ExecuteScalar<string>(
            "SELECT reason FROM stock_movements WHERE product_id = $id ORDER BY id DESC LIMIT 1",
            new { id = _productId });
    }

    private StockAdjustmentRequest Req(string reason, long delta, string notes = "test note") => new(
        ProductId: _productId,
        ReasonCategory: reason,
        DeltaMilli: delta,
        Notes: notes,
        UserId: _userId,
        BusinessDate: "2026-10-21");

    [Fact]
    public async Task Damaged_DecreasesStock_WithDamageMovementType()
    {
        var result = await _service.AdjustAsync(Req(AdjustmentReasons.Damaged, -3000));

        Assert.Equal(50000, result.PreviousStockMilli);
        Assert.Equal(47000, result.NewStockMilli);
        Assert.Equal("DAMAGE", result.MovementType);
        Assert.Equal(47000, CurrentStock());
        Assert.Equal("DAMAGE", LastMovementType());
    }

    [Fact]
    public async Task Lost_DecreasesStock_WithAdjustLossMovementType()
    {
        await _service.AdjustAsync(Req(AdjustmentReasons.Lost, -5000));

        Assert.Equal(45000, CurrentStock());
        Assert.Equal("ADJUST_LOSS", LastMovementType());
    }

    [Fact]
    public async Task Found_IncreasesStock_WithAdjustGainMovementType()
    {
        await _service.AdjustAsync(Req(AdjustmentReasons.Found, 2000));

        Assert.Equal(52000, CurrentStock());
        Assert.Equal("ADJUST_GAIN", LastMovementType());
    }

    [Fact]
    public async Task Counted_WithPositiveDelta_UsesAdjustGain()
    {
        // Owner counted 53 but system said 50
        await _service.AdjustAsync(Req(AdjustmentReasons.Counted, 3000, "Stock take 2026-10-21"));

        Assert.Equal(53000, CurrentStock());
        Assert.Equal("ADJUST_GAIN", LastMovementType());
    }

    [Fact]
    public async Task Counted_WithNegativeDelta_UsesAdjustLoss()
    {
        // Owner counted 47 but system said 50
        await _service.AdjustAsync(Req(AdjustmentReasons.Counted, -3000, "Stock take 2026-10-21"));

        Assert.Equal(47000, CurrentStock());
        Assert.Equal("ADJUST_LOSS", LastMovementType());
    }

    [Fact]
    public async Task WritesAuditLog()
    {
        await _service.AdjustAsync(Req(AdjustmentReasons.Damaged, -3000, "Bag torn in storage"));

        using var c = _db.Factory.CreateOpen();
        int auditCount = c.ExecuteScalar<int>(
            "SELECT COUNT(1) FROM audit_log WHERE action = 'STOCK_ADJUSTED'");
        Assert.Equal(1, auditCount);
    }

    [Fact]
    public async Task NotesBecomeMovementReason()
    {
        await _service.AdjustAsync(Req(AdjustmentReasons.Found, 1000, "Discovered behind shelf"));

        Assert.Equal("Discovered behind shelf", LastMovementReason());
    }

    [Fact]
    public async Task ZeroDelta_Rejected()
    {
        await Assert.ThrowsAsync<StockAdjustmentException>(() =>
            _service.AdjustAsync(Req(AdjustmentReasons.Damaged, 0)));
    }

    [Fact]
    public async Task DamagedWithPositiveDelta_Rejected()
    {
        await Assert.ThrowsAsync<StockAdjustmentException>(() =>
            _service.AdjustAsync(Req(AdjustmentReasons.Damaged, 1000)));
    }

    [Fact]
    public async Task FoundWithNegativeDelta_Rejected()
    {
        await Assert.ThrowsAsync<StockAdjustmentException>(() =>
            _service.AdjustAsync(Req(AdjustmentReasons.Found, -1000)));
    }

    [Fact]
    public async Task EmptyNotes_Rejected()
    {
        await Assert.ThrowsAsync<StockAdjustmentException>(() =>
            _service.AdjustAsync(Req(AdjustmentReasons.Damaged, -1000, notes: "   ")));
    }

    [Fact]
    public async Task UnknownReason_Rejected()
    {
        await Assert.ThrowsAsync<StockAdjustmentException>(() =>
            _service.AdjustAsync(Req("MAGIC", -1000)));
    }

    [Fact]
    public async Task UnknownProduct_Rejected()
    {
        var req = new StockAdjustmentRequest(99999, AdjustmentReasons.Damaged, -1000, "test", _userId, "2026-10-21");
        await Assert.ThrowsAsync<StockAdjustmentException>(() => _service.AdjustAsync(req));
    }

    [Fact]
    public async Task WouldGoNegative_Rejected()
    {
        // Current stock is 50000 (50). Try to remove 60.
        var ex = await Assert.ThrowsAsync<StockAdjustmentException>(() =>
            _service.AdjustAsync(Req(AdjustmentReasons.Damaged, -60000)));
        Assert.Contains("negative", ex.Message);
        Assert.Equal(50000, CurrentStock());   // unchanged
    }

    [Fact]
    public async Task ExactZeroAllowed_WhenCountingToZero()
    {
        // Owner says "I counted zero but system said 50" — remove all.
        await _service.AdjustAsync(Req(AdjustmentReasons.Counted, -50000, "All gone"));

        Assert.Equal(0, CurrentStock());
    }
}
