using System.Reflection;
using Microsoft.Data.Sqlite;
using Pos.Application.Backup;

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

public sealed class MigrationRunner
{
    private static readonly Version ZeroVersion = new(0, 0);
    private static readonly Migration[] Migrations =
    {
        new(new Version(1, 1), "Pos.Infrastructure.Migrations.001_baseline.sql"),
        new(new Version(1, 2), "Pos.Infrastructure.Migrations.002_admin_promotion.sql"),
        new(new Version(1, 3), "Pos.Infrastructure.Migrations.003_expenses.sql"),
        new(new Version(1, 4), "Pos.Infrastructure.Migrations.004_multi_unit_pricing.sql"),
    };

    private readonly DbConnectionFactory _factory;
    private readonly IBackupService _backup;

    public MigrationRunner(DbConnectionFactory factory, IBackupService backup)
    {
        _factory = factory;
        _backup = backup;
    }

    public Version TargetVersion => Migrations[^1].Version;

    public MigrationResult Run()
    {
        Version fromVersion = ZeroVersion;

        try
        {
            using SqliteConnection connection = _factory.CreateOpen();

            bool isFresh = !TableExists(connection, "app_meta");
            Version current = isFresh ? ZeroVersion : ReadVersion(connection);
            fromVersion = current;

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

            Migration[] pending = Migrations
                .Where(m => m.Version > current)
                .OrderBy(m => m.Version)
                .ToArray();

            if (pending.Length == 0)
            {
                return new MigrationResult(MigrationOutcome.UpToDate, current, TargetVersion, null, null);
            }

            string? backupPath = null;
            if (!isFresh)
            {
                backupPath = _backup.CreateBackupAsync("pre-migrate").GetAwaiter().GetResult().Path;
            }

            foreach (Migration migration in pending)
            {
                ApplyScript(connection, migration.ResourceName);
            }

            MigrationOutcome outcome = isFresh ? MigrationOutcome.FreshInstall : MigrationOutcome.Upgraded;
            return new MigrationResult(outcome, current, TargetVersion, backupPath, null);
        }
        catch (Exception ex)
        {
            return new MigrationResult(MigrationOutcome.Failed, fromVersion, TargetVersion, null, ex.Message);
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

