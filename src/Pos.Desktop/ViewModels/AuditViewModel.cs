using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dapper;
using Microsoft.Extensions.Logging;
using Pos.Infrastructure.Data;

namespace Pos.Desktop.ViewModels;

public sealed class AuditRow
{
    public int Id { get; set; }
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public int? EntityId { get; set; }
    public string? UserName { get; set; }
    public string? NewValue { get; set; }
    public string CreatedAt { get; set; } = "";
    public string WhenDisplay => CreatedAt.Replace("T", " ").Replace("Z", "");
}

public partial class AuditViewModel : ObservableObject
{
    private readonly DbConnectionFactory _factory;
    private readonly ILogger<AuditViewModel> _logger;

    [ObservableProperty] private bool isBusy;

    public ObservableCollection<AuditRow> Entries { get; } = new();

    public AuditViewModel(DbConnectionFactory factory, ILogger<AuditViewModel> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<string> GetSummaryAsync()
    {
        try
        {
            using var conn = _factory.CreateOpen();
            string? latest = await conn.ExecuteScalarAsync<string>(
                "SELECT created_at FROM audit_log ORDER BY id DESC LIMIT 1");
            if (string.IsNullOrEmpty(latest)) return "No activity yet";
            return $"Last activity {Relative(latest)}";
        }
        catch { return "Audit log"; }
    }

    public async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            using var conn = _factory.CreateOpen();
            var rows = await conn.QueryAsync<AuditRow>(@"
                SELECT a.id AS Id,
                       a.action AS Action,
                       COALESCE(a.entity_type, '') AS EntityType,
                       a.entity_id AS EntityId,
                       u.name AS UserName,
                       a.new_value AS NewValue,
                       a.created_at AS CreatedAt
                  FROM audit_log a
                  LEFT JOIN users u ON u.id = a.user_id
                 ORDER BY a.id DESC
                 LIMIT 200");
            Entries.Clear();
            foreach (var r in rows) Entries.Add(r);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audit load failed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    private static string Relative(string isoUtc)
    {
        if (!DateTime.TryParse(isoUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime dt))
        {
            return "recently";
        }
        TimeSpan age = DateTime.UtcNow - dt.ToUniversalTime();
        if (age.TotalMinutes < 1) return "just now";
        if (age.TotalMinutes < 60) return $"{(int)age.TotalMinutes}m ago";
        if (age.TotalHours < 24) return $"{(int)age.TotalHours}h ago";
        return $"{(int)age.TotalDays}d ago";
    }
}
