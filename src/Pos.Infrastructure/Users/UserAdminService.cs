using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.Sqlite;
using Pos.Application.Users;
using Pos.Infrastructure.Data;

namespace Pos.Infrastructure.Users;

public sealed partial class UserAdminService : IUserAdminService
{
    private const string PinNotSetSentinel = "PIN_NOT_SET";

    private readonly DbConnectionFactory _factory;
    private readonly IPinHasher _hasher;
    private readonly IUserRepository _users;

    public UserAdminService(DbConnectionFactory factory, IPinHasher hasher, IUserRepository users)
    {
        _factory = factory;
        _hasher = hasher;
        _users = users;
    }

    public async Task<IReadOnlyList<StaffMember>> ListStaffAsync(
        bool includeArchived = false,
        CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();

        string sql = @"
            SELECT id         AS Id,
                   name       AS Name,
                   role       AS Role,
                   is_active  AS IsActive,
                   pin_hash   AS PinHash,
                   created_at AS CreatedAt
              FROM users" +
            (includeArchived ? "" : " WHERE is_active = 1") +
            " ORDER BY is_active DESC, name COLLATE NOCASE";

        var rows = await connection.QueryAsync<UserRow>(
            new CommandDefinition(sql, cancellationToken: ct));

        return rows.Select(r => new StaffMember
        {
            Id = r.Id,
            Name = r.Name,
            Role = r.Role,
            IsActive = r.IsActive,
            HasPinSet = !string.IsNullOrWhiteSpace(r.PinHash)
                        && !string.Equals(r.PinHash, PinNotSetSentinel, StringComparison.Ordinal),
            CreatedAt = r.CreatedAt,
        }).ToList();
    }

    public async Task<StaffMember?> GetByIdAsync(int userId, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();

        UserRow? row = await connection.QueryFirstOrDefaultAsync<UserRow>(new CommandDefinition(@"
            SELECT id AS Id, name AS Name, role AS Role, is_active AS IsActive,
                   pin_hash AS PinHash, created_at AS CreatedAt
              FROM users WHERE id = $userId",
            new { userId }, cancellationToken: ct));

        if (row is null) return null;

        return new StaffMember
        {
            Id = row.Id,
            Name = row.Name,
            Role = row.Role,
            IsActive = row.IsActive,
            HasPinSet = !string.IsNullOrWhiteSpace(row.PinHash)
                        && !string.Equals(row.PinHash, PinNotSetSentinel, StringComparison.Ordinal),
            CreatedAt = row.CreatedAt,
        };
    }

    public async Task<int> AddStaffAsync(
        string name,
        string role,
        string pin,
        CancellationToken ct = default)
    {
        ValidateName(name);
        ValidateRole(role);
        ValidatePin(pin);

        string hash = _hasher.Hash(pin);

        using SqliteConnection connection = _factory.CreateOpen();
        using SqliteTransaction tx = connection.BeginTransaction();

        try
        {
            // Note: uuid is not in the users table — users is a local-only concept
            // in v1 (no cloud sync). If multi-branch sync ever needs user mapping,
            // a migration adds a uuid column.
            long id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(@"
                INSERT INTO users(name, role, pin_hash, is_active)
                VALUES ($name, $role, $hash, 1)
                RETURNING id",
                new { name = name.Trim(), role, hash },
                tx, cancellationToken: ct));

            await WriteAuditAsync(connection, tx, "STAFF_ADDED", (int)id,
                oldValue: null, newValue: $"{name.Trim()} as {role}", ct);

            tx.Commit();
            return (int)id;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task UpdateStaffAsync(
        int userId,
        string name,
        string role,
        CancellationToken ct = default)
    {
        ValidateName(name);
        ValidateRole(role);

        using SqliteConnection connection = _factory.CreateOpen();
        using SqliteTransaction tx = connection.BeginTransaction();

        try
        {
            UserRow? target = await connection.QueryFirstOrDefaultAsync<UserRow>(new CommandDefinition(
                "SELECT id AS Id, name AS Name, role AS Role, is_active AS IsActive FROM users WHERE id = $userId",
                new { userId }, tx, cancellationToken: ct));

            if (target is null)
            {
                throw new InvalidOperationException($"Staff member {userId} not found.");
            }

            // Guard: cannot demote the last active Admin.
            if (target.IsActive
                && string.Equals(target.Role, "ADMIN", StringComparison.Ordinal)
                && !string.Equals(role, "ADMIN", StringComparison.Ordinal))
            {
                long otherAdmins = await CountOtherActiveAdminsAsync(connection, tx, userId, ct);
                if (otherAdmins == 0)
                {
                    throw new InvalidOperationException(
                        "Cannot change the role of the last Admin. Promote another staff member to Admin first.");
                }
            }

            await connection.ExecuteAsync(new CommandDefinition(@"
                UPDATE users
                   SET name = $name,
                       role = $role
                 WHERE id = $userId",
                new { name = name.Trim(), role, userId },
                tx, cancellationToken: ct));

            await WriteAuditAsync(connection, tx, "STAFF_UPDATED", userId,
                oldValue: $"{target.Name} as {target.Role}",
                newValue: $"{name.Trim()} as {role}", ct);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task ChangePinAsync(int userId, string newPin, CancellationToken ct = default)
    {
        ValidatePin(newPin);

        // Delegate to IUserRepository — it already has format validation and the
        // PIN hash logic in one place. No duplication here.
        await _users.SetPinAsync(userId, newPin, ct);

        using SqliteConnection connection = _factory.CreateOpen();
        using SqliteTransaction tx = connection.BeginTransaction();

        try
        {
            await WriteAuditAsync(connection, tx, "STAFF_PIN_CHANGED", userId,
                oldValue: null, newValue: null, ct);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task ArchiveStaffAsync(int userId, CancellationToken ct = default)
    {
        using SqliteConnection connection = _factory.CreateOpen();
        using SqliteTransaction tx = connection.BeginTransaction();

        try
        {
            UserRow? target = await connection.QueryFirstOrDefaultAsync<UserRow>(new CommandDefinition(
                "SELECT id AS Id, name AS Name, role AS Role, is_active AS IsActive FROM users WHERE id = $userId",
                new { userId }, tx, cancellationToken: ct));

            if (target is null)
            {
                throw new InvalidOperationException($"Staff member {userId} not found.");
            }

            if (!target.IsActive)
            {
                return; // already archived, no-op
            }

            // Guard: cannot archive the last active Admin.
            if (string.Equals(target.Role, "ADMIN", StringComparison.Ordinal))
            {
                long otherAdmins = await CountOtherActiveAdminsAsync(connection, tx, userId, ct);
                if (otherAdmins == 0)
                {
                    throw new InvalidOperationException(
                        "Cannot remove the last Admin. Promote another staff member to Admin first.");
                }
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE users SET is_active = 0 WHERE id = $userId",
                new { userId }, tx, cancellationToken: ct));

            await WriteAuditAsync(connection, tx, "STAFF_ARCHIVED", userId,
                oldValue: $"{target.Name} as {target.Role}", newValue: null, ct);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task UnarchiveStaffAsync(int userId, CancellationToken ct = default)
    {
        using SqliteConnection connection = _factory.CreateOpen();
        using SqliteTransaction tx = connection.BeginTransaction();

        try
        {
            UserRow? target = await connection.QueryFirstOrDefaultAsync<UserRow>(new CommandDefinition(
                "SELECT id AS Id, name AS Name, role AS Role, is_active AS IsActive FROM users WHERE id = $userId",
                new { userId }, tx, cancellationToken: ct));

            if (target is null)
            {
                throw new InvalidOperationException($"Staff member {userId} not found.");
            }

            if (target.IsActive)
            {
                return; // already active, no-op
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE users SET is_active = 1 WHERE id = $userId",
                new { userId }, tx, cancellationToken: ct));

            await WriteAuditAsync(connection, tx, "STAFF_UNARCHIVED", userId,
                oldValue: null, newValue: $"{target.Name} as {target.Role}", ct);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static async Task<long> CountOtherActiveAdminsAsync(
        SqliteConnection connection,
        SqliteTransaction tx,
        int excludeUserId,
        CancellationToken ct)
    {
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(1) FROM users WHERE role = 'ADMIN' AND is_active = 1 AND id != $excludeUserId",
            new { excludeUserId }, tx, cancellationToken: ct));
    }

    private static async Task WriteAuditAsync(
        SqliteConnection connection,
        SqliteTransaction tx,
        string action,
        int entityId,
        string? oldValue,
        string? newValue,
        CancellationToken ct)
    {
        // userId on the audit row is the person who did the action, taken from
        // the ambient session via the caller (this class runs under an Admin
        // session, so we don't need to pass it explicitly — the audit table
        // foreign key may be null when the actor is not yet known).
        await connection.ExecuteAsync(new CommandDefinition(@"
            INSERT INTO audit_log(user_id, action, entity_type, entity_id, old_value, new_value)
            VALUES (NULL, $action, 'user', $entityId, $oldValue, $newValue)",
            new { action, entityId, oldValue, newValue },
            tx, cancellationToken: ct));
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        if (name.Trim().Length > 100)
        {
            throw new ArgumentException("Name is too long (max 100 characters).", nameof(name));
        }
    }

    private static void ValidateRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role) || !RolePattern().IsMatch(role))
        {
            throw new ArgumentException(
                "Role must be one of ADMIN, MANAGER, or CASHIER.", nameof(role));
        }
    }

    private static void ValidatePin(string pin)
    {
        if (string.IsNullOrWhiteSpace(pin) || !PinPattern().IsMatch(pin))
        {
            throw new ArgumentException("PIN must be 4 to 6 digits.", nameof(pin));
        }
    }

    [GeneratedRegex("^(ADMIN|MANAGER|CASHIER)$")]
    private static partial Regex RolePattern();

    [GeneratedRegex("^[0-9]{4,6}$")]
    private static partial Regex PinPattern();

    private sealed class UserRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Role { get; set; } = "";
        public bool IsActive { get; set; }
        public string PinHash { get; set; } = "";
        public string? CreatedAt { get; set; }
    }
}
