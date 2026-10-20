using Microsoft.Data.Sqlite;
using Pos.Infrastructure.Backup;
using Pos.Infrastructure.Data;

namespace Pos.Infrastructure.Tests;

/// <summary>
/// Disposable fresh SQLite database with the migration applied.
/// Each test instance gets its own temp file; Dispose removes the file + WAL/SHM.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    public string Path { get; }
    public DbConnectionFactory Factory { get; }
    public BackupService Backup { get; }
    public MigrationRunner Migrations { get; }

    public TestDatabase()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pos-t-{Guid.NewGuid():N}.db");
        Factory = new DbConnectionFactory(Path);
        Backup = new BackupService(Factory);
        Migrations = new MigrationRunner(Factory, Backup);
        MigrationResult result = Migrations.Run();
        if (result.Outcome != MigrationOutcome.FreshInstall)
        {
            throw new InvalidOperationException($"Test db setup failed: {result.Outcome} {result.Error}");
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (string suffix in new[] { "", "-wal", "-shm" })
        {
            string file = Path + suffix;
            try { if (File.Exists(file)) File.Delete(file); } catch { /* best effort */ }
        }
    }
}

