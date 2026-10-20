namespace Pos.Application.Users;

/// <summary>
/// Holds the currently logged-in user for the running app. Singleton for the
/// lifetime of the process. Everything that writes UserId to the DB queries
/// this session — nothing hardcodes UserId anymore.
///
/// Login/logout are explicit methods here, not repository concerns. The session
/// is a runtime concept, not a database concept.
/// </summary>
public interface IUserSession
{
    /// <summary>True when a user is logged in. False at app startup.</summary>
    bool IsLoggedIn { get; }

    /// <summary>The current user's id, or null if nobody is logged in.</summary>
    int? CurrentUserId { get; }

    /// <summary>The current user's name, or null if nobody is logged in.</summary>
    string? CurrentUserName { get; }

    /// <summary>The current user's role (ADMIN / MANAGER / CASHIER), or null.</summary>
    string? CurrentUserRole { get; }

    /// <summary>
    /// Convenience property for services that require a user id. Throws
    /// if called when nobody is logged in. Forces "login before any write"
    /// at the type level, not by convention.
    /// </summary>
    int RequireUserId { get; }

    /// <summary>Sets the session to the given user. Called after successful login.</summary>
    void Start(int userId, string name, string role);

    /// <summary>Clears the session. Called on logout or app exit.</summary>
    void End();
}
