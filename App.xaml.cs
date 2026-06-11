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
        // M5: the gate is the precondition for opening any business DbContext;
        // the rest of startup cannot proceed until we know which DB to open, so
        // we must block on it (runs once per launch). Running the async chain
        // directly via GetAwaiter().GetResult() on the WPF UI thread is fragile
        // sync-over-async: CheckAsync awaits EF/IO that, without ConfigureAwait,
        // would try to resume on the captured WPF SynchronizationContext that is
        // currently blocked here → deadlock risk. Task.Run hops the whole chain
        // onto a thread-pool thread with no captured UI context, so the
        // continuations never contend for this thread. (ConfigureAwait(false) is
        // also applied inside the gate as defense in depth.)
        var report = System.Threading.Tasks.Task
            .Run(() => gate.CheckAsync(lastTenant))
            .GetAwaiter().GetResult();

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
            // Handler chain (outermost first): per-request tenant/auth header
            // stamping (Bug H1) → network logging → real socket handler.
            var logHandler  = new NetworkLogHandler(sp.GetRequiredService<NetworkLogService>());
            var authHandler = new TenantAuthHeaderHandler(sp.GetRequiredService<SettingsRepository>())
            {
                InnerHandler = logHandler
            };
            var http = new HttpClient(authHandler) { Timeout = TimeSpan.FromSeconds(15) };
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
        sc.AddSingleton<RealMigrationExecutionGateService>();
        sc.AddSingleton<GuardedRealMigrationExecutorService>();
        sc.AddSingleton<GuardedRuntimeCutoverExecutorService>();
        sc.AddSingleton<GuardedRollbackExecutorService>();
        sc.AddSingleton<GuardedRetentionCleanupExecutorService>();
        sc.AddSingleton<ProductionPilotReadinessReportService>();
        sc.AddSingleton<ProductionPilotEvidenceBundleService>();
        sc.AddSingleton<OperatorPermissionApiClient>();
        sc.AddSingleton<OperatorAuditEvidenceApiClient>();
        sc.AddSingleton<OperatorAuditReviewApiClient>();
        sc.AddSingleton<OperatorPermissionAdminApiClient>();
        sc.AddSingleton<OperatorPermissionAdminMutationApiClient>();

        // Phase 10.22E — desktop-side evidence bundle local export
        // pipeline. NONE of these services call any backend endpoint.
        // The export pipeline is gated by the local
        // `operator_evidence_bundle_export_ui_enabled` flag (default OFF).
        sc.AddSingleton<Services.EvidenceBundleExport.EvidenceBundleRedactionScanner>();
        sc.AddSingleton<Services.EvidenceBundleExport.EvidenceBundleManifestGenerator>();
        sc.AddSingleton<Services.EvidenceBundleExport.EvidenceBundleZipWriter>();
        sc.AddSingleton<Services.EvidenceBundleExport.EvidenceBundleExportService>();

        // Phase 10.22F — desktop-side backend upload + finalize pipeline.
        // Gated by the local `operator_evidence_bundle_upload_ui_enabled`
        // flag (default OFF). Calls the Phase 10.22C/D backend endpoints
        // only when the flag is ON.
        sc.AddSingleton<OperatorEvidenceBundleApiClient>();
        sc.AddSingleton<Services.EvidenceBundleUpload.EvidenceBundleUploadService>();

        // Phase 10.22G — desktop-side reviewer + download pipeline.
        // Gated by the local `operator_evidence_bundle_review_ui_enabled`
        // flag (default OFF). Consumes the Phase 10.22G backend review
        // + download endpoints.
        sc.AddSingleton<Services.EvidenceBundleReview.EvidenceBundleReviewService>();

        // Phase 10.22H — desktop-side retention + legal hold UI surface.
        // Gated by the local `operator_evidence_bundle_retention_ui_enabled`
        // flag (default OFF). Consumes the Phase 10.22H backend retention /
        // legal-hold / archive / expire / retention-candidates endpoints.
        // The card has NO upload / finalize / delete / hard-delete /
        // dangerous-execute / confirmation-phrase / storage-path control.
        sc.AddSingleton<Services.EvidenceBundleRetention.EvidenceBundleRetentionService>();

        // Phase 10.22P — desktop-side read-only lifecycle scheduler status UI.
        // Gated by `operator_evidence_bundle_lifecycle_scheduler_status_ui_enabled`
        // (default OFF). Consumes Phase 10.22N retention-sweeper and Phase 10.22O
        // expiration-sweeper run-history endpoints. NO hard-delete, NO storage
        // object deletion, NO dangerous operation.
        sc.AddSingleton<Services.EvidenceBundleLifecycleScheduler.EvidenceBundleLifecycleSchedulerStatusService>();
        sc.AddSingleton<BackendOperatorPermissionSnapshotService>();
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
        sc.AddTransient<ViewModels.Ombor.InventoryAdjustmentViewModel>();
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

        // No demo seed: the POS catalog stays empty until the first successful
        // login + sync, which is correct. Demo rows could otherwise be sold
        // before sync, producing LOCAL_ONLY sales that can never reach the server.
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

    // One-time TARGETED cleanup: remove ONLY the known demo seed rows that older
    // builds inserted (RemoteUuid = '' AND a code/phone from the exact seed set).
    // A blanket "DELETE ... WHERE RemoteUuid = ''" would be a latent trap: it
    // would silently destroy any future locally-created row whose server UUID
    // failed to persist. Any OTHER empty-RemoteUuid row must SURVIVE startup.
    // Settings key marking the one-time legacy-seed cleanup as already run on
    // THIS database file. Settings live per-DB, so in tenant-DB mode each file
    // gets its own one-time run — the correct behavior.
    private const string LegacySeedCleanupDoneKey = "legacy_seed_cleanup_done";

    private static void CleanupLegacySeedRows(AppDbContext db)
    {
        // H3 fix: this whole block (demo DELETEs + the LOCAL_ONLY retire UPDATE)
        // must run exactly once per DB. The UPDATE in particular force-"syncs"
        // any unsynced sale whose items all lack a ProductRemoteUuid; re-armed on
        // every boot it could silently retire a future legitimately-unsynced sale
        // (e.g. a crashed partial write) — irreversible revenue loss. Guard with a
        // marker in the Settings table: run once, stamp it, skip forever after.
        // Plain SQL against the same db context, consistent with this method's
        // style and usable here before DI repositories are wired.
        if (LegacySeedCleanupAlreadyDone(db)) return;

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

        // Retire legacy LOCAL_ONLY sales (demo-seed era: no item ever had a server
        // UUID, so they can never be sent). Mark them with the terminal LOCAL_ONLY
        // state so the pending counter doesn't stay inflated forever and the sale
        // history shows them as local-only. New such sales can't occur — products
        // without a server UUID are no longer sellable.
        try { db.Database.ExecuteSqlRaw(
            "UPDATE Sales SET Synced = 1, ServerUuid = 'LOCAL_ONLY' " +
            "WHERE Synced = 0 AND LocalId NOT IN (" +
            "SELECT DISTINCT SaleLocalId FROM SaleItems WHERE ProductRemoteUuid <> '')"); }
        catch { }

        // Stamp the marker so this destructive cleanup never runs again on this
        // DB. Done last: if any step above threw it was swallowed (idempotent
        // style), and an already-clean DB re-running once more is harmless — but
        // after this point we are guaranteed to skip.
        MarkLegacySeedCleanupDone(db);
    }

    private static bool LegacySeedCleanupAlreadyDone(AppDbContext db)
    {
        try
        {
            using var cmd = db.Database.GetDbConnection().CreateCommand();
            if (cmd.Connection!.State != System.Data.ConnectionState.Open) cmd.Connection.Open();
            cmd.CommandText = "SELECT Value FROM Settings WHERE Key = @k";
            var p = cmd.CreateParameter();
            p.ParameterName = "@k";
            p.Value = LegacySeedCleanupDoneKey;
            cmd.Parameters.Add(p);
            var result = cmd.ExecuteScalar();
            return result is string s && s == "1";
        }
        catch
        {
            // If we can't read the marker (e.g. Settings missing), fall through
            // to running the cleanup — it's idempotent and self-guards via try/catch.
            return false;
        }
    }

    private static void MarkLegacySeedCleanupDone(AppDbContext db)
    {
        try { db.Database.ExecuteSqlRaw(
            "INSERT INTO Settings (Key, Value) VALUES ({0}, '1') " +
            "ON CONFLICT(Key) DO UPDATE SET Value = '1'",
            LegacySeedCleanupDoneKey); }
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
