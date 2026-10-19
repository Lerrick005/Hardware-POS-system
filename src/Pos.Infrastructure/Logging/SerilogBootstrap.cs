using Serilog;

namespace Pos.Infrastructure.Logging;

/// <summary>
/// Configures the global Serilog logger to write rolling daily files.
/// Call once on application startup, before any other logging.
/// </summary>
public static class SerilogBootstrap
{
    public static void Initialize(string logFilePath)
    {
        string? directory = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(
                path: logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: false,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}
