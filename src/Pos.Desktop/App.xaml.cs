using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Pos.Infrastructure.Data;
using Pos.Infrastructure.Logging;
using Serilog;

namespace Pos.Desktop;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string dataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MyPOS");
        Directory.CreateDirectory(dataFolder);

        string logFolder = Path.Combine(dataFolder, "logs");
        SerilogBootstrap.Initialize(Path.Combine(logFolder, "pos-.log"));
        Log.Information("MyPOS starting. Data folder: {DataFolder}", dataFolder);

        string dbPath = Path.Combine(dataFolder, "pos.db");

        ServiceCollection services = new();
        services.AddSingleton(new DbConnectionFactory(dbPath));
        services.AddSingleton<BackupService>();
        services.AddSingleton<MigrationRunner>();
        _services = services.BuildServiceProvider();

        MigrationRunner runner = _services.GetRequiredService<MigrationRunner>();
        MigrationResult migration = runner.Run();
        Log.Information(
            "Migration outcome: {Outcome}, from {From} to {To}, backup={Backup}",
            migration.Outcome, migration.FromVersion, migration.ToVersion, migration.BackupPath ?? "none");

        if (migration.Outcome == MigrationOutcome.Failed)
        {
            Log.Error("Migration failed: {Error}", migration.Error);
            MessageBox.Show(
                $"Database could not be prepared: {migration.Error}",
                "MyPOS",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        MainWindow window = new();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        Log.Information("MyPOS exiting.");
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
