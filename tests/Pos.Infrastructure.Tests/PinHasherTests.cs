using Pos.Infrastructure.Users;

namespace Pos.Infrastructure.Tests;

public sealed class PinHasherTests
{
    private readonly PinHasher _hasher = new();

    [Fact]
    public void HashThenVerify_CorrectPin_ReturnsTrue()
    {
        string stored = _hasher.Hash("1234");

        Assert.True(_hasher.Verify("1234", stored));
    }

    [Fact]
    public void Verify_WrongPin_ReturnsFalse()
    {
        string stored = _hasher.Hash("1234");

        Assert.False(_hasher.Verify("9999", stored));
        Assert.False(_hasher.Verify("1235", stored));
        Assert.False(_hasher.Verify("", stored));
    }

    [Fact]
    public void Hash_SamePinTwice_ProducesDifferentHashes()
    {
        // This proves the salt is random. Without it, two users with PIN 1234
        // would produce identical stored hashes — an obvious security leak.
        string a = _hasher.Hash("1234");
        string b = _hasher.Hash("1234");

        Assert.NotEqual(a, b);
        Assert.True(_hasher.Verify("1234", a));
        Assert.True(_hasher.Verify("1234", b));
    }

    [Fact]
    public void Hash_HasExpectedFormat()
    {
        string stored = _hasher.Hash("1234");

        int sep = stored.IndexOf('$');
        Assert.True(sep > 0);

        byte[] salt = Convert.FromBase64String(stored.Substring(0, sep));
        byte[] hash = Convert.FromBase64String(stored.Substring(sep + 1));

        Assert.Equal(16, salt.Length);
        Assert.Equal(32, hash.Length);
    }

    [Fact]
    public void Verify_Sentinel_ReturnsFalse()
    {
        // The seeded Cashier user has pin_hash = 'PIN_NOT_SET'. Verify must
        // never accept that, even if someone literally types "PIN_NOT_SET".
        Assert.False(_hasher.Verify("PIN_NOT_SET", "PIN_NOT_SET"));
        Assert.False(_hasher.Verify("anything", "PIN_NOT_SET"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-separator-here")]
    [InlineData("!!!not-base64!!!$also-not-base64")]
    [InlineData("only-salt$")]
    [InlineData("$only-hash")]
    public void Verify_MalformedHash_ReturnsFalse(string stored)
    {
        // Never throw on bad data — return false. Login flow must not crash
        // because of a corrupt row.
        Assert.False(_hasher.Verify("1234", stored));
    }

    [Fact]
    public void Verify_EmptyStoredHash_ReturnsFalse()
    {
        Assert.False(_hasher.Verify("1234", ""));
        Assert.False(_hasher.Verify("1234", "   "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Hash_EmptyPin_Throws(string? pin)
    {
        Assert.Throws<ArgumentException>(() => _hasher.Hash(pin!));
    }

    [Fact]
    public void Hash_DifferentPins_ProduceDifferentHashes()
    {
        string a = _hasher.Hash("1111");
        string b = _hasher.Hash("2222");

        Assert.True(_hasher.Verify("1111", a));
        Assert.False(_hasher.Verify("2222", a));
        Assert.True(_hasher.Verify("2222", b));
        Assert.False(_hasher.Verify("1111", b));
    }

    [Fact]
    public void Hash_LongerPin_StillWorks()
    {
        // A 6-digit PIN should also work. Schema doesn't constrain length;
        // the login UI will offer 4 by default, 6 if the owner wants.
        string stored = _hasher.Hash("654321");

        Assert.True(_hasher.Verify("654321", stored));
        Assert.False(_hasher.Verify("65432", stored));
    }
}
