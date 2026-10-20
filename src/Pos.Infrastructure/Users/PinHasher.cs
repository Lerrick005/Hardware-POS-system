using System.Security.Cryptography;
using System.Text;
using Pos.Application.Users;

namespace Pos.Infrastructure.Users;

public sealed class PinHasher : IPinHasher
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int Iterations = 100_000;
    private const string Sentinel = "PIN_NOT_SET";
    private const char Separator = '$';

    public string Hash(string pin)
    {
        if (string.IsNullOrWhiteSpace(pin))
        {
            throw new ArgumentException("PIN cannot be empty.", nameof(pin));
        }

        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] hash = Derive(pin, salt, Iterations);

        return Convert.ToBase64String(salt) + Separator + Convert.ToBase64String(hash);
    }

    public bool Verify(string pin, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(pin) || string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        // The schema seed uses a placeholder that means "no PIN yet".
        if (storedHash == Sentinel)
        {
            return false;
        }

        int sepIndex = storedHash.IndexOf(Separator);
        if (sepIndex < 0)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(storedHash.Substring(0, sepIndex));
            expected = Convert.FromBase64String(storedHash.Substring(sepIndex + 1));
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] actual = Derive(pin, salt, Iterations);

        // Constant-time comparison — prevents timing attacks where the attacker
        // measures response time to guess the hash byte by byte.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Derive(string pin, byte[] salt, int iterations)
    {
        // PBKDF2-SHA256 with 100,000 iterations. On a low-end PC this takes
        // ~150-200ms per verification — acceptable for login, prohibitive for
        // brute-forcing a 4-digit PIN.
        return Rfc2898DeriveBytes.Pbkdf2(
            password: Encoding.UTF8.GetBytes(pin),
            salt: salt,
            iterations: iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: HashBytes);
    }
}
