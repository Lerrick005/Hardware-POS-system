using Microsoft.Data.Sqlite;

namespace Pos.Infrastructure.Data;

/// <summary>
/// Creates hot backups of the SQLite database using the online backup API
/// (not file copy), so an open WAL database is copied consistently.
/// </summary>
public sealed class BackupService
{
    private readonly DbConnectionFactory _factory;

    public BackupService(DbConnectionFactory factory) => _factory = factory;

    /// <summary>
    /// Writes a backup beside the live database: pos.backup-yyyyMMdd-HHmmss.db
    /// Returns the full path of the backup file.
    /// </summary>
    public string CreateBackup(string? tag = null)
    {
        string directory = Path.GetDirectoryName(_factory.DatabasePath) ?? ".";
        string baseName = Path.GetFileNameWithoutExtension(_factory.DatabasePath);
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture);
        string suffix = string.IsNullOrWhiteSpace(tag) ? string.Empty : "-" + tag;
        string target = Path.Combine(directory, $"{baseName}.backup-{stamp}{suffix}.db");

        using SqliteConnection source = _factory.CreateOpen();
        using SqliteConnection destination = new(new SqliteConnectionStringBuilder
        {
            DataSource = target,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        destination.Open();

        source.BackupDatabase(destination);

        return target;
    }
}
