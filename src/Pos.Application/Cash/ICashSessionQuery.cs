namespace Pos.Application.Cash;

public interface ICashSessionQuery
{
    /// <summary>
    /// The open session for a user, or null if none. Used after login to
    /// decide whether to prompt for shift open.
    /// </summary>
    Task<OpenCashSession?> GetOpenForUserAsync(int userId, CancellationToken ct = default);

    /// <summary>Session detail by id, regardless of open/closed state.</summary>
    Task<OpenCashSession?> GetByIdAsync(int sessionId, CancellationToken ct = default);

    /// <summary>All cash movements for a session, newest first.</summary>
    Task<IReadOnlyList<CashMovementRow>> ListMovementsAsync(int sessionId, CancellationToken ct = default);

    /// <summary>Most recent closed sessions, for the shop owner to review history.</summary>
    Task<IReadOnlyList<ClosedShiftSummary>> ListRecentClosedAsync(int limit = 30, CancellationToken ct = default);
}

public sealed class ClosedShiftSummary
{
    public int Id { get; set; }
    public string? UserName { get; set; }
    public string OpenedAt { get; set; } = "";
    public string? ClosedAt { get; set; }
    public long OpeningFloatCents { get; set; }
    public long ExpectedCashCents { get; set; }
    public long CountedCashCents { get; set; }
    public long VarianceCents => CountedCashCents - ExpectedCashCents;
}
