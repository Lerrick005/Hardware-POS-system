using Dapper;
using Microsoft.Data.Sqlite;
using Pos.Application.Cash;
using Pos.Infrastructure.Data;

namespace Pos.Infrastructure.Cash;

public sealed class CashSessionService : ICashSessionService
{
    private readonly DbConnectionFactory _factory;

    public CashSessionService(DbConnectionFactory factory) => _factory = factory;

    public async Task<int> OpenShiftAsync(OpenShiftRequest request, CancellationToken ct = default)
    {
        if (request.UserId <= 0)
            throw new CashSessionException("User id is required to open a shift.");

        if (request.OpeningFloatCents < 0)
            throw new CashSessionException("Opening float cannot be negative.");

        using SqliteConnection connection = _factory.CreateOpen();
        using SqliteTransaction tx = connection.BeginTransaction();

        try
        {
            // Pre-check for a friendly message rather than relying on the
            // unique index exception. Schema has ux_cash_session_open.
            long existing = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(1) FROM cash_sessions WHERE user_id = $userId AND closed_at IS NULL",
                new { userId = request.UserId }, tx, cancellationToken: ct));

            if (existing > 0)
            {
                throw new CashSessionException(
                    "You already have an open shift. Close it before opening a new one.");
            }

            long id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(@"
                INSERT INTO cash_sessions(user_id, opening_float_cents)
                VALUES ($userId, $float)
                RETURNING id",
                new { userId = request.UserId, @float = request.OpeningFloatCents },
                tx, cancellationToken: ct));

            await WriteAuditAsync(connection, tx, request.UserId, "SHIFT_OPENED", (int)id,
                null, $"Float {new Pos.Domain.Money(request.OpeningFloatCents)}", ct);

            tx.Commit();
            return (int)id;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<CloseShiftResult> CloseShiftAsync(
        int sessionId, long countedCashCents, string? notes, CancellationToken ct = default)
    {
        if (countedCashCents < 0)
            throw new CashSessionException("Counted cash cannot be negative.");

        using SqliteConnection connection = _factory.CreateOpen();
        using SqliteTransaction tx = connection.BeginTransaction();

        try
        {
            SessionRow? session = await connection.QueryFirstOrDefaultAsync<SessionRow>(new CommandDefinition(@"
                SELECT id AS Id, user_id AS UserId, opened_at AS OpenedAt,
                       opening_float_cents AS OpeningFloatCents, closed_at AS ClosedAt
                  FROM cash_sessions WHERE id = $id",
                new { id = sessionId }, tx, cancellationToken: ct));

            if (session is null)
                throw new CashSessionException($"Shift {sessionId} not found.");

            if (session.ClosedAt is not null)
                throw new CashSessionException("This shift is already closed.");

            long cashSales = await connection.ExecuteScalarAsync<long>(new CommandDefinition(@"
                SELECT COALESCE(SUM(p.amount_cents), 0)
                  FROM payments p
                  JOIN sales s ON s.id = p.sale_id
                 WHERE s.cash_session_id = $sessionId
                   AND s.status = 'COMPLETED'
                   AND p.method = 'CASH'",
                new { sessionId }, tx, cancellationToken: ct));

            long cashIn = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COALESCE(SUM(amount_cents), 0) FROM cash_movements WHERE cash_session_id = $id AND direction = 'IN'",
                new { id = sessionId }, tx, cancellationToken: ct));

            long cashOut = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COALESCE(SUM(amount_cents), 0) FROM cash_movements WHERE cash_session_id = $id AND direction = 'OUT'",
                new { id = sessionId }, tx, cancellationToken: ct));

            long expected = session.OpeningFloatCents + cashSales + cashIn - cashOut;
            long variance = countedCashCents - expected;

            await connection.ExecuteAsync(new CommandDefinition(@"
                UPDATE cash_sessions
                   SET closed_at = strftime('%Y-%m-%dT%H:%M:%SZ','now'),
                       counted_cash_cents = $counted,
                       expected_cash_cents = $expected,
                       notes = $notes
                 WHERE id = $id",
                new { counted = countedCashCents, expected, notes, id = sessionId },
                tx, cancellationToken: ct));

            await WriteAuditAsync(connection, tx, session.UserId, "SHIFT_CLOSED", sessionId,
                null,
                $"Expected {new Pos.Domain.Money(expected)} counted {new Pos.Domain.Money(countedCashCents)} variance {new Pos.Domain.Money(variance)}",
                ct);

            tx.Commit();

            return new CloseShiftResult(
                SessionId: sessionId,
                OpeningFloatCents: session.OpeningFloatCents,
                CashSalesCents: cashSales,
                CashInCents: cashIn,
                CashOutCents: cashOut,
                ExpectedCashCents: expected,
                CountedCashCents: countedCashCents,
                VarianceCents: variance);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<int> RecordMovementAsync(
        int sessionId,
        string direction,
        long amountCents,
        string reason,
        int userId,
        string businessDate,
        CancellationToken ct = default)
    {
        if (direction != "IN" && direction != "OUT")
            throw new CashSessionException("Direction must be IN or OUT.");

        if (amountCents <= 0)
            throw new CashSessionException("Amount must be greater than zero.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new CashSessionException("Reason is required for a cash movement.");

        if (string.IsNullOrWhiteSpace(businessDate) || businessDate.Length != 10)
            throw new CashSessionException("Date must be YYYY-MM-DD.");

        using SqliteConnection connection = _factory.CreateOpen();
        using SqliteTransaction tx = connection.BeginTransaction();

        try
        {
            SessionRow? session = await connection.QueryFirstOrDefaultAsync<SessionRow>(new CommandDefinition(
                "SELECT id AS Id, user_id AS UserId, closed_at AS ClosedAt FROM cash_sessions WHERE id = $id",
                new { id = sessionId }, tx, cancellationToken: ct));

            if (session is null)
                throw new CashSessionException($"Shift {sessionId} not found.");

            if (session.ClosedAt is not null)
                throw new CashSessionException("Cannot record movements on a closed shift.");

            long id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(@"
                INSERT INTO cash_movements(cash_session_id, direction, amount_cents, reason, user_id, business_date)
                VALUES ($sessionId, $direction, $amountCents, $reason, $userId, $businessDate)
                RETURNING id",
                new
                {
                    sessionId,
                    direction,
                    amountCents,
                    reason = reason.Trim(),
                    userId,
                    businessDate,
                },
                tx, cancellationToken: ct));

            await WriteAuditAsync(connection, tx, userId,
                direction == "IN" ? "CASH_IN" : "CASH_OUT",
                (int)id,
                null,
                $"{new Pos.Domain.Money(amountCents)} - {reason.Trim()}",
                ct);

            tx.Commit();
            return (int)id;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static async Task WriteAuditAsync(
        SqliteConnection c, SqliteTransaction tx, int userId, string action,
        int entityId, string? oldValue, string? newValue, CancellationToken ct)
    {
        await c.ExecuteAsync(new CommandDefinition(@"
            INSERT INTO audit_log(user_id, action, entity_type, entity_id, old_value, new_value)
            VALUES ($userId, $action, 'cash_session', $entityId, $oldValue, $newValue)",
            new { userId, action, entityId, oldValue, newValue },
            tx, cancellationToken: ct));
    }

    private sealed class SessionRow
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string OpenedAt { get; set; } = "";
        public long OpeningFloatCents { get; set; }
        public string? ClosedAt { get; set; }
    }
}
