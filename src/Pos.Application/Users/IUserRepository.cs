using Pos.Domain.Users;

namespace Pos.Application.Users;

/// <summary>
/// Row shape for the login screen staff picker. Deliberately excludes pin_hash —
/// the UI must never see it.
/// </summary>
public sealed class LoginUserRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public bool HasPinSet { get; set; }
}

public sealed class LoginResult
{
    public bool Success { get; init; }
    public int? UserId { get; init; }
    public string? UserName { get; init; }
    public string? Role { get; init; }
    public string? ErrorMessage { get; init; }

    public static LoginResult Ok(int id, string name, string role) =>
        new() { Success = true, UserId = id, UserName = name, Role = role };

    public static LoginResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}

public interface IUserRepository
{
    /// <summary>
    /// All active users for the login picker. Names and roles only.
    /// HasPinSet is true if the user has a real PIN (not the schema sentinel).
    /// </summary>
    Task<IReadOnlyList<LoginUserRow>> ListForLoginAsync(CancellationToken ct = default);

    /// <summary>
    /// Attempts a login. Returns success/failure — never throws for bad PIN.
    /// Failure messages are the shop-language versions the UI shows directly.
    /// </summary>
    Task<LoginResult> LoginAsync(int userId, string pin, CancellationToken ct = default);

    /// <summary>
    /// Sets (or resets) a user's PIN. Used on first launch, and by the owner
    /// to change a staff PIN later.
    /// </summary>
    Task SetPinAsync(int userId, string newPin, CancellationToken ct = default);
}
