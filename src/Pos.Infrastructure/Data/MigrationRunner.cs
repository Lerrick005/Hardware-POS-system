using System.Reflection;
using Microsoft.Data.Sqlite;

namespace Pos.Infrastructure.Data;

public enum MigrationOutcome
{
    FreshInstall,
    UpToDate,
    Upgraded,
    SchemaAhead,
    Failed,
}

public sealed record MigrationResult(
    MigrationOutcome Outcome,
    Version FromVersion,
    Version ToVersion,
    string? BackupPath,
    string? Error);

public sealed record Migration(Version Version, string ResourceName);

/// <summary>
/// Forward-only migration runner. Applies embedded SQL scripts in version order.
/// Takes an online backup before applying any migration to a non-empty database.
/// </summary>
public sealed class MigrationRunner
{
    private static readonly Version ZeroVersion = new(0, 0);
    private static readonly Migration[] Migrations =
    {
        new(new Version(1, 1), "Pos.Infrastructure.Migrations.001_baseline.sql"),
    };

    private readonly DbConnectionFactory _factory;
    private readonly BackupService _backup;

    public MigrationRunner(DbConnectionFactory factory, BackupService backup)
    {
        _factory = factory;
        _backup = backup;
    }

    public Version TargetVersion => Migrations[^1].Version;

    public MigrationResult Run()
    {
        try
        {
            using SqliteConnection connection = _factory.CreateOpen();

            if (!TableExists(connection, "app_meta"))
            {
                ApplyScript(connection, Migrations[0].ResourceName);
                return new MigrationResult(MigrationOutcome.FreshInstall, ZeroVersion, TargetVersion, null, null);
            }

            Version current = ReadVersion(connection);
            if (current == TargetVersion)
            {
                return new MigrationResult(MigrationOutcome.UpToDate, current, TargetVersion, null, null);
            }

            if (current > TargetVersion)
            {
                return new MigrationResult(
                    MigrationOutcome.SchemaAhead,
                    current,
                    TargetVersion,
                    null,
                    "Database schema is newer than this application supports.");
            }

            Migration[] pending = Migrations.Where(m => m.Version > current).OrderBy(m => m.Version).ToArray();
            if (pending.Length == 0)
            {
                return new MigrationResult(MigrationOutcome.UpToDate, current, TargetVersion, null, null);
            }

            string backupPath = _backup.CreateBackup("pre-migrate");
            foreach (Migration migration in pending)
            {
                ApplyScript(connection, migration.ResourceName);
            }

            return new MigrationResult(MigrationOutcome.Upgraded, current, TargetVersion, backupPath, null);
        }
        catch (Exception ex)
        {
            return new MigrationResult(MigrationOutcome.Failed, ZeroVersion, TargetVersion, null, ex.Message);
        }
    }

    private static bool TableExists(SqliteConnection connection, string name)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";
        command.Parameters.AddWithValue("$name", name);
        return command.ExecuteScalar() is not null;
    }

    private static Version ReadVersion(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM app_meta WHERE key = 'schema_version';";
        string? raw = command.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ZeroVersion;
        }

        return Version.TryParse(raw, out Version? parsed) ? parsed : ZeroVersion;
    }

    private static void ApplyScript(SqliteConnection connection, string resourceName)
    {
        string sql = ReadResource(resourceName);

        using SqliteTransaction transaction = connection.BeginTransaction();
        try
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static string ReadResource(string resourceName)
    {
        Assembly assembly = typeof(MigrationRunner).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            string available = string.Join(", ", assembly.GetManifestResourceNames());
            throw new InvalidOperationException(
                $"Embedded migration '{resourceName}' not found. Available: {available}");
        }

        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
