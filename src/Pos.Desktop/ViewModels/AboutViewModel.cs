using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Pos.Infrastructure.Data;

namespace Pos.Desktop.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    private readonly DbConnectionFactory _factory;

    [ObservableProperty] private string appVersion = "1.0.0";
    [ObservableProperty] private string dbSizeDisplay = "";
    [ObservableProperty] private string dbPath = "";
    [ObservableProperty] private string schemaVersion = "";

    public AboutViewModel(DbConnectionFactory factory)
    {
        _factory = factory;
    }

    public string GetSummary()
    {
        string ver = typeof(AboutViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        return $"v{ver}";
    }

    public void Refresh()
    {
        AppVersion = typeof(AboutViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        DbPath = _factory.DatabasePath;

        try
        {
            FileInfo fi = new(_factory.DatabasePath);
            DbSizeDisplay = fi.Length < 1024 * 1024
                ? $"{fi.Length / 1024.0:0.#} KB"
                : $"{fi.Length / (1024.0 * 1024.0):0.#} MB";

            using var conn = _factory.CreateOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM app_meta WHERE key = 'schema_version'";
            SchemaVersion = cmd.ExecuteScalar() as string ?? "unknown";
        }
        catch
        {
            DbSizeDisplay = "unknown";
            SchemaVersion = "unknown";
        }
    }
}

