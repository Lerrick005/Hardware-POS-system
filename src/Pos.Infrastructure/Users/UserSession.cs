using Pos.Application.Users;

namespace Pos.Infrastructure.Users;

public sealed class UserSession : IUserSession
{
    private int? _userId;
    private string? _userName;
    private string? _userRole;

    public bool IsLoggedIn => _userId is not null;

    public int? CurrentUserId => _userId;
    public string? CurrentUserName => _userName;
    public string? CurrentUserRole => _userRole;

    public int RequireUserId =>
        _userId ?? throw new InvalidOperationException(
            "No user is logged in. The caller attempted to write data without a session.");

    public void Start(int userId, string name, string role)
    {
        if (userId <= 0)
        {
            throw new ArgumentException("Invalid user id.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("User name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(role))
        {
            throw new ArgumentException("User role is required.", nameof(role));
        }

        _userId = userId;
        _userName = name;
        _userRole = role;
    }

    public void End()
    {
        _userId = null;
        _userName = null;
        _userRole = null;
    }
}
