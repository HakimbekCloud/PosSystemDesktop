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

        // Phase 10.5B: opt-in runtime tenant DB switching. Default-disabled —
        // only takes effect when (1) tenant_db_runtime_enabled=="1" in global
        // settings AND (2) TenantCutoverReadinessGate green-lights the
        // last-used tenant. EnsureDatabase below then runs Migrate against
        // whichever DB the provider now points at (legacy or tenant).
        MaybeSwitchToTenantDbAtStartup();

        EnsureDatabase();
        _services.GetRequiredService<MainWindow>().Show();
    }

    private void MaybeSwitchToTenantDbAtStartup()
    {
        var global = _services.GetRequiredService<Data.Repositories.GlobalSettingsRepository>();
        if (global.Get("tenant_db_runtime_enabled") != "1") return;

        var lastTenant = global.Get("last_tenant_subdomain");
        if (string.IsNullOrWhiteSpace(lastTenant)) return;

        var gate = _services.GetRequiredService<TenantCutoverReadinessGate>();
        // Block-on-async: the gate is the precondition for opening any
        // business DbContext; the rest of startup cannot proceed until we
        // know which DB to open. Acceptable cost — runs once per launch.
        var report = gate.CheckAsync(lastTenant).GetAwaiter().GetResult();

        if (!report.CanCutOver)
        {
            System.Diagnostics.Debug.WriteLine(
                "[Phase 10.5B] Runtime tenant DB mode requested but gate refused: " +
                string.Join("; ", report.Errors) +
                (report.Warnings.Count > 0 ? " | warnings: " + string.Join("; ", report.Warnings) : ""));
            return; // stay on legacy pos.db
        }

        var pathProvider = _services.GetRequiredService<Data.ILocalDatabasePathProvider>();
        pathProvider.UseTenantDatabase(lastTenant);

        System.Diagnostics.Debug.WriteLine(
            $"[Phase 10.5B] Switched to tenant DB: {pathProvider.CurrentDbPath} " +
            $"(status: {report.Status}, warnings: {report.Warnings.Count})");
    }

    private ServiceProvider BuildServices()
    {
        var sc = new ServiceCollection();

        // ── Database (factory = safe for singleton repos) ──────────────────────
        // Phase 10.1 seam: the DB path comes from ILocalDatabasePathProvider so
        // future phases (per-tenant DB, alternate locations) can swap it without
        // touching repository code or AppDbContext.
        // Phase 10.3A upgraded the provider to support tenant-mode switching;
        // Phase 10.3B replaces the default cached AddDbContextFactory with a
        // custom factory that re-reads the provider on every CreateDbContext()
        // so a tenant switch actually takes effect. Both layers remain
        // disabled by default — provider stays in legacy mode until a future
        // phase wires UseTenantDatabase into the login flow.
        sc.AddSingleton<Data.ILocalDatabasePathProvider, Data.TenantAwareLocalDatabasePathProvider>();
        sc.AddSingleton<IDbContextFactory<AppDbContext>, Data.TenantAwareDbContextFactory>();

        // ── Repositories (singleton: use IDbContextFactory internally) ─────────
        sc.AddSingleton<GlobalSettingsRepository>();
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
            return new ApiClient(
                http,
                sp.GetRequiredService<SettingsRepository>(),
                sp.GetRequiredService<GlobalSettingsRepository>());
        });

        sc.AddSingleton<AuthService>();
        sc.AddSingleton<SyncService>();
        sc.AddSingleton<TenantScopeService>();
        sc.AddSingleton<SharedToTenantMigrationAuditor>();
        sc.AddSingleton<MigrationAuditLogger>();
        sc.AddSingleton<SharedToTenantMigrationVerifier>();
        sc.AddSingleton<SharedToTenantDatabaseMigrator>();
        sc.AddSingleton<MigrationDryRunPreviewService>();
        sc.AddSingleton<TenantCutoverReadinessGate>();
        sc.AddSingleton<TenantDbRollbackReadinessChecker>();
        sc.AddSingleton<TenantDbRollbackExecutor>();
        sc.AddSingleton<RollbackDryRunPreviewService>();
        sc.AddSingleton<MigrationOperationsPreflightExportService>();
        sc.AddSingleton<TenantDatabaseInventoryService>();
        sc.AddSingleton<TenantDatabaseRetentionPreviewService>();
        sc.AddSingleton<TenantDatabaseInventoryExportService>();
        sc.AddSingleton<OperatorDiagnosticsService>();
        sc.AddSingleton<OperatorDiagnosticsExportService>();
        sc.AddSingleton<OperatorAccessService>();

        // ── ViewModels (transient: fresh per navigation) ───────────────────────
        sc.AddTransient<LoginViewModel>();
        sc.AddTransient<GameViewModel>();
        sc.AddTransient<ProductsViewModel>();
        sc.AddTransient<OmborViewModel>();
        sc.AddTransient<AddProductViewModel>(sp => new AddProductViewModel(
            sp.GetRequiredService<ApiClient>(),
            sp.GetRequiredService<PriceListRepository>(),
            sp.GetRequiredService<ProductTypeRepository>()));
        sc.AddTransient<ShiftViewModel>();
        sc.AddTransient<PosViewModel>();

        // ── Views ─────────────────────────────────────────────────────────────
        sc.AddTransient<OperatorDiagnosticsViewModel>();
        sc.AddTransient<Views.OperatorDiagnosticsWindow>();

        sc.AddTransient<MigrationOperationsViewModel>();
        sc.AddTransient<Views.MigrationOperationsWindow>();

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

        // EF Core Migrations replace the previous EnsureCreated + ad-hoc
        // ALTER TABLE pattern. Existing installations (where the DB was created
        // by EnsureCreated and patched manually) are detected and baselined as
        // "InitialCreate already applied" so Migrate() doesn't try to recreate
        // existing tables on first run after upgrade.
        BaselineExistingDatabaseIfNeeded(db);
        db.Database.Migrate();

        // One-time data cleanup of legacy seed rows from older builds. These are
        // data deletions, not schema changes, so they stay outside migrations.
        CleanupLegacySeedRows(db);

        // Phase 9: upgrade any plaintext auth/refresh tokens left by older
        // builds to DPAPI-encrypted form. No-op when values are absent or
        // already encrypted.
        var settings = _services.GetRequiredService<Data.Repositories.SettingsRepository>();
        settings.EncryptIfLegacy("auth_token");
        settings.EncryptIfLegacy("refresh_token");

        // Phase 10.2: one-time copy of machine-level settings from shared pos.db
        // into the new global JSON store. Idempotent — only fills keys the
        // global store doesn't already have. Old rows remain in the Settings
        // table as a rollback fallback.
        var globalSettings = _services.GetRequiredService<Data.Repositories.GlobalSettingsRepository>();
        MigrateLegacyGlobalSettings(settings, globalSettings);

        // Seed demo data only when there is no authenticated session.
        // Once the user logs in and syncs, real backend data replaces the demo rows.
        var auth = _services.GetRequiredService<AuthService>();
        if (!auth.HasValidSession())
            SeedDemoData(db);
    }

    // Bridges the EnsureCreated era to the migrations era. If the database was
    // built by EnsureCreated (no __EFMigrationsHistory table) but already has
    // the business tables, we:
    //  1. Repair any column/table gaps left over from older app versions that
    //     never ran the full ApplySchemaMigrations pipeline. Without this, an
    //     old DB lacking e.g. Sales.TenantSubdomain would be stamped as
    //     "InitialCreate applied" and any later EF query referencing the column
    //     would crash at runtime.
    //  2. Refuse the baseline if a core table is missing entirely — the DB is
    //     too damaged to salvage automatically; manual restore is safer than
    //     silently creating empty tables.
    //  3. Create __EFMigrationsHistory and stamp InitialCreate as applied.
    private static void BaselineExistingDatabaseIfNeeded(AppDbContext db)
    {
        if (TableExists(db, "__EFMigrationsHistory")) return;
        if (!TableExists(db, "Products")) return; // truly fresh install — let Migrate run

        // Refuse the baseline path when the DB is structurally broken — a
        // missing core table indicates corruption or a hostile downgrade, not a
        // legitimate upgrade we can recover from with ADD COLUMN.
        EnsureCoreTablesPresent(db);

        // Additive repair: add any post-original columns/tables that older
        // builds may not have created.
        RepairAdditiveSchemaGaps(db);

        db.Database.ExecuteSqlRaw(
            "CREATE TABLE \"__EFMigrationsHistory\" (" +
            "\"MigrationId\" TEXT NOT NULL CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY, " +
            "\"ProductVersion\" TEXT NOT NULL)");

        // Stamp the InitialCreate migration as already applied. The migration ID
        // must exactly match the generated file name.
        db.Database.ExecuteSqlRaw(
            "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") " +
            "VALUES ('20260522070942_InitialCreate', '8.0.11')");
    }

    // Tables present in the very first schema. If any of these is missing on a
    // DB that already has `Products`, the file is corrupted — bail loudly
    // rather than letting Migrate silently leave the app half-working.
    private static readonly string[] CoreTables =
        { "Categories", "Products", "Customers", "Sales", "SaleItems", "Settings" };

    private static void EnsureCoreTablesPresent(AppDbContext db)
    {
        var missing = new List<string>();
        foreach (var t in CoreTables)
            if (!TableExists(db, t)) missing.Add(t);

        if (missing.Count == 0) return;

        throw new InvalidOperationException(
            "Mahalliy ma'lumotlar bazasi zararlangan — quyidagi asosiy jadvallar yo'q: "
            + string.Join(", ", missing)
            + ". Iltimos, oxirgi to'g'ri zaxiradan tiklang yoki ushbu pos.db faylini o'chiring "
            + "(har qanday sinxronlanmagan sotuv yo'qoladi). "
            + "(Local database is damaged — core tables missing: "
            + string.Join(", ", missing)
            + ". Restore from a known-good backup, or delete pos.db (will lose unsynced sales).)");
    }

    // Columns/tables added by the historical ApplySchemaMigrations pipeline.
    // Old installations that never ran those exact ALTER TABLEs would otherwise
    // be stamped as "InitialCreate applied" and crash at the first EF query.
    private static readonly (string Table, string Column, string AddColumnSql)[] AdditiveColumns =
    {
        ("Products",  "RemoteUuid",         "ALTER TABLE Products  ADD COLUMN RemoteUuid         TEXT NOT NULL DEFAULT ''"),
        ("Customers", "RemoteUuid",         "ALTER TABLE Customers ADD COLUMN RemoteUuid         TEXT NOT NULL DEFAULT ''"),
        ("Customers", "IsActive",           "ALTER TABLE Customers ADD COLUMN IsActive           INTEGER NOT NULL DEFAULT 1"),
        ("Sales",     "ServerUuid",         "ALTER TABLE Sales     ADD COLUMN ServerUuid         TEXT"),
        ("Sales",     "CustomerRemoteUuid", "ALTER TABLE Sales     ADD COLUMN CustomerRemoteUuid TEXT NOT NULL DEFAULT ''"),
        ("Sales",     "TenantSubdomain",    "ALTER TABLE Sales     ADD COLUMN TenantSubdomain    TEXT NOT NULL DEFAULT ''"),
        ("Sales",     "CashAmount",         "ALTER TABLE Sales     ADD COLUMN CashAmount         NUMERIC NOT NULL DEFAULT 0"),
        ("Sales",     "CardAmount",         "ALTER TABLE Sales     ADD COLUMN CardAmount         NUMERIC NOT NULL DEFAULT 0"),
        ("Sales",     "BankAmount",         "ALTER TABLE Sales     ADD COLUMN BankAmount         NUMERIC NOT NULL DEFAULT 0"),
        ("Sales",     "DebtAmount",         "ALTER TABLE Sales     ADD COLUMN DebtAmount         NUMERIC NOT NULL DEFAULT 0"),
        ("Sales",     "RetryCount",         "ALTER TABLE Sales     ADD COLUMN RetryCount         INTEGER NOT NULL DEFAULT 0"),
        ("Sales",     "NextRetryAt",        "ALTER TABLE Sales     ADD COLUMN NextRetryAt        TEXT"),
        ("Sales",     "LastSyncError",      "ALTER TABLE Sales     ADD COLUMN LastSyncError      TEXT NOT NULL DEFAULT ''"),
        ("Sales",     "IsPoison",           "ALTER TABLE Sales     ADD COLUMN IsPoison           INTEGER NOT NULL DEFAULT 0"),
        ("SaleItems", "ProductRemoteUuid",  "ALTER TABLE SaleItems ADD COLUMN ProductRemoteUuid  TEXT NOT NULL DEFAULT ''"),
    };

    private static void RepairAdditiveSchemaGaps(AppDbContext db)
    {
        // Late-added tables (added by ApplySchemaMigrations in prior builds).
        if (!TableExists(db, "PriceLists"))
            db.Database.ExecuteSqlRaw(
                "CREATE TABLE PriceLists (" +
                "Id INTEGER PRIMARY KEY, Name TEXT NOT NULL DEFAULT '', " +
                "Currency TEXT NOT NULL DEFAULT '', CurrencyId INTEGER NOT NULL DEFAULT 1, " +
                "Active INTEGER NOT NULL DEFAULT 1)");

        if (!TableExists(db, "ProductTypes"))
            db.Database.ExecuteSqlRaw(
                "CREATE TABLE ProductTypes (" +
                "Id INTEGER PRIMARY KEY, Name TEXT NOT NULL DEFAULT '', " +
                "Active INTEGER NOT NULL DEFAULT 1)");

        // Additive columns. Each is a no-op if already present.
        foreach (var (table, column, addSql) in AdditiveColumns)
        {
            if (!ColumnExists(db, table, column))
                db.Database.ExecuteSqlRaw(addSql);
        }
    }

    private static bool TableExists(AppDbContext db, string tableName)
    {
        using var cmd = db.Database.GetDbConnection().CreateCommand();
        if (cmd.Connection!.State != System.Data.ConnectionState.Open) cmd.Connection.Open();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=@n";
        var p = cmd.CreateParameter();
        p.ParameterName = "@n";
        p.Value = tableName;
        cmd.Parameters.Add(p);
        var result = cmd.ExecuteScalar();
        return result is not null && result is not DBNull;
    }

    private static bool ColumnExists(AppDbContext db, string table, string column)
    {
        using var cmd = db.Database.GetDbConnection().CreateCommand();
        if (cmd.Connection!.State != System.Data.ConnectionState.Open) cmd.Connection.Open();
        // PRAGMA does not bind parameters — table name is whitelisted in AdditiveColumns.
        cmd.CommandText = $"PRAGMA table_info(\"{table}\")";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            // PRAGMA table_info columns: cid, name, type, notnull, dflt_value, pk
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void MigrateLegacyGlobalSettings(
        Data.Repositories.SettingsRepository settings,
        Data.Repositories.GlobalSettingsRepository global)
    {
        string[] machineKeys =
            { "api_base_url", "tablet_mode", "ui_scale", "receipt_printer", "label_printer" };

        foreach (var key in machineKeys)
        {
            if (global.ContainsKey(key)) continue;
            var value = settings.Get(key);
            if (!string.IsNullOrEmpty(value)) global.Set(key, value);
        }

        // Backfill the new "last-used tenant for login prefill" key from the
        // current session, if any. After this, the login screen survives a
        // logout and still prefills the cashier's tenant.
        if (!global.ContainsKey("last_tenant_subdomain"))
        {
            var current = settings.Get("tenant_subdomain");
            if (!string.IsNullOrEmpty(current)) global.Set("last_tenant_subdomain", current);
        }
    }

    private static void CleanupLegacySeedRows(AppDbContext db)
    {
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
