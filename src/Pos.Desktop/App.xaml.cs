using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Pos.Application.Catalog;
using Pos.Application.Customers;
using Pos.Application.Stock;
using Pos.Desktop.ViewModels;
using Pos.Desktop.Views;
using Pos.Infrastructure.Catalog;
using Pos.Infrastructure.Customers;
using Pos.Infrastructure.Data;
using Pos.Infrastructure.Logging;
using Pos.Infrastructure.Stock;
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
        services.AddLogging(b => b.AddSerilog(Log.Logger, dispose: false));

        services.AddSingleton(new DbConnectionFactory(dbPath));
        services.AddSingleton<BackupService>();
        services.AddSingleton<MigrationRunner>();

        services.AddSingleton<IProductRepository, ProductRepository>();
        services.AddSingleton<IUnitRepository, UnitRepository>();
        services.AddSingleton<ICategoryRepository, CategoryRepository>();
        services.AddSingleton<IPriceTierRepository, PriceTierRepository>();
        services.AddSingleton<IReceiveGoods, ReceiveGoodsService>();
        services.AddSingleton<IReceiveGoodsQuery, ReceiveGoodsQuery>();
        services.AddSingleton<ICustomerRepository, CustomerRepository>();
        services.AddSingleton<ICustomerAccountService, CustomerAccountService>();

        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<ProductsViewModel>();
        services.AddSingleton<ReceiveGoodsViewModel>();
        services.AddSingleton<CustomersViewModel>();
        services.AddTransient<AddProductViewModel>();
        services.AddTransient<AddProductView>();
        services.AddTransient<NewReceiptViewModel>();
        services.AddTransient<NewReceiptView>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        _services = services.BuildServiceProvider();

        ProductsViewModel productsVm = _services.GetRequiredService<ProductsViewModel>();
        productsVm.CreateAddProductDialog = () => _services.GetRequiredService<AddProductView>();

        ReceiveGoodsViewModel receiveVm = _services.GetRequiredService<ReceiveGoodsViewModel>();
        receiveVm.CreateNewReceiptDialog = () =>
        {
            NewReceiptView dialog = _services.GetRequiredService<NewReceiptView>();
            dialog.Owner = MainWindow;
            return dialog.ShowDialog() == true;
        };

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
                "MyPOS", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        MainWindow window = _services.GetRequiredService<MainWindow>();
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
