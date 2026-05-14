using System.Net.Http;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PosSystem.Data;
using PosSystem.Data.Repositories;
using PosSystem.Services;
using PosSystem.ViewModels;
using PosSystem.ViewModels.Admin;
using PosSystem.ViewModels.Admin.Modules;
using PosSystem.ViewModels.Game;
using PosSystem.ViewModels.Ombor;
using PosSystem.ViewModels.Pos;
using PosSystem.ViewModels.Products;
using PosSystem.Views;
using PosSystem.Views.Admin;
using PosSystem.Views.Game;
using PosSystem.Views.Pos;

namespace PosSystem;

public partial class App : Application
{
    private ServiceProvider _services = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _services = BuildServices();
        EnsureDatabase();
        _services.GetRequiredService<MainWindow>().Show();
    }

    private ServiceProvider BuildServices()
    {
        var sc = new ServiceCollection();

        // ── Database (factory = safe for singleton repos) ──────────────────────
        sc.AddDbContextFactory<AppDbContext>();

        // ── Repositories (singleton: use IDbContextFactory internally) ─────────
        sc.AddSingleton<SettingsRepository>();
        sc.AddSingleton<ProductRepository>();
        sc.AddSingleton<CustomerRepository>();
        sc.AddSingleton<SaleRepository>();
        sc.AddSingleton<PriceListRepository>();
        sc.AddSingleton<ProductTypeRepository>();

        // ── Services ──────────────────────────────────────────────────────────
        sc.AddSingleton<ConnectivityService>();
        sc.AddSingleton<NetworkLogService>();
        sc.AddHttpClient();

        sc.AddSingleton<ApiClient>(sp =>
        {
            var logHandler = new NetworkLogHandler(sp.GetRequiredService<NetworkLogService>());
            var http = new HttpClient(logHandler) { Timeout = TimeSpan.FromSeconds(15) };
            return new ApiClient(http, sp.GetRequiredService<SettingsRepository>());
        });

        sc.AddSingleton<AuthService>();
        sc.AddSingleton<SyncService>();

        // ── ViewModels (transient: fresh per navigation) ───────────────────────
        sc.AddTransient<LoginViewModel>();
        sc.AddTransient<GameViewModel>();
        sc.AddTransient<ProductsViewModel>();
        sc.AddTransient<OmborViewModel>();
        sc.AddTransient<AddProductViewModel>(sp => new AddProductViewModel(
            sp.GetRequiredService<ApiClient>(),
            sp.GetRequiredService<PriceListRepository>(),
            sp.GetRequiredService<ProductTypeRepository>()));
        sc.AddTransient<PosViewModel>();

        // Admin shell + modules (singleton so navigating away preserves state).
        sc.AddSingleton<AdminShellViewModel>();
        sc.AddSingleton<DashboardViewModel>();
        sc.AddSingleton<SalesEntryViewModel>();
        sc.AddSingleton<ReturnsViewModel>();
        sc.AddSingleton<ProductsAdminViewModel>();
        sc.AddSingleton<InventoryViewModel>();
        sc.AddSingleton<CustomersViewModel>();
        sc.AddSingleton<EmployeesViewModel>();
        sc.AddSingleton<StatisticsViewModel>();
        sc.AddSingleton<SettingsViewModel>();

        // ── Views ─────────────────────────────────────────────────────────────
        sc.AddTransient<LoginView>();
        sc.AddTransient<GameView>();
        sc.AddTransient<PosView>();
        sc.AddTransient<AdminShellView>();
        sc.AddSingleton<MainWindow>();

        return sc.BuildServiceProvider();
    }

    private void EnsureDatabase()
    {
        var factory = _services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        ApplySchemaMigrations(db);

        // Seed demo data only when there is no authenticated session.
        // Once the user logs in and syncs, real backend data replaces the demo rows.
        var auth = _services.GetRequiredService<AuthService>();
        if (!auth.HasValidSession())
            SeedDemoData(db);
    }

    // Adds new columns to existing SQLite databases without full migration.
    // Each statement is wrapped in try/catch — SQLite throws on duplicate column.
    private static void ApplySchemaMigrations(AppDbContext db)
    {
        // New tables (CREATE TABLE IF NOT EXISTS is idempotent)
        try { db.Database.ExecuteSqlRaw(
            "CREATE TABLE IF NOT EXISTS PriceLists (" +
            "Id INTEGER PRIMARY KEY, Name TEXT NOT NULL DEFAULT '', " +
            "Currency TEXT NOT NULL DEFAULT '', CurrencyId INTEGER NOT NULL DEFAULT 1, " +
            "Active INTEGER NOT NULL DEFAULT 1)"); }
        catch { }

        try { db.Database.ExecuteSqlRaw(
            "CREATE TABLE IF NOT EXISTS ProductTypes (" +
            "Id INTEGER PRIMARY KEY, Name TEXT NOT NULL DEFAULT '', " +
            "Active INTEGER NOT NULL DEFAULT 1)"); }
        catch { }

        var columns = new[]
        {
            "ALTER TABLE Products  ADD COLUMN RemoteUuid         TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE Customers ADD COLUMN RemoteUuid         TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE Sales     ADD COLUMN ServerUuid         TEXT",
            "ALTER TABLE Sales     ADD COLUMN CustomerRemoteUuid TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE SaleItems ADD COLUMN ProductRemoteUuid  TEXT NOT NULL DEFAULT ''",
        };

        foreach (var sql in columns)
        {
            try { db.Database.ExecuteSqlRaw(sql); }
            catch { /* column already exists — safe to ignore */ }
        }

        // One-time cleanup: remove seed/demo rows left over from previous runs.
        // After login the real backend data will be synced in their place.
        try { db.Database.ExecuteSqlRaw(
            "DELETE FROM Products  WHERE RemoteUuid = '' OR RemoteUuid IS NULL"); }
        catch { }
        try { db.Database.ExecuteSqlRaw(
            "DELETE FROM Customers WHERE RemoteUuid = '' OR RemoteUuid IS NULL"); }
        catch { }
    }

    private static void SeedDemoData(AppDbContext db)
    {
        if (db.Products.Any()) return;

        db.Products.AddRange(
            new Core.Entities.Product { Id = 1,  Name = "Coca-Cola 0.5L",    Code = "CC001", Barcode = "4870204999999", Price = 8_000,  Unit = "dona",  Stock = 120, CategoryId = 1, CategoryName = "Ichimliklar" },
            new Core.Entities.Product { Id = 2,  Name = "Sprite 0.5L",       Code = "SP001", Barcode = "4870204888888", Price = 7_500,  Unit = "dona",  Stock = 85,  CategoryId = 1, CategoryName = "Ichimliklar" },
            new Core.Entities.Product { Id = 3,  Name = "Pepsi 0.5L",        Code = "PP001", Barcode = "4870204777777", Price = 7_000,  Unit = "dona",  Stock = 60,  CategoryId = 1, CategoryName = "Ichimliklar" },
            new Core.Entities.Product { Id = 4,  Name = "Aqua 1.5L",         Code = "AQ001", Barcode = "4870204666666", Price = 5_000,  Unit = "dona",  Stock = 200, CategoryId = 1, CategoryName = "Ichimliklar" },
            new Core.Entities.Product { Id = 5,  Name = "Non (katta)",       Code = "BR001", Barcode = "4870204555555", Price = 4_000,  Unit = "dona",  Stock = 50,  CategoryId = 2, CategoryName = "Non mahsulotlari" },
            new Core.Entities.Product { Id = 6,  Name = "Tort \"Medovik\"",  Code = "CK001", Barcode = "4870204444444", Price = 45_000, Unit = "dona",  Stock = 10,  CategoryId = 2, CategoryName = "Non mahsulotlari" },
            new Core.Entities.Product { Id = 7,  Name = "Sut 1L",            Code = "ML001", Barcode = "4870204333333", Price = 12_000, Unit = "litr",  Stock = 40,  CategoryId = 3, CategoryName = "Sut mahsulotlari" },
            new Core.Entities.Product { Id = 8,  Name = "Qatiq 500g",        Code = "YG001", Barcode = "4870204222222", Price = 9_000,  Unit = "dona",  Stock = 30,  CategoryId = 3, CategoryName = "Sut mahsulotlari" },
            new Core.Entities.Product { Id = 9,  Name = "Tuxum (10 dona)",   Code = "EG001", Barcode = "4870204111111", Price = 22_000, Unit = "quti",  Stock = 25,  CategoryId = 4, CategoryName = "Tuxum" },
            new Core.Entities.Product { Id = 10, Name = "Guruch 1kg",        Code = "RC001", Barcode = "4870204000000", Price = 15_000, Unit = "kg",    Stock = 80,  CategoryId = 5, CategoryName = "Don mahsulotlari" },
            new Core.Entities.Product { Id = 11, Name = "Un 2kg",            Code = "FL001", Barcode = "4870203999999", Price = 18_000, Unit = "kg",    Stock = 60,  CategoryId = 5, CategoryName = "Don mahsulotlari" },
            new Core.Entities.Product { Id = 12, Name = "Shakar 1kg",        Code = "SG001", Barcode = "4870203888888", Price = 14_000, Unit = "kg",    Stock = 100, CategoryId = 5, CategoryName = "Don mahsulotlari" },
            new Core.Entities.Product { Id = 13, Name = "O'simlik yogi 1L",  Code = "OL001", Barcode = "4870203777777", Price = 25_000, Unit = "litr",  Stock = 45,  CategoryId = 6, CategoryName = "Moy" },
            new Core.Entities.Product { Id = 14, Name = "Makaron 400g",      Code = "PS001", Barcode = "4870203666666", Price = 8_500,  Unit = "dona",  Stock = 70,  CategoryId = 5, CategoryName = "Don mahsulotlari" },
            new Core.Entities.Product { Id = 15, Name = "Pomidor 1kg",       Code = "TM001", Barcode = "4870203555555", Price = 12_000, Unit = "kg",    Stock = 35,  CategoryId = 7, CategoryName = "Sabzavotlar" },
            new Core.Entities.Product { Id = 16, Name = "Kartoshka 1kg",     Code = "PT001", Barcode = "4870203444444", Price = 6_000,  Unit = "kg",    Stock = 150, CategoryId = 7, CategoryName = "Sabzavotlar" },
            new Core.Entities.Product { Id = 17, Name = "Piyoz 1kg",         Code = "ON001", Barcode = "4870203333333", Price = 4_500,  Unit = "kg",    Stock = 90,  CategoryId = 7, CategoryName = "Sabzavotlar" },
            new Core.Entities.Product { Id = 18, Name = "Olma 1kg",          Code = "AP001", Barcode = "4870203222222", Price = 18_000, Unit = "kg",    Stock = 40,  CategoryId = 8, CategoryName = "Mevalar" }
        );

        db.Customers.AddRange(
            new Core.Entities.Customer { Id = 1, Name = "Alisher Karimov",   Phone = "+998901234567" },
            new Core.Entities.Customer { Id = 2, Name = "Malika Yusupova",   Phone = "+998907654321" },
            new Core.Entities.Customer { Id = 3, Name = "Bobur Toshmatov",   Phone = "+998991112233" },
            new Core.Entities.Customer { Id = 4, Name = "Dilnoza Ergasheva", Phone = "+998993334455" },
            new Core.Entities.Customer { Id = 5, Name = "Sardor Holiqov",    Phone = "+998946677889" }
        );

        db.SaveChanges();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services.Dispose();
        base.OnExit(e);
    }

    public T GetService<T>() where T : notnull =>
        _services.GetRequiredService<T>();
}
