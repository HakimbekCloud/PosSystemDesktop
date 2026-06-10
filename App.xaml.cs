using System.Net.Http;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PosSystem.Data;
using PosSystem.Data.Repositories;
using PosSystem.Services;
using PosSystem.ViewModels;
using PosSystem.ViewModels.Game;
using PosSystem.ViewModels.Ombor;
using PosSystem.ViewModels.Pos;
using PosSystem.ViewModels.Products;
using PosSystem.Views;
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

        // ── Views ─────────────────────────────────────────────────────────────
        sc.AddTransient<LoginView>();
        sc.AddTransient<GameView>();
        sc.AddTransient<PosView>();
        sc.AddSingleton<MainWindow>();

        return sc.BuildServiceProvider();
    }

    private void EnsureDatabase()
    {
        var factory = _services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        ApplySchemaMigrations(db);

        // No demo seed: the POS catalog stays empty until the first successful
        // login + sync, which is correct. Demo rows could otherwise be sold
        // before sync, producing LOCAL_ONLY sales that can never reach the server.
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
            "ALTER TABLE Sales     ADD COLUMN SyncAttempts       INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE Sales     ADD COLUMN LastSyncError      TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE Sales     ADD COLUMN LastSyncAttemptAt  TEXT NULL",
        };

        foreach (var sql in columns)
        {
            try { db.Database.ExecuteSqlRaw(sql); }
            catch { /* column already exists — safe to ignore */ }
        }

        // One-time TARGETED cleanup: remove ONLY the known demo seed rows that older
        // builds inserted (RemoteUuid = '' AND a code/phone from the exact seed set).
        // The previous blanket "DELETE ... WHERE RemoteUuid = ''" was a latent trap:
        // it would silently destroy any future locally-created row whose server UUID
        // failed to persist. Any OTHER empty-RemoteUuid row must now SURVIVE startup.
        try { db.Database.ExecuteSqlRaw(
            "DELETE FROM Products WHERE RemoteUuid = '' AND Code IN (" +
            "'CC001','SP001','PP001','AQ001','BR001','CK001','ML001','YG001'," +
            "'EG001','RC001','FL001','SG001','OL001','PS001','TM001','PT001'," +
            "'ON001','AP001')"); }
        catch { }
        try { db.Database.ExecuteSqlRaw(
            "DELETE FROM Customers WHERE RemoteUuid = '' AND Phone IN (" +
            "'+998901234567','+998907654321','+998991112233'," +
            "'+998993334455','+998946677889')"); }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services.Dispose();
        base.OnExit(e);
    }

    public T GetService<T>() where T : notnull =>
        _services.GetRequiredService<T>();
}
