namespace Pos.Application.Users;

/// <summary>
/// Row shape for the Staff management page. Distinct from LoginUserRow —
/// this one is for the Admin who manages staff, not the login picker.
/// Never includes pin_hash.
/// </summary>
public sealed class StaffMember
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public bool IsActive { get; set; }

    /// <summary>True if the user has a real PIN (not the schema sentinel).</summary>
    public bool HasPinSet { get; set; }

    public string? CreatedAt { get; set; }
}

public interface IUserAdminService
{
    /// <summary>
    /// All staff. By default excludes archived (is_active = 0) so the owner
    /// sees only current team. Pass includeArchived:true to see removed staff
    /// and unarchive them.
    /// </summary>
    Task<IReadOnlyList<StaffMember>> ListStaffAsync(bool includeArchived = false, CancellationToken ct = default);

    Task<StaffMember?> GetByIdAsync(int userId, CancellationToken ct = default);

    /// <summary>
    /// Creates a new staff member with a valid PIN. Name, role, and PIN are
    /// all required together — a staff account without a PIN cannot log in,
    /// so we don't allow creating one.
    ///
    /// Role must be one of: ADMIN, MANAGER, CASHIER.
    /// PIN must be 4 to 6 digits.
    /// Returns the new user's id.
    /// </summary>
    Task<int> AddStaffAsync(
        string name,
        string role,
        string pin,
        CancellationToken ct = default);

    /// <summary>
    /// Updates name and role. Does not touch the PIN — use ChangePinAsync.
    ///
    /// Refuses to demote the last active ADMIN to a lower role — that would
    /// lock the shop out of Settings permanently.
    /// </summary>
    Task UpdateStaffAsync(
        int userId,
        string name,
        string role,
        CancellationToken ct = default);

    /// <summary>
    /// Sets a new PIN for the user. Delegate to IUserRepository.SetPinAsync
    /// under the hood so PIN format validation stays in one place.
    /// </summary>
    Task ChangePinAsync(int userId, string newPin, CancellationToken ct = default);

    /// <summary>
    /// Soft-deletes (is_active = 0). The user can no longer log in and does
    /// not appear on the login picker. Their historic sales, deliveries, and
    /// audit entries remain intact.
    ///
    /// Refuses if the user is the last active ADMIN — that would leave the
    /// shop with no way to reach Settings.
    /// </summary>
    Task ArchiveStaffAsync(int userId, CancellationToken ct = default);

    /// <summary>
    /// Re-activates a previously archived staff member. The old PIN hash is
    /// still valid — the owner should ChangePinAsync after unarchiving if the
    /// staff member has forgotten it.
    /// </summary>
    Task UnarchiveStaffAsync(int userId, CancellationToken ct = default);
}
