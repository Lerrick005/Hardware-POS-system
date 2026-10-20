using Dapper;
using Pos.Application.Cash;
using Pos.Infrastructure.Data;

namespace Pos.Infrastructure.Cash;

public sealed class CashSessionQuery : ICashSessionQuery
{
    private readonly DbConnectionFactory _factory;

    public CashSessionQuery(DbConnectionFactory factory) => _factory = factory;

    public async Task<OpenCashSession?> GetOpenForUserAsync(int userId, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();

        return await connection.QueryFirstOrDefaultAsync<OpenCashSession>(new CommandDefinition(@"
            SELECT cs.id AS Id,
                   cs.user_id AS UserId,
                   u.name AS UserName,
                   cs.opened_at AS OpenedAt,
                   cs.opening_float_cents AS OpeningFloatCents,
                   (SELECT COALESCE(SUM(p.amount_cents), 0)
                      FROM payments p
                      JOIN sales s ON s.id = p.sale_id
                     WHERE s.cash_session_id = cs.id
                       AND s.status = 'COMPLETED'
                       AND p.method = 'CASH') AS CashSalesCents,
                   (SELECT COALESCE(SUM(amount_cents), 0) FROM cash_movements
                     WHERE cash_session_id = cs.id AND direction = 'IN') AS CashInCents,
                   (SELECT COALESCE(SUM(amount_cents), 0) FROM cash_movements
                     WHERE cash_session_id = cs.id AND direction = 'OUT') AS CashOutCents
              FROM cash_sessions cs
              LEFT JOIN users u ON u.id = cs.user_id
             WHERE cs.user_id = $userId AND cs.closed_at IS NULL",
            new { userId }, cancellationToken: ct));
    }

    public async Task<OpenCashSession?> GetByIdAsync(int sessionId, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();

        return await connection.QueryFirstOrDefaultAsync<OpenCashSession>(new CommandDefinition(@"
            SELECT cs.id AS Id,
                   cs.user_id AS UserId,
                   u.name AS UserName,
                   cs.opened_at AS OpenedAt,
                   cs.opening_float_cents AS OpeningFloatCents,
                   (SELECT COALESCE(SUM(p.amount_cents), 0)
                      FROM payments p
                      JOIN sales s ON s.id = p.sale_id
                     WHERE s.cash_session_id = cs.id
                       AND s.status = 'COMPLETED'
                       AND p.method = 'CASH') AS CashSalesCents,
                   (SELECT COALESCE(SUM(amount_cents), 0) FROM cash_movements
                     WHERE cash_session_id = cs.id AND direction = 'IN') AS CashInCents,
                   (SELECT COALESCE(SUM(amount_cents), 0) FROM cash_movements
                     WHERE cash_session_id = cs.id AND direction = 'OUT') AS CashOutCents
              FROM cash_sessions cs
              LEFT JOIN users u ON u.id = cs.user_id
             WHERE cs.id = $sessionId",
            new { sessionId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<CashMovementRow>> ListMovementsAsync(int sessionId, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();

        var rows = await connection.QueryAsync<CashMovementRow>(new CommandDefinition(@"
            SELECT m.id AS Id,
                   m.direction AS Direction,
                   m.amount_cents AS AmountCents,
                   m.reason AS Reason,
                   u.name AS UserName,
                   m.created_at AS CreatedAt
              FROM cash_movements m
              LEFT JOIN users u ON u.id = m.user_id
             WHERE m.cash_session_id = $sessionId
             ORDER BY m.id DESC",
            new { sessionId }, cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<IReadOnlyList<ClosedShiftSummary>> ListRecentClosedAsync(int limit = 30, CancellationToken ct = default)
    {
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));

        using var connection = _factory.CreateOpen();

        var rows = await connection.QueryAsync<ClosedShiftSummary>(new CommandDefinition(@"
            SELECT cs.id AS Id,
                   u.name AS UserName,
                   cs.opened_at AS OpenedAt,
                   cs.closed_at AS ClosedAt,
                   cs.opening_float_cents AS OpeningFloatCents,
                   COALESCE(cs.expected_cash_cents, 0) AS ExpectedCashCents,
                   COALESCE(cs.counted_cash_cents, 0) AS CountedCashCents
              FROM cash_sessions cs
              LEFT JOIN users u ON u.id = cs.user_id
             WHERE cs.closed_at IS NOT NULL
             ORDER BY cs.closed_at DESC
             LIMIT $limit",
            new { limit }, cancellationToken: ct));

        return rows.AsList();
    }
}
