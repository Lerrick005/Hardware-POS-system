using Dapper;
using Pos.Application.Users;
using Pos.Infrastructure.Users;

namespace Pos.Infrastructure.Tests;

public sealed class UserAdminServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly PinHasher _hasher = new();
    private readonly UserRepository _users;
    private readonly UserAdminService _admin;

    public UserAdminServiceTests()
    {
        _users = new UserRepository(_db.Factory, _hasher);
        _admin = new UserAdminService(_db.Factory, _hasher, _users);
    }

    public void Dispose() => _db.Dispose();

    private int SeedAdmin(string name = "Admin")
    {
        using var c = _db.Factory.CreateOpen();
        return (int)c.ExecuteScalar<long>(@"
            INSERT INTO users(name, role, pin_hash, is_active)
            VALUES ($name, 'ADMIN', 'PIN_NOT_SET', 1) RETURNING id",
            new { name });
    }

    private int CountUsers(string where = "1")
    {
        using var c = _db.Factory.CreateOpen();
        return (int)c.ExecuteScalar<long>($"SELECT COUNT(1) FROM users WHERE {where}");
    }

    // ===== AddStaff =====

    [Fact]
    public async Task AddStaff_CreatesUser_WithValidPinHash()
    {
        int id = await _admin.AddStaffAsync("Mary Cashier", "CASHIER", "1234");

        Assert.True(id > 0);
        Assert.Equal(1, CountUsers("role = 'CASHIER'"));

        // Login with the new PIN must succeed — proving the hash was written correctly.
        LoginResult login = await _users.LoginAsync(id, "1234");
        Assert.True(login.Success);
        Assert.Equal("Mary Cashier", login.UserName);
        Assert.Equal("CASHIER", login.Role);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddStaff_EmptyName_Throws(string name)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _admin.AddStaffAsync(name, "CASHIER", "1234"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("admin")]
    [InlineData("SUPERUSER")]
    [InlineData("OWNER")]
    public async Task AddStaff_BadRole_Throws(string role)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _admin.AddStaffAsync("Test", role, "1234"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("12")]
    [InlineData("1234567")]
    [InlineData("abcd")]
    public async Task AddStaff_BadPin_Throws(string pin)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _admin.AddStaffAsync("Test", "CASHIER", pin));
    }

    // ===== UpdateStaff =====

    [Fact]
    public async Task UpdateStaff_ChangesNameAndRole()
    {
        int id = await _admin.AddStaffAsync("Old Name", "CASHIER", "1234");

        await _admin.UpdateStaffAsync(id, "New Name", "MANAGER");

        StaffMember? member = await _admin.GetByIdAsync(id);
        Assert.NotNull(member);
        Assert.Equal("New Name", member!.Name);
        Assert.Equal("MANAGER", member.Role);
    }

    [Fact]
    public async Task UpdateStaff_CannotDemoteLastAdmin()
    {
        int adminId = SeedAdmin("Solo Admin");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _admin.UpdateStaffAsync(adminId, "Solo Admin", "CASHIER"));

        Assert.Contains("last Admin", ex.Message);
    }

    [Fact]
    public async Task UpdateStaff_CanDemoteAdmin_WhenAnotherExists()
    {
        int admin1 = SeedAdmin("Admin One");
        SeedAdmin("Admin Two");

        await _admin.UpdateStaffAsync(admin1, "Admin One", "MANAGER");

        StaffMember? member = await _admin.GetByIdAsync(admin1);
        Assert.Equal("MANAGER", member!.Role);
    }

    // ===== ChangePin =====

    [Fact]
    public async Task ChangePin_AllowsNewPin_BlocksOld()
    {
        int id = await _admin.AddStaffAsync("Test", "CASHIER", "1234");

        await _admin.ChangePinAsync(id, "9999");

        LoginResult oldPin = await _users.LoginAsync(id, "1234");
        LoginResult newPin = await _users.LoginAsync(id, "9999");

        Assert.False(oldPin.Success);
        Assert.True(newPin.Success);
    }

    [Fact]
    public async Task ChangePin_InvalidFormat_Throws()
    {
        int id = await _admin.AddStaffAsync("Test", "CASHIER", "1234");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _admin.ChangePinAsync(id, "abc"));
    }

    // ===== Archive =====

    [Fact]
    public async Task ArchiveStaff_ArchivesUser()
    {
        int cashierId = await _admin.AddStaffAsync("Temp", "CASHIER", "1234");

        await _admin.ArchiveStaffAsync(cashierId);

        StaffMember? member = await _admin.GetByIdAsync(cashierId);
        Assert.False(member!.IsActive);
    }

    [Fact]
    public async Task ArchiveStaff_CannotArchiveLastAdmin()
    {
        int adminId = SeedAdmin("Solo Admin");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _admin.ArchiveStaffAsync(adminId));

        Assert.Contains("last Admin", ex.Message);

        // Admin must still be active.
        StaffMember? member = await _admin.GetByIdAsync(adminId);
        Assert.True(member!.IsActive);
    }

    [Fact]
    public async Task ArchiveStaff_CanArchiveAdmin_WhenAnotherExists()
    {
        int admin1 = SeedAdmin("Admin One");
        SeedAdmin("Admin Two");

        await _admin.ArchiveStaffAsync(admin1);

        StaffMember? member = await _admin.GetByIdAsync(admin1);
        Assert.False(member!.IsActive);
    }

    [Fact]
    public async Task UnarchiveStaff_Reactivates_AndPreservesPinHash()
    {
        int id = await _admin.AddStaffAsync("Rehire", "CASHIER", "1234");
        await _admin.ArchiveStaffAsync(id);

        // After archive, login must fail.
        LoginResult archived = await _users.LoginAsync(id, "1234");
        Assert.False(archived.Success);

        await _admin.UnarchiveStaffAsync(id);

        // After unarchive, old PIN works again — hash preserved.
        LoginResult back = await _users.LoginAsync(id, "1234");
        Assert.True(back.Success);
    }

    // ===== Audit =====

    [Fact]
    public async Task ArchiveAndUnarchive_WriteAuditEntries()
    {
        int id = await _admin.AddStaffAsync("Test", "CASHIER", "1234");
        await _admin.ArchiveStaffAsync(id);
        await _admin.UnarchiveStaffAsync(id);

        using var c = _db.Factory.CreateOpen();

        int added = c.ExecuteScalar<int>("SELECT COUNT(1) FROM audit_log WHERE action = 'STAFF_ADDED'");
        int archived = c.ExecuteScalar<int>("SELECT COUNT(1) FROM audit_log WHERE action = 'STAFF_ARCHIVED'");
        int unarchived = c.ExecuteScalar<int>("SELECT COUNT(1) FROM audit_log WHERE action = 'STAFF_UNARCHIVED'");

        Assert.Equal(1, added);
        Assert.Equal(1, archived);
        Assert.Equal(1, unarchived);
    }

    // ===== ListStaff =====

    [Fact]
    public async Task ListStaff_ExcludesArchived_ByDefault()
    {
        int keep = await _admin.AddStaffAsync("Active", "CASHIER", "1111");
        int gone = await _admin.AddStaffAsync("Archived", "CASHIER", "2222");
        await _admin.ArchiveStaffAsync(gone);

        var active = await _admin.ListStaffAsync();
        var all = await _admin.ListStaffAsync(includeArchived: true);

        Assert.Single(active);
        Assert.Equal("Active", active[0].Name);
        Assert.Equal(2, all.Count);
    }
}
