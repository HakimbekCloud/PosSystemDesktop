using System.IO;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PosSystem.Data;
using PosSystem.Data.Repositories;

namespace PosSystem.Services;

// ── Options ──────────────────────────────────────────────────────────────────

public sealed class SharedToTenantMigrationOptions
{
    // Default-safe: nothing on disk changes unless the caller explicitly opts in.
    public bool DryRunOnly { get; init; } = true;

    // Required for any real migration. Carries two semantics: (1) explicit
    // intent to write to disk, and (2) override of the global "already
    // migrated" marker so an operator can rerun after restoring pos.db from
    // backup. Real migration NEVER runs with Force=false.
    public bool Force { get; init; } = false;

    // Bypasses the global feature flag (shared_to_tenant_migration_enabled).
    // Intended for tooling / debugger / tests only. NOT sufficient on its own:
    // a real migration still requires Force=true. Setting this flag without
    // Force just keeps the migrator in Disabled state for safety.
    public bool AllowWhenFeatureDisabled { get; init; } = false;

    // Opt-in audit logging. When true, every MigrateAsync call writes a JSON
    // audit entry under
    //   %LocalAppData%\PosSystem\logs\migrations\
    // capturing the options, the result, and (if requested) a verification
    // report. Default is false because audit logs persist machine/user/tenant
    // metadata and migration structure — operators must intentionally opt in.
    // Any secret-shaped strings inside the payload (JWTs, DPAPI blobs) are
    // redacted by MigrationAuditLogger before the file is written.
    public bool WriteAuditLog { get; init; } = false;
}

// ── Result DTOs ──────────────────────────────────────────────────────────────

public enum SharedToTenantMigrationOutcome
{
    Disabled,          // real-migration guard rejected (missing Force, or
                       // feature flag closed and AllowWhenFeatureDisabled=false)
    NoOp,              // nothing to migrate (no source DB, no tenants, no orphans)
    AlreadyMigrated,   // global marker present and Force=false (work is already done)
    Success,           // migration (or dry-run) completed cleanly
    Failed             // exception, path collision, or other precondition violation.
                       // Cancellation propagates as OperationCanceledException and
                       // surfaces as Outcome=Failed via the catch block.
}

public enum TenantMigrationOutcome
{
    Skipped,           // dry run, or zero work for this tenant
    AlreadyMigrated,   // per-tenant marker present in target
    Migrated,          // successfully copied
    Failed             // exception during this tenant
}

public sealed class TenantMigrationResult
{
    public string Subdomain        { get; init; } = "";
    public string TargetDbPath     { get; init; } = "";
    public TenantMigrationOutcome Outcome { get; init; }
    public int    SalesCopied      { get; init; }
    public int    SaleItemsCopied  { get; init; }
    public int    SettingsCopied   { get; init; }
    public bool   CatalogCopied    { get; init; }
    public string? ErrorMessage    { get; init; }
}

public sealed class SharedToTenantMigrationResult
{
    public SharedToTenantMigrationOutcome Outcome { get; init; }
    public bool   DryRun           { get; init; }
    public string SourceDbPath     { get; init; } = "";
    public string? BackupPath      { get; init; }
    public string? BackupSha256    { get; init; }
    public string? FailureReason   { get; init; }
    public int    OrphanSalesQuarantined { get; init; }
    public System.Collections.Generic.IReadOnlyList<TenantMigrationResult> Tenants
        { get; init; } = System.Array.Empty<TenantMigrationResult>();
    public System.DateTime StartedAtUtc   { get; init; }
    public System.DateTime CompletedAtUtc { get; init; }

    // Set by MigrateAsync after the result is otherwise final, when
    // options.WriteAuditLog is true. Null when audit logging was disabled or
    // failed to write (failures during log write are non-fatal).
    public string? AuditLogPath    { get; set; }
}

// ── Migrator ─────────────────────────────────────────────────────────────────

public sealed class SharedToTenantDatabaseMigrator
{
    private const string FeatureFlagKey    = "shared_to_tenant_migration_enabled";
    private const string GlobalMarkerKey   = "shared_to_tenant_migrated_at";
    private const string TenantMarkerKey   = "migrated_from_shared_at";
    private const string OrphanSubdomain   = "_orphan";
    private const string OrphanQuarantineMessage =
        "Quarantined by Phase 10.4B migration — untagged legacy sale needs operator review.";

    // Explicit column lists protect against future schema reorderings. Any new
    // column added by a later EF migration must also be appended here before
    // running the migrator against an updated source DB.
    private const string SalesColumns =
        "Id, LocalId, ServerUuid, TenantSubdomain, CustomerRemoteUuid, CustomerId, CustomerName, " +
        "TotalAmount, Discount, PaidAmount, ChangeAmount, " +
        "CashAmount, CardAmount, BankAmount, DebtAmount, " +
        "PaymentType, Note, Synced, " +
        "RetryCount, NextRetryAt, LastSyncError, IsPoison, " +
        "CreatedAt, SyncedAt";

    private const string SaleItemsColumns =
        "Id, SaleLocalId, ProductId, ProductRemoteUuid, ProductName, ProductCode, " +
        "Unit, Price, Quantity, Discount, Total";

    private const string ProductsColumns =
        "Id, RemoteUuid, Name, Code, Barcode, Price, CostPrice, " +
        "CategoryId, CategoryName, Unit, Stock, IsActive, ImageUrl, UpdatedAt";

    private const string CustomersColumns =
        "Id, RemoteUuid, Name, Phone, Address, Balance, IsActive, UpdatedAt";

    private const string CategoriesColumns   = "Id, Name, UpdatedAt";
    private const string PriceListsColumns   = "Id, Name, Currency, CurrencyId, Active";
    private const string ProductTypesColumns = "Id, Name, Active";

    // For orphan sales we substitute these two columns with quarantine values so
    // they surface in the failed-sale UI and cannot be auto-synced.
    private const string SalesColumnsForOrphan =
        "Id, LocalId, ServerUuid, TenantSubdomain, CustomerRemoteUuid, CustomerId, CustomerName, " +
        "TotalAmount, Discount, PaidAmount, ChangeAmount, " +
        "CashAmount, CardAmount, BankAmount, DebtAmount, " +
        "PaymentType, Note, Synced, " +
        "RetryCount, NextRetryAt, " +
        "@orphanMsg AS LastSyncError, 1 AS IsPoison, " +
        "CreatedAt, SyncedAt";

    private readonly ILocalDatabasePathProvider _pathProvider;
    private readonly GlobalSettingsRepository   _global;
    private readonly SettingsRepository         _legacySettings;
    private readonly SharedToTenantMigrationAuditor _auditor;
    private readonly SyncService                _sync;
    private readonly MigrationAuditLogger       _auditLogger;

    public SharedToTenantDatabaseMigrator(
        ILocalDatabasePathProvider pathProvider,
        GlobalSettingsRepository global,
        SettingsRepository legacySettings,
        SharedToTenantMigrationAuditor auditor,
        SyncService sync,
        MigrationAuditLogger auditLogger)
    {
        _pathProvider   = pathProvider;
        _global         = global;
        _legacySettings = legacySettings;
        _auditor        = auditor;
        _sync           = sync;
        _auditLogger    = auditLogger;
    }

    private SharedToTenantMigrationResult WithAuditLog(
        SharedToTenantMigrationOptions options,
        SharedToTenantMigrationResult result)
    {
        if (!options.WriteAuditLog) return result;
        try
        {
            result.AuditLogPath = _auditLogger.Write(options, result, verification: null);
        }
        catch
        {
            // Audit-log failure must not turn a successful migration into a
            // failed one. Swallow; AuditLogPath stays null in the result.
        }
        return result;
    }

    public async System.Threading.Tasks.Task<SharedToTenantMigrationResult> MigrateAsync(
        SharedToTenantMigrationOptions options,
        System.Threading.CancellationToken ct = default)
    {
        if (options is null) throw new System.ArgumentNullException(nameof(options));

        var started = System.DateTime.UtcNow;
        var sourcePath = _pathProvider.GetLegacyDbPath();

        // Audit runs unconditionally so we can compute a plan (dry-run) or
        // detect "nothing to migrate" before any gate decision.
        var report = await _auditor.AnalyzeAsync(ct);
        if (!report.SourceDbExists || (report.Tenants.Count == 0 && report.UntaggedSalesCount == 0))
            return WithAuditLog(options, Done(started, sourcePath, SharedToTenantMigrationOutcome.NoOp,
                dryRun: options.DryRunOnly,
                failureReason: report.SourceDbExists
                    ? "No tenants and no orphan sales in shared pos.db."
                    : "Source pos.db does not exist."));

        // Plan tenants + orphan slot.
        var lastUsed = (_global.Get("last_tenant_subdomain") ?? "").Trim();
        var plan = new System.Collections.Generic.List<string>(
            report.Tenants.Select(t => t.Subdomain));
        if (report.UntaggedSalesCount > 0) plan.Add(OrphanSubdomain);

        // Path-collision detection — distinct subdomain inputs that sanitize to
        // the same on-disk directory would silently merge data. Runs even for
        // dry-runs so operators see the structural problem early.
        try { DetectPathCollisions(plan); }
        catch (System.Exception ex)
        {
            return WithAuditLog(options, Done(started, sourcePath, SharedToTenantMigrationOutcome.Failed,
                dryRun: options.DryRunOnly, failureReason: ex.Message));
        }

        // ── DRY-RUN — bypasses all guards, never touches the filesystem ──────
        //
        // Dry-run is always allowed regardless of feature flag, Force, or
        // marker state. It exists exactly so operators can inspect what WOULD
        // happen without making any commitment.
        if (options.DryRunOnly)
        {
            var tenantPlan = new System.Collections.Generic.List<TenantMigrationResult>();
            foreach (var sub in plan)
            {
                var planned = report.Tenants.FirstOrDefault(x =>
                    string.Equals(x.Subdomain, sub, System.StringComparison.OrdinalIgnoreCase));
                tenantPlan.Add(new TenantMigrationResult
                {
                    Subdomain     = sub,
                    TargetDbPath  = _pathProvider.GetTenantDbPath(sub),
                    Outcome       = TenantMigrationOutcome.Skipped,
                    SalesCopied   = sub == OrphanSubdomain ? report.UntaggedSalesCount : (planned?.TotalSales ?? 0),
                    CatalogCopied = string.Equals(sub, lastUsed, System.StringComparison.OrdinalIgnoreCase),
                });
            }

            return WithAuditLog(options, new SharedToTenantMigrationResult
            {
                Outcome                = SharedToTenantMigrationOutcome.Success,
                DryRun                 = true,
                SourceDbPath           = sourcePath,
                BackupPath             = null,
                BackupSha256           = null,
                Tenants                = tenantPlan,
                OrphanSalesQuarantined = report.UntaggedSalesCount,
                StartedAtUtc           = started,
                CompletedAtUtc         = System.DateTime.UtcNow,
            });
        }

        // ── REAL-RUN GUARDS (strict) ─────────────────────────────────────────
        //
        // Real migration must clear THREE independent gates in order:
        //
        //   (1) Global marker absent, OR Force=true. (Marker-present without
        //       Force returns AlreadyMigrated — distinct from Disabled to
        //       inform the operator that the work is already done.)
        //   (2) Force = true. (Plain Force=false is rejected as Disabled —
        //       "I might be running a real migration by accident".)
        //   (3) Feature flag enabled in global_settings.json, OR
        //       AllowWhenFeatureDisabled = true. (Flag closed + no override =
        //       Disabled.)
        //
        // AllowWhenFeatureDisabled=true alone is NOT enough. Force=true alone
        // (without flag or override) is NOT enough. Real migration requires
        // intentional double confirmation.

        if (!options.Force && !string.IsNullOrEmpty(_global.Get(GlobalMarkerKey)))
            return WithAuditLog(options, Done(started, sourcePath, SharedToTenantMigrationOutcome.AlreadyMigrated,
                dryRun: false,
                failureReason: "Global marker present. Pass Force=true to rerun."));

        if (!options.Force)
            return WithAuditLog(options, Done(started, sourcePath, SharedToTenantMigrationOutcome.Disabled,
                dryRun: false,
                failureReason: "Real tenant DB migration requires Force=true."));

        var enabled = _global.Get(FeatureFlagKey) == "1";
        if (!enabled && !options.AllowWhenFeatureDisabled)
            return WithAuditLog(options, Done(started, sourcePath, SharedToTenantMigrationOutcome.Disabled,
                dryRun: false,
                failureReason: "Tenant DB migration is disabled. Set shared_to_tenant_migration_enabled=1 " +
                               "or pass AllowWhenFeatureDisabled=true for test tooling."));

        // ── REAL RUN — drains sync, backs up, copies, marks ─────────────────
        var tenants = new System.Collections.Generic.List<TenantMigrationResult>();
        string? backupPath = null;
        string? backupHash = null;
        int orphanCount = 0;
        string? failure = null;
        SharedToTenantMigrationOutcome outcome = SharedToTenantMigrationOutcome.Failed;

        bool resumeBackground = _sync.IsBackgroundRunning;
        await _sync.PauseAsync();
        try
        {
            (backupPath, backupHash) = BackupSource(sourcePath);

            foreach (var sub in plan)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var r = MigrateSingleTenant(sourcePath, sub, lastUsed);
                    tenants.Add(r);
                    if (sub == OrphanSubdomain) orphanCount = r.SalesCopied;
                }
                catch (System.Exception ex)
                {
                    tenants.Add(new TenantMigrationResult
                    {
                        Subdomain    = sub,
                        TargetDbPath = _pathProvider.GetTenantDbPath(sub),
                        Outcome      = TenantMigrationOutcome.Failed,
                        ErrorMessage = ex.Message,
                    });
                    throw;
                }
            }

            if (tenants.All(t => t.Outcome != TenantMigrationOutcome.Failed))
            {
                _global.Set(GlobalMarkerKey, System.DateTime.UtcNow.ToString("O"));
                outcome = SharedToTenantMigrationOutcome.Success;
            }
        }
        catch (System.Exception ex)
        {
            failure = ex.Message;
            outcome = SharedToTenantMigrationOutcome.Failed;
        }
        finally
        {
            _sync.Resume(resumeBackground);
        }

        return WithAuditLog(options, new SharedToTenantMigrationResult
        {
            Outcome                = outcome,
            DryRun                 = false,
            SourceDbPath           = sourcePath,
            BackupPath             = backupPath,
            BackupSha256           = backupHash,
            FailureReason          = failure,
            OrphanSalesQuarantined = orphanCount,
            Tenants                = tenants,
            StartedAtUtc           = started,
            CompletedAtUtc         = System.DateTime.UtcNow,
        });
    }

    // ── Path collision detection ─────────────────────────────────────────────

    private void DetectPathCollisions(System.Collections.Generic.IEnumerable<string> tenants)
    {
        var seen = new System.Collections.Generic.Dictionary<string, string>(
            System.StringComparer.OrdinalIgnoreCase);
        foreach (var t in tenants)
        {
            var path = _pathProvider.GetTenantDbPath(t);
            if (seen.TryGetValue(path, out var first) &&
                !string.Equals(first, t, System.StringComparison.OrdinalIgnoreCase))
                throw new System.InvalidOperationException(
                    $"Tenant directory collision: '{first}' and '{t}' both resolve to {path}. " +
                    "Resolve the naming conflict in source data before migrating.");
            seen[path] = t;
        }
    }

    // ── Backup (organized under …\PosSystem\backups\) ────────────────────────

    private static (string Path, string Sha256) BackupSource(string sourcePath)
    {
        var backupDir = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "PosSystem", "backups");
        Directory.CreateDirectory(backupDir);

        var stamp = System.DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var backupPath = Path.Combine(backupDir, $"pos.db.backup-{stamp}.legacy");
        File.Copy(sourcePath, backupPath, overwrite: false);

        var srcHash = Hash(sourcePath);
        var bakHash = Hash(backupPath);
        if (!string.Equals(srcHash, bakHash, System.StringComparison.Ordinal))
            throw new System.IO.IOException(
                $"Backup integrity check failed (source {srcHash} != backup {bakHash}).");

        return (backupPath, bakHash);
    }

    private static string Hash(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        return System.Convert.ToHexString(sha.ComputeHash(fs));
    }

    // ── Per-tenant copy ──────────────────────────────────────────────────────

    private TenantMigrationResult MigrateSingleTenant(string sourcePath, string subdomain, string lastUsed)
    {
        var targetPath = _pathProvider.GetTenantDbPath(subdomain);
        var targetDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);

        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={targetPath}")
            .Options;
        using (var targetDb = new AppDbContext(opts))
        {
            targetDb.Database.Migrate();
        }

        // Per-tenant idempotency.
        using (var probe = new SqliteConnection($"Data Source={targetPath}"))
        {
            probe.Open();
            using var cmd = probe.CreateCommand();
            cmd.CommandText = "SELECT Value FROM Settings WHERE Key = @k";
            cmd.Parameters.AddWithValue("@k", TenantMarkerKey);
            var existing = cmd.ExecuteScalar() as string;
            if (!string.IsNullOrEmpty(existing))
                return new TenantMigrationResult
                {
                    Subdomain    = subdomain,
                    TargetDbPath = targetPath,
                    Outcome      = TenantMigrationOutcome.AlreadyMigrated,
                };
        }

        bool isLastUsed = string.Equals(subdomain, lastUsed, System.StringComparison.OrdinalIgnoreCase);
        bool isOrphan   = subdomain == OrphanSubdomain;

        int sales = 0, items = 0, settings = 0;
        bool catalog = false;

        using var conn = new SqliteConnection($"Data Source={targetPath}");
        conn.Open();

        using (var attach = conn.CreateCommand())
        {
            attach.CommandText = $"ATTACH DATABASE '{sourcePath.Replace("'", "''")}' AS shared";
            attach.ExecuteNonQuery();
        }

        using var tx = conn.BeginTransaction();
        try
        {
            if (isOrphan)
            {
                sales = ExecNonQuery(conn, tx,
                    $"INSERT OR IGNORE INTO main.Sales ({SalesColumns}) " +
                    $"SELECT {SalesColumnsForOrphan} FROM shared.Sales WHERE TenantSubdomain = ''",
                    ("@orphanMsg", OrphanQuarantineMessage));
            }
            else
            {
                sales = ExecNonQuery(conn, tx,
                    $"INSERT OR IGNORE INTO main.Sales ({SalesColumns}) " +
                    $"SELECT {SalesColumns} FROM shared.Sales WHERE TenantSubdomain = @sub",
                    ("@sub", subdomain));
            }

            items = ExecNonQuery(conn, tx,
                $"INSERT OR IGNORE INTO main.SaleItems ({SaleItemsColumns}) " +
                $"SELECT {SaleItemsColumns} FROM shared.SaleItems " +
                "WHERE SaleLocalId IN (SELECT LocalId FROM main.Sales)");

            if (isLastUsed)
            {
                ExecNonQuery(conn, tx, $"INSERT OR IGNORE INTO main.Products     ({ProductsColumns})     SELECT {ProductsColumns}     FROM shared.Products");
                ExecNonQuery(conn, tx, $"INSERT OR IGNORE INTO main.Customers    ({CustomersColumns})    SELECT {CustomersColumns}    FROM shared.Customers");
                ExecNonQuery(conn, tx, $"INSERT OR IGNORE INTO main.Categories   ({CategoriesColumns})   SELECT {CategoriesColumns}   FROM shared.Categories");
                ExecNonQuery(conn, tx, $"INSERT OR IGNORE INTO main.PriceLists   ({PriceListsColumns})   SELECT {PriceListsColumns}   FROM shared.PriceLists");
                ExecNonQuery(conn, tx, $"INSERT OR IGNORE INTO main.ProductTypes ({ProductTypesColumns}) SELECT {ProductTypesColumns} FROM shared.ProductTypes");
                catalog = true;
            }

            // Tenant-suffixed settings → strip suffix.
            foreach (var prefix in new[]
            {
                "bootstrap_completed_at", "last_product_sync_at",
                "last_customer_sync_at", "last_stock_reconcile_at"
            })
            {
                settings += ExecNonQuery(conn, tx,
                    "INSERT OR REPLACE INTO main.Settings (Key, Value) " +
                    $"SELECT '{prefix}', Value FROM shared.Settings WHERE Key = @k",
                    ("@k", $"{prefix}:{subdomain}"));
            }

            if (isLastUsed)
            {
                string[] sessionKeys =
                {
                    "auth_token", "refresh_token",
                    "user_name", "user_id", "tenant_name", "tenant_subdomain",
                    "default_branch_uuid", "default_cashbox_uuid",
                    "default_currency_id", "default_price_list_id",
                    "cashbox_uuid_cash", "cashbox_uuid_card", "cashbox_uuid_bank",
                    "last_sync_at",
                };
                foreach (var k in sessionKeys)
                {
                    settings += ExecNonQuery(conn, tx,
                        "INSERT OR REPLACE INTO main.Settings (Key, Value) " +
                        "SELECT Key, Value FROM shared.Settings WHERE Key = @k",
                        ("@k", k));
                }
            }

            ExecNonQuery(conn, tx,
                "INSERT OR REPLACE INTO main.Settings (Key, Value) VALUES (@k, @v)",
                ("@k", TenantMarkerKey), ("@v", System.DateTime.UtcNow.ToString("O")));

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
        finally
        {
            using var detach = conn.CreateCommand();
            detach.CommandText = "DETACH DATABASE shared";
            detach.ExecuteNonQuery();
        }

        return new TenantMigrationResult
        {
            Subdomain       = subdomain,
            TargetDbPath    = targetPath,
            Outcome         = TenantMigrationOutcome.Migrated,
            SalesCopied     = sales,
            SaleItemsCopied = items,
            SettingsCopied  = settings,
            CatalogCopied   = catalog,
        };
    }

    private static int ExecNonQuery(SqliteConnection conn, SqliteTransaction tx,
        string sql, params (string Name, object Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (n, v) in parameters)
            cmd.Parameters.AddWithValue(n, v ?? System.DBNull.Value);
        return cmd.ExecuteNonQuery();
    }

    private static SharedToTenantMigrationResult Done(
        System.DateTime started, string sourcePath,
        SharedToTenantMigrationOutcome outcome, bool dryRun, string? failureReason)
        => new()
        {
            Outcome        = outcome,
            DryRun         = dryRun,
            SourceDbPath   = sourcePath,
            FailureReason  = failureReason,
            StartedAtUtc   = started,
            CompletedAtUtc = System.DateTime.UtcNow,
        };
}
