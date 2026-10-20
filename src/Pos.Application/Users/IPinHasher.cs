namespace Pos.Application.Users;

/// <summary>
/// Hashes and verifies 4-6 digit numeric PINs using PBKDF2-SHA256.
///
/// Storage format: base64(salt)$base64(hash)
/// Salt is 16 bytes (random per user), hash is 32 bytes.
/// A stored value of "PIN_NOT_SET" (the schema seed) is treated as "no PIN yet"
/// and must be reset before login is possible.
/// </summary>
public interface IPinHasher
{
    /// <summary>Produces a storable hash string for the given PIN.</summary>
    string Hash(string pin);

    /// <summary>Returns true only if the pin matches the stored hash.</summary>
    bool Verify(string pin, string storedHash);
}
