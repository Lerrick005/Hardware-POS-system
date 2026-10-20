using System.Text.RegularExpressions;
using Dapper;
using Pos.Application.Users;
using Pos.Infrastructure.Data;

namespace Pos.Infrastructure.Users;

public sealed partial class UserRepository : IUserRepository
{
    private const string PinNotSetSentinel = "PIN_NOT_SET";

    private readonly DbConnectionFactory _factory;
    private readonly IPinHasher _hasher;

    public UserRepository(DbConnectionFactory factory, IPinHasher hasher)
    {
        _factory = factory;
        _hasher = hasher;
    }

    public async Task<IReadOnlyList<LoginUserRow>> ListForLoginAsync(CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();

        var rows = await connection.QueryAsync<UserRow>(new CommandDefinition(@"
            SELECT id         AS Id,
                   name       AS Name,
                   role       AS Role,
                   pin_hash   AS PinHash
              FROM users
             WHERE is_active = 1
             ORDER BY name COLLATE NOCASE",
            cancellationToken: ct));

        return rows
            .Select(r => new LoginUserRow
            {
                Id = r.Id,
                Name = r.Name,
                Role = r.Role,
                HasPinSet = !string.Equals(r.PinHash, PinNotSetSentinel, StringComparison.Ordinal)
                            && !string.IsNullOrWhiteSpace(r.PinHash),
            })
            .ToList();
    }

    public async Task<LoginResult> LoginAsync(int userId, string pin, CancellationToken ct = default)
    {
        if (userId <= 0)
        {
            return LoginResult.Fail("Choose a staff member.");
        }

        if (string.IsNullOrWhiteSpace(pin))
        {
            return LoginResult.Fail("Enter your PIN.");
        }

        using var connection = _factory.CreateOpen();

        UserRow? row = await connection.QueryFirstOrDefaultAsync<UserRow>(new CommandDefinition(@"
            SELECT id, name, role, pin_hash AS PinHash
              FROM users
             WHERE id = $userId AND is_active = 1",
            new { userId }, cancellationToken: ct));

        if (row is null)
        {
            return LoginResult.Fail("Staff member not found.");
        }

        if (string.Equals(row.PinHash, PinNotSetSentinel, StringComparison.Ordinal))
        {
            return LoginResult.Fail("This account has no PIN yet. Tap 'Set PIN' to create one.");
        }

        bool ok = _hasher.Verify(pin, row.PinHash);
        if (!ok)
        {
            return LoginResult.Fail("Wrong PIN. Try again.");
        }

        return LoginResult.Ok(row.Id, row.Name, row.Role);
    }

    public async Task SetPinAsync(int userId, string newPin, CancellationToken ct = default)
    {
        if (userId <= 0)
        {
            throw new ArgumentException("Invalid user id.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(newPin) || !PinPattern().IsMatch(newPin))
        {
            throw new ArgumentException("PIN must be 4 to 6 digits.", nameof(newPin));
        }

        string hash = _hasher.Hash(newPin);

        using var connection = _factory.CreateOpen();
        int updated = await connection.ExecuteAsync(new CommandDefinition(@"
            UPDATE users
               SET pin_hash = $hash
             WHERE id = $userId AND is_active = 1",
            new { hash, userId }, cancellationToken: ct));

        if (updated == 0)
        {
            throw new InvalidOperationException(
                $"User {userId} not found or is inactive; PIN not saved.");
        }
    }

    [GeneratedRegex("^[0-9]{4,6}$")]
    private static partial Regex PinPattern();

    private sealed class UserRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Role { get; set; } = "";
        public string PinHash { get; set; } = "";
    }
}
