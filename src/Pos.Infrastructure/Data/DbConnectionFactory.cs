using Microsoft.Data.Sqlite;

namespace Pos.Infrastructure.Data;

/// <summary>
/// Creates SQLite connections with the PRAGMAs the POS depends on.
/// foreign_keys=ON, journal_mode=WAL, synchronous=FULL, busy_timeout.
/// </summary>
public sealed class DbConnectionFactory
{
    private readonly string _connectionString;

    public DbConnectionFactory(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path is required.", nameof(databasePath));
        }

        DatabasePath = databasePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            Pooling = true,
        }.ToString();
    }

    public string DatabasePath { get; }

    public SqliteConnection CreateOpen()
    {
        SqliteConnection connection = new(_connectionString);
        connection.Open();
        ApplyPragmas(connection);
        return connection;
    }

    private static void ApplyPragmas(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "PRAGMA foreign_keys = ON;" +
            "PRAGMA journal_mode = WAL;" +
            "PRAGMA synchronous = FULL;" +
            "PRAGMA busy_timeout = 5000;" +
            "PRAGMA cache_size = -65536;" +   // 64 MB page cache — keeps 20k products hot
            "PRAGMA temp_store = MEMORY;";
        command.ExecuteNonQuery();
    }
}

