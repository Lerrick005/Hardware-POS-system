namespace Pos.Application.Cash;

/// <summary>
/// A currently open cash session. Returned by query, updated by service.
/// Class, not record — Dapper materialization (ADR-0011).
/// </summary>
public sealed class OpenCashSession
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string? UserName { get; set; }
    public string OpenedAt { get; set; } = "";
    public long OpeningFloatCents { get; set; }

    /// <summary>Cash received from sales during this session (CASH payments only).</summary>
    public long CashSalesCents { get; set; }

    /// <summary>Cash movements IN during this session (petty cash returned, etc).</summary>
    public long CashInCents { get; set; }

    /// <summary>Cash movements OUT during this session (petty cash, bank deposit, void refunds).</summary>
    public long CashOutCents { get; set; }

    /// <summary>
    /// Expected cash in drawer = opening_float + cash_sales + cash_in - cash_out.
    /// Computed, not stored (stored value captured at close time for audit).
    /// </summary>
    public long ExpectedCashCents =>
        OpeningFloatCents + CashSalesCents + CashInCents - CashOutCents;
}

public sealed class CashMovementRow
{
    public int Id { get; set; }
    public string Direction { get; set; } = "";  // IN or OUT
    public long AmountCents { get; set; }
    public string Reason { get; set; } = "";
    public string? UserName { get; set; }
    public string CreatedAt { get; set; } = "";
    public string WhenDisplay => CreatedAt.Replace("T", " ").Replace("Z", "");
}

public sealed record OpenShiftRequest(int UserId, long OpeningFloatCents);

public sealed record CloseShiftResult(
    int SessionId,
    long OpeningFloatCents,
    long CashSalesCents,
    long CashInCents,
    long CashOutCents,
    long ExpectedCashCents,
    long CountedCashCents,
    long VarianceCents);

public sealed class CashSessionException : Exception
{
    public CashSessionException(string message) : base(message) { }
}

public interface ICashSessionService
{
    /// <summary>
    /// Opens a new cash session for the user. Each user can have at most one
    /// open session at a time (enforced by unique index). Opening float is
    /// the physical cash placed in the drawer at start of shift.
    /// </summary>
    Task<int> OpenShiftAsync(OpenShiftRequest request, CancellationToken ct = default);

    /// <summary>
    /// Closes the current session. Computes expected cash from movements,
    /// stores the variance (counted - expected) for audit. Variance can be
    /// positive (over), negative (short), or zero (perfect).
    ///
    /// Refuses if no open session exists.
    /// </summary>
    Task<CloseShiftResult> CloseShiftAsync(int sessionId, long countedCashCents, string? notes, CancellationToken ct = default);

    /// <summary>
    /// Records a cash movement (petty cash in/out, bank deposit, void refund).
    /// Uses the current open session. Refuses if no open session.
    /// </summary>
    Task<int> RecordMovementAsync(
        int sessionId,
        string direction,        // "IN" or "OUT"
        long amountCents,
        string reason,
        int userId,
        string businessDate,
        CancellationToken ct = default);
}
