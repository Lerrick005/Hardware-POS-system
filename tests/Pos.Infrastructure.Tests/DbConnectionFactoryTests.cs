using Microsoft.Data.Sqlite;
using Pos.Infrastructure.Data;

namespace Pos.Infrastructure.Tests;

public sealed class DbConnectionFactoryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pos-test-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (string suffix in new[] { "", "-wal", "-shm" })
        {
            string file = _dbPath + suffix;
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public void CreateOpen_AppliesRequiredPragmas()
    {
        DbConnectionFactory factory = new(_dbPath);
        using SqliteConnection connection = factory.CreateOpen();

        Assert.Equal(1L, ScalarLong(connection, "PRAGMA foreign_keys;"));
        Assert.Equal("wal", ScalarString(connection, "PRAGMA journal_mode;"));
        Assert.Equal(2L, ScalarLong(connection, "PRAGMA synchronous;"));
        Assert.Equal(5000L, ScalarLong(connection, "PRAGMA busy_timeout;"));
    }

    [Fact]
    public void CreateOpen_AppliesPragmasOnEveryConnection()
    {
        DbConnectionFactory factory = new(_dbPath);

        using (SqliteConnection first = factory.CreateOpen())
        {
            Assert.Equal(1L, ScalarLong(first, "PRAGMA foreign_keys;"));
        }

        using SqliteConnection second = factory.CreateOpen();
        Assert.Equal(1L, ScalarLong(second, "PRAGMA foreign_keys;"));
        Assert.Equal(5000L, ScalarLong(second, "PRAGMA busy_timeout;"));
    }

    [Fact]
    public void CreateOpen_CreatesTheDatabaseFile()
    {
        DbConnectionFactory factory = new(_dbPath);
        using SqliteConnection connection = factory.CreateOpen();

        Assert.True(File.Exists(_dbPath));
        Assert.Equal(_dbPath, factory.DatabasePath);
    }

    [Fact]
    public void Constructor_RejectsEmptyPath()
    {
        Assert.Throws<ArgumentException>(() => new DbConnectionFactory(""));
    }

    private static long ScalarLong(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string ScalarString(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
