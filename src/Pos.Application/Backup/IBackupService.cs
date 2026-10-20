namespace Pos.Application.Backup;

/// <summary>
/// Metadata about one backup file on disk. Used by the Settings UI to list
/// and to let the owner pick which one to restore.
/// </summary>
public sealed class BackupInfo
{
    public string Path { get; set; } = "";
    public string FileName { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public long SizeBytes { get; set; }
    public string? Tag { get; set; }
}

public interface IBackupService
{
    /// <summary>
    /// Creates an online backup (safe while the app is running, uses SQLite's
    /// backup API — never a raw file copy). Returns the metadata of the new
    /// backup file.
    ///
    /// Tag examples: "manual", "auto-close", "pre-restore", "pre-migrate".
    /// Tags are informational; they help the owner identify why a backup exists.
    /// </summary>
    Task<BackupInfo> CreateBackupAsync(string? tag = null, CancellationToken ct = default);

    /// <summary>
    /// All backups in the backup folder, newest first.
    /// </summary>
    Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(CancellationToken ct = default);

    /// <summary>
    /// Replaces the live database with the given backup. Creates a safety
    /// backup first (tag "pre-restore") so a mistaken restore can be undone.
    ///
    /// The caller MUST restart the app after this completes — open connections
    /// are invalidated. UI should prompt "Restore complete. Please restart."
    /// </summary>
    Task RestoreBackupAsync(string backupFilePath, CancellationToken ct = default);

    /// <summary>
    /// UTC timestamp of the most recent backup, or null if none exists.
    /// Used by the Settings card to show "Last backup: X ago".
    /// </summary>
    Task<DateTime?> GetLastBackupTimeUtcAsync(CancellationToken ct = default);
}
