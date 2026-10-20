using Microsoft.Data.Sqlite;
using Pos.Application.Backup;
using Pos.Infrastructure.Data;

namespace Pos.Infrastructure.Backup;

public sealed class BackupService : IBackupService
{
    private readonly DbConnectionFactory _factory;
    private readonly string _backupDir;

    public BackupService(DbConnectionFactory factory)
    {
        _factory = factory;
        string? dataFolder = Path.GetDirectoryName(factory.DatabasePath);
        _backupDir = Path.Combine(dataFolder ?? ".", "backups");
        Directory.CreateDirectory(_backupDir);
    }

    public async Task<BackupInfo> CreateBackupAsync(string? tag = null, CancellationToken ct = default)
    {
        string safeTag = string.IsNullOrWhiteSpace(tag)
            ? "manual"
            : new string(tag.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());

        string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", System.Globalization.CultureInfo.InvariantCulture);
        string fileName = $"pos_backup_{stamp}_{safeTag}.db";
        string target = Path.Combine(_backupDir, fileName);

        using (SqliteConnection source = _factory.CreateOpen())
        using (SqliteConnection destination = new(new SqliteConnectionStringBuilder
        {
            DataSource = target,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString()))
        {
            destination.Open();
            source.BackupDatabase(destination);
        }

        FileInfo info = new(target);

        return await Task.FromResult(new BackupInfo
        {
            Path = target,
            FileName = fileName,
            CreatedAtUtc = info.LastWriteTimeUtc,
            SizeBytes = info.Length,
            Tag = safeTag,
        });
    }

    public async Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_backupDir))
        {
            return Array.Empty<BackupInfo>();
        }

        return await Task.FromResult(Directory
            .GetFiles(_backupDir, "pos_backup_*.db")
            .Select(path =>
            {
                FileInfo info = new(path);
                string name = info.Name;
                string? parsedTag = ExtractTag(name);

                return new BackupInfo
                {
                    Path = path,
                    FileName = name,
                    CreatedAtUtc = info.LastWriteTimeUtc,
                    SizeBytes = info.Length,
                    Tag = parsedTag,
                };
            })
            .OrderByDescending(b => b.CreatedAtUtc)
            .ToList());
    }

    public async Task RestoreBackupAsync(string backupFilePath, CancellationToken ct = default)
    {
        if (!File.Exists(backupFilePath))
        {
            throw new FileNotFoundException(
                "The backup file could not be found.", backupFilePath);
        }

        // Safety backup first — so a mistaken restore can be undone.
        await CreateBackupAsync("pre-restore", ct);

        // Release all open connections before swapping files. Without this,
        // SQLite may hold file handles and Windows blocks the copy.
        SqliteConnection.ClearAllPools();

        string livePath = _factory.DatabasePath;
        string walPath = livePath + "-wal";
        string shmPath = livePath + "-shm";

        // Copy the backup over a temp file, then atomically swap — reduces the
        // window where a crash during restore leaves nothing valid.
        string tempRestorePath = livePath + ".restoring";
        File.Copy(backupFilePath, tempRestorePath, overwrite: true);

        if (File.Exists(livePath)) File.Delete(livePath);
        if (File.Exists(walPath)) File.Delete(walPath);
        if (File.Exists(shmPath)) File.Delete(shmPath);

        File.Move(tempRestorePath, livePath);
    }

    public async Task<DateTime?> GetLastBackupTimeUtcAsync(CancellationToken ct = default)
    {
        IReadOnlyList<BackupInfo> backups = await ListBackupsAsync(ct);
        return backups.FirstOrDefault()?.CreatedAtUtc;
    }

    private static string? ExtractTag(string fileName)
    {
        // Format: pos_backup_yyyyMMdd_HHmmss_fff_tag.db
        string nameNoExt = Path.GetFileNameWithoutExtension(fileName);
        const string prefix = "pos_backup_";
        if (!nameNoExt.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        string rest = nameNoExt.Substring(prefix.Length);
        int lastUnderscore = rest.LastIndexOf('_');
        if (lastUnderscore < 0) return null;

        return rest.Substring(lastUnderscore + 1);
    }
}
