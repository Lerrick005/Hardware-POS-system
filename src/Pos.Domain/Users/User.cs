namespace Pos.Domain.Users;

/// <summary>
/// A staff member who can log into the POS. Role determines what they can
/// see and do (Admin / Manager / Cashier — enforced later when role guards land).
/// </summary>
public sealed class User
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "CASHIER";

    /// <summary>
    /// Salted hash from the DB. Never log this. Never return it to the UI.
    /// </summary>
    public string PinHash { get; set; } = "";

    public bool IsActive { get; set; } = true;
    public string? CreatedAt { get; set; }
}
