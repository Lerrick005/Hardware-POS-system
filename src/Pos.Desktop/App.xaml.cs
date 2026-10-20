using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Pos.Application.Backup;
using Pos.Application.Catalog;
using Pos.Application.Customers;
using Pos.Application.Reports;
using Pos.Application.Sales;
using Pos.Application.Settings;
using Pos.Application.Stock;
using Pos.Application.Suppliers;
using Pos.Application.Users;
using Pos.Desktop.ViewModels;
using Pos.Desktop.Views;
using Pos.Infrastructure.Backup;
using Pos.Infrastructure.Catalog;
using Pos.Infrastructure.Customers;
using Pos.Infrastructure.Data;
using Pos.Infrastructure.Logging;
using Pos.Infrastructure.Reports;
using Pos.Infrastructure.Sales;
using Pos.Infrastructure.Settings;
using Pos.Infrastructure.Stock;
using Pos.Infrastructure.Suppliers;
using Pos.Infrastructure.Users;
using Serilog;

namespace Pos.Desktop;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Critical: prevent WPF from auto-shutting-down when the login
        // dialog closes (before MainWindow exists). With the default
        // OnLastWindowClose mode, closing the login dialog kills the app
        // before we ever show the main window.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

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
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<MigrationRunner>();

        services.AddSingleton<IPinHasher, PinHasher>();
        services.AddSingleton<IUserRepository, UserRepository>();
        services.AddSingleton<IUserSession, UserSession>();

        services.AddSingleton<IProductRepository, ProductRepository>();
        services.AddSingleton<IUnitRepository, UnitRepository>();
        services.AddSingleton<ICategoryRepository, CategoryRepository>();
        services.AddSingleton<IPriceTierRepository, PriceTierRepository>();
        services.AddSingleton<IReceiveGoods, ReceiveGoodsService>();
        services.AddSingleton<IReceiveGoodsQuery, ReceiveGoodsQuery>();
        services.AddSingleton<ICustomerRepository, CustomerRepository>();
        services.AddSingleton<ICustomerAccountService, CustomerAccountService>();
        services.AddSingleton<ISupplierRepository, SupplierRepository>();
        services.AddSingleton<ISellService, SellService>();
        services.AddSingleton<IReportsQuery, ReportsQuery>();
        services.AddSingleton<IDashboardQuery, DashboardQuery>();
        services.AddSingleton<ISettingsService, SettingsService>();

        services.AddSingleton<LoginViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<ProductsViewModel>();
        services.AddSingleton<ReceiveGoodsViewModel>();
        services.AddSingleton<CustomersViewModel>();
        services.AddSingleton<SuppliersViewModel>();
        services.AddSingleton<SellViewModel>();
        services.AddSingleton<ReportsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddTransient<AddProductViewModel>();
        services.AddTransient<AddProductView>();
        services.AddTransient<CategoriesViewModel>();
        services.AddTransient<CategoriesView>();
        services.AddTransient<NewReceiptViewModel>();
        services.AddTransient<NewReceiptView>();
        services.AddTransient<AddCustomerViewModel>();
        services.AddTransient<AddCustomerView>();
        services.AddTransient<RecordPaymentViewModel>();
        services.AddTransient<RecordPaymentView>();
        services.AddTransient<StatementViewModel>();
        services.AddTransient<StatementView>();
        services.AddTransient<AddSupplierViewModel>();
        services.AddTransient<AddSupplierView>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

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
                "MyPOS", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        SeedDefaultCashier(_services.GetRequiredService<DbConnectionFactory>());

        IUserSession session = _services.GetRequiredService<IUserSession>();
        LoginViewModel loginVm = _services.GetRequiredService<LoginViewModel>();
        LoginView loginView = new(loginVm);
        bool? loginResult = loginView.ShowDialog();

        if (loginResult != true || !session.IsLoggedIn)
        {
            Log.Information("Login not completed; shutting down.");
            Shutdown(0);
            return;
        }

        Log.Information("User logged in: {Name} ({Role})", session.CurrentUserName, session.CurrentUserRole);

        WireDialogFactories();

        MainWindowViewModel mainVm = _services.GetRequiredService<MainWindowViewModel>();
        mainVm.UserLabel = $"{session.CurrentUserName} ({session.CurrentUserRole})";

        MainWindow window = _services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();

        // Now that the real main window exists, closing it should exit the app.
        ShutdownMode = ShutdownMode.OnMainWindowClose;
    }

    private void WireDialogFactories()
    {
        if (_services is null) return;

        ProductsViewModel productsVm = _services.GetRequiredService<ProductsViewModel>();
        productsVm.CreateProductDialog = (int? productId) =>
        {
            AddProductView dialog = _services.GetRequiredService<AddProductView>();
            AddProductViewModel vm = (AddProductViewModel)dialog.DataContext;
            vm.ProductId = productId;
            dialog.Owner = MainWindow;
            return dialog;
        };
        productsVm.CreateCategoriesDialog = () =>
        {
            CategoriesView dialog = _services.GetRequiredService<CategoriesView>();
            dialog.Owner = MainWindow;
            return dialog;
        };

        ReceiveGoodsViewModel receiveVm = _services.GetRequiredService<ReceiveGoodsViewModel>();
        receiveVm.CreateNewReceiptDialog = () =>
        {
            NewReceiptView dialog = _services.GetRequiredService<NewReceiptView>();
            dialog.Owner = MainWindow;
            return dialog.ShowDialog() == true;
        };

        CustomersViewModel customersVm = _services.GetRequiredService<CustomersViewModel>();
        customersVm.CreateCustomerDialog = (int? customerId) =>
        {
            AddCustomerView dialog = _services.GetRequiredService<AddCustomerView>();
            AddCustomerViewModel vm = (AddCustomerViewModel)dialog.DataContext;
            vm.CustomerId = customerId;
            dialog.Owner = MainWindow;
            return dialog;
        };
        customersVm.CreateRecordPaymentDialog = (int customerId) =>
        {
            RecordPaymentView dialog = _services.GetRequiredService<RecordPaymentView>();
            RecordPaymentViewModel vm = (RecordPaymentViewModel)dialog.DataContext;
            vm.CustomerId = customerId;
            dialog.Owner = MainWindow;
            return dialog;
        };
        customersVm.CreateStatementDialog = (int customerId) =>
        {
            StatementView dialog = _services.GetRequiredService<StatementView>();
            StatementViewModel vm = (StatementViewModel)dialog.DataContext;
            vm.CustomerId = customerId;
            dialog.Owner = MainWindow;
            return dialog;
        };

        SuppliersViewModel suppliersVm = _services.GetRequiredService<SuppliersViewModel>();
        suppliersVm.CreateSupplierDialog = (int? supplierId) =>
        {
            AddSupplierView dialog = _services.GetRequiredService<AddSupplierView>();
            AddSupplierViewModel vm = (AddSupplierViewModel)dialog.DataContext;
            vm.SupplierId = supplierId;
            dialog.Owner = MainWindow;
            return dialog;
        };

        ISettingsService settingsService = _services.GetRequiredService<ISettingsService>();
        SellViewModel sellVm = _services.GetRequiredService<SellViewModel>();
        sellVm.CreateReceiptDialog = (SaleResult sale) =>
        {
            ShopProfile profile = settingsService.GetShopProfileAsync().GetAwaiter().GetResult();
            ReceiptView dialog = new(sale, profile);
            dialog.Owner = MainWindow;
            return dialog;
        };

        MainWindowViewModel mainVm = _services.GetRequiredService<MainWindowViewModel>();
        DashboardViewModel dashVm = _services.GetRequiredService<DashboardViewModel>();
        dashVm.NavigateToSell = () => mainVm.ShowSellCommand.Execute(null);
        dashVm.NavigateToStock = () => mainVm.ShowStockCommand.Execute(null);
        dashVm.NavigateToProducts = () => mainVm.ShowProductsCommand.Execute(null);
        dashVm.NavigateToCustomers = () => mainVm.ShowCustomersCommand.Execute(null);
        dashVm.NavigateToReports = () => mainVm.ShowReportsCommand.Execute(null);
    }

    private static void SeedDefaultCashier(DbConnectionFactory factory)
    {
        using var connection = factory.CreateOpen();
        using var countCmd = connection.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(1) FROM users";
        long userCount = Convert.ToInt64(countCmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        if (userCount > 0)
        {
            return;
        }
        using var seedCmd = connection.CreateCommand();
        seedCmd.CommandText = "INSERT INTO users(name, role, pin_hash) VALUES ('Cashier', 'CASHIER', 'PIN_NOT_SET')";
        seedCmd.ExecuteNonQuery();
        Log.Warning("Seeded default Cashier user - will ask for PIN on first login.");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_services is not null)
        {
            try
            {
                IBackupService backups = _services.GetRequiredService<IBackupService>();
                BackupInfo created = backups.CreateBackupAsync("auto-close").GetAwaiter().GetResult();
                Log.Information("Auto-backup on close: {FileName}", created.FileName);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Auto-backup on close failed (non-fatal).");
            }

            _services.Dispose();
        }

        Log.Information("MyPOS exiting.");
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}



