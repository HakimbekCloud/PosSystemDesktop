using System.IO;
using Microsoft.EntityFrameworkCore;
using PosSystem.Data;
using PosSystem.Data.Repositories;

namespace PosSystem.Services;

// ── Report DTOs ──────────────────────────────────────────────────────────────

public sealed class LocalSalesDiagnostics
{
    public int PendingSalesCount         { get; init; }
    public int PoisonSalesCount          { get; init; }
    public int FailedRetryableSalesCount { get; init; }
    public int SyncedSalesCount          { get; init; }
    public int TotalSalesCount           { get; init; }
    public string? ErrorMessage          { get; init; }
}

public sealed class LocalCacheDiagnostics
{
    public int  ProductsCount       { get; init; }
    public int  CustomersCount      { get; init; }
    public int  CategoriesCount     { get; init; }
    public int  PriceListsCount     { get; init; }
    public int  ProductTypesCount   { get; init; }

    public bool   BootstrapCompleted     { get; init; }
    public string? BootstrapCompletedAt  { get; init; }
    public string? LastProductSyncAt     { get; init; }
    public string? LastCustomerSyncAt    { get; init; }
    public string? LastStockReconcileAt  { get; init; }

    public string? ErrorMessage          { get; init; }
}

public sealed class OperatorDiagnosticsReport
{
    public System.DateTime CheckedAtUtc       { get; init; } = System.DateTime.UtcNow;

    public string ActiveDbPath                { get; init; } = "";
    public bool   IsTenantScoped              { get; init; }
    public string LegacyDbPath                { get; init; } = "";
    public string? CurrentTenantSubdomain     { get; init; }
    public string? RequestedTenantSubdomain   { get; init; }
    public string? SanitizedRequestedTenantSubdomain { get; init; }
    public string? TargetTenantDbPath         { get; init; }

    public bool   RuntimeTenantDbEnabled      { get; init; }
    public bool   MigrationFeatureEnabled     { get; init; }
    public bool   SharedToTenantMigrated      { get; init; }
    public string? SharedToTenantMigratedAt   { get; init; }

    public string? LastTenantSubdomain        { get; init; }
    public string? ApiBaseUrlConfigured       { get; init; }

    public TenantCutoverReadinessReport?       CutoverReadiness  { get; init; }
    public TenantDbRollbackReadinessReport     RollbackReadiness { get; init; } = new();

    public LocalSalesDiagnostics  Sales  { get; init; } = new();
    public LocalCacheDiagnostics  Cache  { get; init; } = new();

    public System.Collections.Generic.List<string> Warnings { get; init; } = new();
    public System.Collections.Generic.List<string> Errors   { get; init; } = new();
}

// ── Service ──────────────────────────────────────────────────────────────────

// Strictly read-only diagnostics aggregator. Compiles a snapshot of every
// operator-relevant subsystem (active DB, feature flags, migration state,
// cutover/rollback readiness, sales counts, cache counts, sync watermarks)
// in a single report. Phase 10.7A introduces the service in DI; no production
// flow invokes it. A future operator UI / CLI / support workflow is the only
// intended caller.
//
// Read-only contract:
//   • All DB access uses the standard IDbContextFactory<AppDbContext>, which
//     respects the current path provider state. Only Count() / single-row
//     selects are issued; never any UPDATE / DELETE / INSERT / CREATE.
//   • Cutover gate and rollback checker are invoked but they themselves are
//     read-only (Phase 10.5A.1 / 10.6A.1 contracts).
//   • Per-subsection failures land in OperatorDiagnosticsReport.Errors; the
//     diagnostics never throw to callers.
public sealed class OperatorDiagnosticsService
{
    private readonly ILocalDatabasePathProvider           _pathProvider;
    private readonly GlobalSettingsRepository             _global;
    private readonly SettingsRepository                   _settings;
    private readonly IDbContextFactory<AppDbContext>      _dbFactory;
    private readonly TenantCutoverReadinessGate           _cutoverGate;
    private readonly TenantDbRollbackReadinessChecker     _rollbackChecker;

    public OperatorDiagnosticsService(
        ILocalDatabasePathProvider pathProvider,
        GlobalSettingsRepository global,
        SettingsRepository settings,
        IDbContextFactory<AppDbContext> dbFactory,
        TenantCutoverReadinessGate cutoverGate,
        TenantDbRollbackReadinessChecker rollbackChecker)
    {
        _pathProvider    = pathProvider;
        _global          = global;
        _settings        = settings;
        _dbFactory       = dbFactory;
        _cutoverGate     = cutoverGate;
        _rollbackChecker = rollbackChecker;
    }

    public async System.Threading.Tasks.Task<OperatorDiagnosticsReport> GetReportAsync(
        string? tenantSubdomain = null,
        System.Threading.CancellationToken ct = default)
    {
        var warnings = new System.Collections.Generic.List<string>();
        var errors   = new System.Collections.Generic.List<string>();

        // Static observations
        var activeDbPath  = _pathProvider.CurrentDbPath;
        var legacyDbPath  = _pathProvider.GetLegacyDbPath();
        var isTenantScoped = _pathProvider.IsTenantScoped;
        var currentTenant = DeriveCurrentTenantSubdomain(_pathProvider);

        var runtimeFlag    = _global.Get("tenant_db_runtime_enabled") == "1";
        var migrationFlag  = _global.Get("shared_to_tenant_migration_enabled") == "1";
        var migratedAt     = _global.Get("shared_to_tenant_migrated_at");
        var lastTenant     = _global.Get("last_tenant_subdomain");
        var apiBaseUrl     = _global.Get("api_base_url");

        // Resolve tenant for cutover readiness: explicit arg → last-used → null
        var resolvedTenant = string.IsNullOrWhiteSpace(tenantSubdomain) ? lastTenant : tenantSubdomain;
        string? sanitizedTenant = null;
        string? targetTenantDbPath = null;
        TenantCutoverReadinessReport? cutoverReport = null;

        if (!string.IsNullOrWhiteSpace(resolvedTenant))
        {
            try
            {
                targetTenantDbPath = _pathProvider.GetTenantDbPath(resolvedTenant);
                sanitizedTenant    = Path.GetFileName(Path.GetDirectoryName(targetTenantDbPath)) ?? resolvedTenant;
                cutoverReport      = await _cutoverGate.CheckAsync(resolvedTenant, ct);
            }
            catch (System.Exception ex)
            {
                errors.Add($"Cutover readiness check failed: {ex.Message}");
            }
        }
        else
        {
            warnings.Add("No tenant subdomain provided and no last tenant is configured.");
        }

        // Rollback readiness always runs (tenant-agnostic).
        TenantDbRollbackReadinessReport rollbackReport;
        try { rollbackReport = _rollbackChecker.Check(); }
        catch (System.Exception ex)
        {
            rollbackReport = new TenantDbRollbackReadinessReport();
            errors.Add($"Rollback readiness check failed: {ex.Message}");
        }

        // Sales / cache diagnostics
        var sales = ReadSalesDiagnostics();
        if (!string.IsNullOrEmpty(sales.ErrorMessage)) errors.Add(sales.ErrorMessage);

        var cache = ReadCacheDiagnostics(currentTenant ?? lastTenant);
        if (!string.IsNullOrEmpty(cache.ErrorMessage)) errors.Add(cache.ErrorMessage);

        var report = new OperatorDiagnosticsReport
        {
            ActiveDbPath                      = activeDbPath,
            LegacyDbPath                      = legacyDbPath,
            IsTenantScoped                    = isTenantScoped,
            CurrentTenantSubdomain            = currentTenant,
            RequestedTenantSubdomain          = tenantSubdomain,
            SanitizedRequestedTenantSubdomain = sanitizedTenant,
            TargetTenantDbPath                = targetTenantDbPath,
            RuntimeTenantDbEnabled            = runtimeFlag,
            MigrationFeatureEnabled           = migrationFlag,
            SharedToTenantMigrated            = !string.IsNullOrEmpty(migratedAt),
            SharedToTenantMigratedAt          = migratedAt,
            LastTenantSubdomain               = lastTenant,
            ApiBaseUrlConfigured              = apiBaseUrl,
            CutoverReadiness                  = cutoverReport,
            RollbackReadiness                 = rollbackReport,
            Sales                             = sales,
            Cache                             = cache,
            Warnings                          = warnings,
            Errors                            = errors,
        };

        ComposeWarnings(report, runtimeFlag);

        // Phase 10.9B.1: surface the missing-role override so operators can
        // see whether the per-machine bypass is active.
        if (_global.Get("operator_access_allow_missing_role") == "1")
            report.Warnings.Add(
                "Operator windows can be opened without a recorded user role because " +
                "operator_access_allow_missing_role is enabled.");

        return report;
    }

    // ── Read-only sales counts ───────────────────────────────────────────────

    private LocalSalesDiagnostics ReadSalesDiagnostics()
    {
        try
        {
            using var db = _dbFactory.CreateDbContext();
            var total           = db.Sales.Count();
            var synced          = db.Sales.Count(s => s.Synced);
            var pending         = db.Sales.Count(s => !s.Synced && !s.IsPoison);
            var poison          = db.Sales.Count(s => !s.Synced && s.IsPoison);
            var failedRetryable = db.Sales.Count(s =>
                                  !s.Synced && !s.IsPoison && s.LastSyncError != "");

            return new LocalSalesDiagnostics
            {
                TotalSalesCount           = total,
                SyncedSalesCount          = synced,
                PendingSalesCount         = pending,
                PoisonSalesCount          = poison,
                FailedRetryableSalesCount = failedRetryable,
            };
        }
        catch (System.Exception ex)
        {
            return new LocalSalesDiagnostics { ErrorMessage = ex.Message };
        }
    }

    // ── Read-only cache counts + sync markers ────────────────────────────────

    private LocalCacheDiagnostics ReadCacheDiagnostics(string? tenantForLegacySuffix)
    {
        try
        {
            using var db = _dbFactory.CreateDbContext();
            var products    = db.Products.Count();
            var customers   = db.Customers.Count();
            var categories  = db.Categories.Count();
            var priceLists  = db.PriceLists.Count();
            var productTypes = db.ProductTypes.Count();

            // Per-tenant DB stores unsuffixed keys (Phase 10.4B migrator strips
            // the suffix). Legacy shared DB stores them as
            // bootstrap_completed_at:<tenant>. Try unsuffixed first, fall back
            // to the suffixed form when a tenant context is available.
            string? Read(string key)
            {
                var v = _settings.Get(key);
                if (!string.IsNullOrEmpty(v)) return v;
                if (!string.IsNullOrWhiteSpace(tenantForLegacySuffix))
                    return _settings.Get(key + ":" + tenantForLegacySuffix);
                return null;
            }

            var bootstrap         = Read("bootstrap_completed_at");
            var lastProductSync   = Read("last_product_sync_at");
            var lastCustomerSync  = Read("last_customer_sync_at");
            var lastStockReconcile= Read("last_stock_reconcile_at");

            return new LocalCacheDiagnostics
            {
                ProductsCount          = products,
                CustomersCount         = customers,
                CategoriesCount        = categories,
                PriceListsCount        = priceLists,
                ProductTypesCount      = productTypes,
                BootstrapCompleted     = !string.IsNullOrEmpty(bootstrap),
                BootstrapCompletedAt   = bootstrap,
                LastProductSyncAt      = lastProductSync,
                LastCustomerSyncAt     = lastCustomerSync,
                LastStockReconcileAt   = lastStockReconcile,
            };
        }
        catch (System.Exception ex)
        {
            return new LocalCacheDiagnostics { ErrorMessage = ex.Message };
        }
    }

    // ── Warnings synthesis ───────────────────────────────────────────────────

    private static void ComposeWarnings(OperatorDiagnosticsReport r, bool runtimeFlag)
    {
        if (runtimeFlag && !r.IsTenantScoped)
            r.Warnings.Add("Runtime tenant DB mode is enabled but provider is not tenant-scoped.");
        if (!runtimeFlag && r.IsTenantScoped)
            r.Warnings.Add("Provider is tenant-scoped but runtime flag is off.");
        if (r.SharedToTenantMigrated && !runtimeFlag)
            r.Warnings.Add("Migration marker present but tenant runtime flag is off.");
        if (runtimeFlag && string.IsNullOrWhiteSpace(r.LastTenantSubdomain))
            r.Warnings.Add("Runtime flag is on but no last tenant is configured.");

        if (r.CutoverReadiness is not null && !r.CutoverReadiness.CanCutOver)
            r.Warnings.Add($"Cutover readiness is blocked: {r.CutoverReadiness.Status}.");
        if (!r.RollbackReadiness.CanRollback &&
            r.RollbackReadiness.Status == TenantDbRollbackReadinessStatus.Blocked)
            r.Warnings.Add("Rollback readiness is blocked.");

        if (r.Sales.PendingSalesCount > 0)
            r.Warnings.Add($"{r.Sales.PendingSalesCount} pending sales exist.");
        if (r.Sales.PoisonSalesCount > 0)
            r.Warnings.Add($"{r.Sales.PoisonSalesCount} poison sales exist.");

        if (r.Cache.ProductsCount == 0)
            r.Warnings.Add("Products cache is empty.");
        if (!r.Cache.BootstrapCompleted)
            r.Warnings.Add("Bootstrap marker missing.");

        if (!string.IsNullOrEmpty(r.ActiveDbPath) && !File.Exists(r.ActiveDbPath))
            r.Warnings.Add("Active DB path does not exist on disk.");
    }

    // ── Path helpers ─────────────────────────────────────────────────────────

    private static string? DeriveCurrentTenantSubdomain(ILocalDatabasePathProvider provider)
    {
        if (!provider.IsTenantScoped) return null;
        var dir = Path.GetDirectoryName(provider.CurrentDbPath);
        return dir is null ? null : Path.GetFileName(dir);
    }
}
