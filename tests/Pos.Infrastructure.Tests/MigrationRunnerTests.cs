using Microsoft.Data.Sqlite;
using Pos.Infrastructure.Backup;
using Pos.Infrastructure.Data;

namespace Pos.Infrastructure.Tests;

public sealed class MigrationRunnerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pos-mig-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (string file in Directory.EnumerateFiles(Path.GetDirectoryName(_dbPath)!, $"{Path.GetFileNameWithoutExtension(_dbPath)}*"))
        {
            try { File.Delete(file); } catch { /* best effort */ }
        }
    }

    private (DbConnectionFactory factory, BackupService backup, MigrationRunner runner) Build()
    {
        DbConnectionFactory factory = new(_dbPath);
        BackupService backup = new(factory);
        return (factory, backup, new MigrationRunner(factory, backup));
    }

    private static void SeedProduct(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO products(uuid, name, base_unit_id) VALUES ('u-seed', 'Test Product', 1);";
        command.ExecuteNonQuery();
    }

    [Fact]
    public void Run_FreshDatabase_AppliesBaseline()
    {
        (_, _, MigrationRunner runner) = Build();

        MigrationResult result = runner.Run();

        Assert.Equal(MigrationOutcome.FreshInstall, result.Outcome);
        Assert.Null(result.Error);
        Assert.Equal(new Version(1, 3), result.ToVersion);
    }

    [Fact]
    public void Run_SecondTime_ReportsUpToDate()
    {
        (_, _, MigrationRunner runner) = Build();
        runner.Run();

        MigrationResult second = runner.Run();

        Assert.Equal(MigrationOutcome.UpToDate, second.Outcome);
        Assert.Null(second.Error);
    }

    [Fact]
    public void Run_DatabaseAheadOfApp_ReportsSchemaAhead()
    {
        (DbConnectionFactory factory, _, MigrationRunner runner) = Build();
        runner.Run();

        using (SqliteConnection connection = factory.CreateOpen())
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE app_meta SET value = '2.0' WHERE key = 'schema_version';";
            command.ExecuteNonQuery();
        }

        MigrationResult result = runner.Run();

        Assert.Equal(MigrationOutcome.SchemaAhead, result.Outcome);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void AfterFreshInstall_SchemaIsUsable()
    {
        (DbConnectionFactory factory, _, MigrationRunner runner) = Build();
        runner.Run();

        using SqliteConnection connection = factory.CreateOpen();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO products(uuid, name, base_unit_id) VALUES ('u1', 'Cement 50kg', 1);
            INSERT INTO stock_movements(product_id, movement_type, qty_milli, ref_type, business_date)
                VALUES (1, 'OPENING', 10000, 'OPENING', '2026-10-19');
            SELECT stock_milli FROM products WHERE id = 1;";
        long stock = Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(10000L, stock);
    }

    [Fact]
    public void AfterFreshInstall_StockGuardStillActive()
    {
        (DbConnectionFactory factory, _, MigrationRunner runner) = Build();
        runner.Run();

        using SqliteConnection connection = factory.CreateOpen();
        SeedProduct(connection);

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE products SET stock_milli = 1 WHERE id = 1;";

        Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
    }
}



