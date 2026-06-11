using System.IO;
using PosSystem.Data.Repositories;

namespace PosSystem.Services;

// ── Report DTO (Phase 10.12A) ────────────────────────────────────────────────

// Operator-facing real-migration readiness verdict. Every field is observation
// — nothing in the codebase reads this report to decide whether to execute
// real migration. The actual migrator has its own independent guards
// (Force=true + feature flag OR override + DryRunOnly=false + healthy gate
// state); this report just helps an operator decide whether to attempt it.
public sealed class RealMigrationExecutionGateReport
{
    public System.DateTime CheckedAtUtc            { get; init; } = System.DateTime.UtcNow;

    // Allowed | AllowedWithWarnings | Blocked
    public string  Status                          { get; init; } = "Blocked";
    public bool    CanExecuteRealMigration         { get; init; }

    public string? TenantSubdomain                 { get; init; }
    public string? ActiveDbPath                    { get; init; }
    public bool    RuntimeTenantDbEnabled          { get; init; }
    public bool    IsTenantScoped                  { get; init; }

    public bool    MigrationFeatureEnabled         { get; init; }
    public bool    SharedToTenantMigrated          { get; init; }
    public string? SharedToTenantMigratedAt        { get; init; }

    public int     PendingSalesCount               { get; init; }
    public int     PoisonSalesCount                { get; init; }

    public string? CutoverStatus                   { get; init; }
    public string? MigrationDryRunOutcome          { get; init; }
    public bool    MigrationDryRunSideEffectCheckPassed     { get; init; }
    public int     MigrationDryRunSideEffectDifferenceCount { get; init; }

    public System.Collections.Generic.List<string> BlockingReasons  { get; init; } = new();
    public System.Collections.Generic.List<string> Warnings         { get; init; } = new();
    public System.Collections.Generic.List<string> RecommendedSteps { get; init; } = new();
}

// ── Service ──────────────────────────────────────────────────────────────────

// Strict, read-only/preview-only verdict assembler for real-migration
// execution readiness. Aggregates the same set of safety signals that the
// preflight bundle uses, but distills them into a single allow/warn/block
// decision suitable for a future operator surface that might expose real
// migration.
//
// What this service does:
//   • Calls read-only / preview-only services (diagnostics, auditor,
//     verifier, cutover gate, migration dry-run preview).
//   • Reads global-settings flags via GlobalSettingsRepository.Get only.
//   • Reads filesystem mtimes via FileInfo for cheap "no recent export"
//     warnings.
//
// What this service NEVER does:
//   • Invoke the migrator (SharedToTenantDatabaseMigrator is not injected).
//   • Invoke the rollback executor (TenantDbRollbackExecutor is not injected).
//   • Switch the path provider (TenantScopeService is not injected).
//   • Mutate global_settings.json, the active DB, or any tenant DB.
//   • Create, delete, move, rename, or copy any file or directory.
//   • Open any SQLite DB for write.
public sealed class RealMigrationExecutionGateService
{
    private const string MigrationFlagKey   = "shared_to_tenant_migration_enabled";
    private const string RuntimeFlagKey     = "tenant_db_runtime_enabled";
    private const string MigratedMarkerKey  = "shared_to_tenant_migrated_at";
    private const string LastTenantKey      = "last_tenant_subdomain";
    private const int    RecentExportDays   = 7;

    private readonly OperatorDiagnosticsService          _diagnostics;
    private readonly SharedToTenantMigrationAuditor      _auditor;
    private readonly SharedToTenantMigrationVerifier     _verifier;
    private readonly TenantCutoverReadinessGate          _cutoverGate;
    private readonly MigrationDryRunPreviewService       _dryRunPreview;
    private readonly GlobalSettingsRepository            _global;
    private readonly Data.ILocalDatabasePathProvider     _paths;

    public RealMigrationExecutionGateService(
        OperatorDiagnosticsService diagnostics,
        SharedToTenantMigrationAuditor auditor,
        SharedToTenantMigrationVerifier verifier,
        TenantCutoverReadinessGate cutoverGate,
        MigrationDryRunPreviewService dryRunPreview,
        GlobalSettingsRepository global,
        Data.ILocalDatabasePathProvider paths)
    {
        _diagnostics   = diagnostics;
        _auditor       = auditor;
        _verifier      = verifier;
        _cutoverGate   = cutoverGate;
        _dryRunPreview = dryRunPreview;
        _global        = global;
        _paths         = paths;
    }

    public async System.Threading.Tasks.Task<RealMigrationExecutionGateReport> CheckAsync(
        string? tenantSubdomain = null,
        System.Threading.CancellationToken ct = default)
    {
        var blockers         = new System.Collections.Generic.List<string>();
        var warnings         = new System.Collections.Generic.List<string>();
        var recommendedSteps = new System.Collections.Generic.List<string>();

        // ── 1. Static flag / tenant resolution. ─────────────────────────────
        var resolvedTenant = string.IsNullOrWhiteSpace(tenantSubdomain)
            ? _global.Get(LastTenantKey)
            : tenantSubdomain.Trim();

        if (string.IsNullOrWhiteSpace(resolvedTenant))
            blockers.Add("Tenant subdomain is missing and cannot be resolved from last_tenant_subdomain.");

        var migrationFlag      = _global.Get(MigrationFlagKey) == "1";
        if (!migrationFlag)
            blockers.Add($"Migration feature flag is off ({MigrationFlagKey} != \"1\").");

        var runtimeFlag        = _global.Get(RuntimeFlagKey) == "1";
        if (runtimeFlag)
            blockers.Add(
                "Runtime tenant DB mode is already enabled — migration should complete BEFORE runtime mode is turned on.");

        if (_paths.IsTenantScoped)
            blockers.Add(
                "Provider is already tenant-scoped — restart the app so it opens legacy pos.db before real migration.");

        var migratedAt         = _global.Get(MigratedMarkerKey);
        var alreadyMigrated    = !string.IsNullOrEmpty(migratedAt);
        if (alreadyMigrated)
            blockers.Add(
                $"Shared-to-tenant migration marker already exists ({MigratedMarkerKey} = \"{migratedAt}\"). " +
                "A second real migration would be unsafe.");

        // ── 2. Diagnostics. ─────────────────────────────────────────────────
        OperatorDiagnosticsReport? diag = null;
        try
        {
            diag = await _diagnostics.GetReportAsync(resolvedTenant, ct);
        }
        catch (System.Exception ex)
        {
            blockers.Add($"Diagnostics subsystem failed: {ex.Message}");
        }

        if (diag is null)
        {
            blockers.Add("Diagnostics report unavailable.");
        }
        else
        {
            if (diag.Sales.PendingSalesCount > 0)
                blockers.Add($"{diag.Sales.PendingSalesCount} pending sales exist.");
            if (diag.Sales.PoisonSalesCount > 0)
                blockers.Add($"{diag.Sales.PoisonSalesCount} poison sales exist.");
            foreach (var w in diag.Warnings)
                warnings.Add($"Diagnostics: {w}");
        }

        // ── 3. Cutover readiness (tenant-scoped). ───────────────────────────
        string? cutoverStatus = null;
        if (!string.IsNullOrWhiteSpace(resolvedTenant))
        {
            try
            {
                var cutover = await _cutoverGate.CheckAsync(resolvedTenant, ct);
                cutoverStatus = cutover.Status.ToString();
                if (cutover.Status == TenantDbCutoverReadinessStatus.Blocked ||
                    cutover.Status == TenantDbCutoverReadinessStatus.Disabled)
                {
                    blockers.Add($"Cutover readiness is {cutover.Status}.");
                }
                else if (cutover.Status == TenantDbCutoverReadinessStatus.AllowedWithWarnings)
                {
                    warnings.Add("Cutover readiness is AllowedWithWarnings.");
                }
            }
            catch (System.Exception ex)
            {
                blockers.Add($"Cutover readiness check failed: {ex.Message}");
            }
        }

        // ── 4. Migration audit (untagged/orphan source rows). ───────────────
        try
        {
            var audit = await _auditor.AnalyzeAsync(ct);
            if (audit.UntaggedSalesCount > 0 && string.IsNullOrWhiteSpace(audit.LastTenantSubdomain))
            {
                blockers.Add(
                    $"{audit.UntaggedSalesCount} untagged source row(s) have no best-guess owner " +
                    "(last_tenant_subdomain not set). Real migration would quarantine them blindly.");
            }
            else if (audit.UntaggedSalesCount > 0)
            {
                warnings.Add(
                    $"{audit.UntaggedSalesCount} untagged source row(s) will be attached to " +
                    $"'{audit.LastTenantSubdomain}' during real migration.");
            }
            foreach (var obs in audit.Observations)
                warnings.Add($"Audit: {obs}");
        }
        catch (System.Exception ex)
        {
            blockers.Add($"Migration audit failed: {ex.Message}");
        }

        // ── 5. Verifier (post-migration-but-unverified state is a blocker). ─
        try
        {
            var verify = await _verifier.VerifyAsync(ct);
            if (verify.SourceDbExists && verify.GlobalMarkerPresent && !verify.AllVerified)
            {
                blockers.Add(
                    "Migration verifier reports a previously-migrated-but-unverified state. " +
                    "Investigate before attempting another migration.");
            }
        }
        catch (System.Exception ex)
        {
            blockers.Add($"Migration verifier failed: {ex.Message}");
        }

        // ── 6. Migration dry-run preview (preview-only, side-effect guarded). ─
        string? dryRunOutcome = null;
        bool    dryRunSideOk  = false;
        int     dryRunSideDiffs = 0;
        try
        {
            var dry = await _dryRunPreview.PreviewAsync(ct);
            dryRunOutcome = dry.Outcome;
            dryRunSideOk  = dry.SideEffectCheckPassed;
            dryRunSideDiffs = dry.SideEffectDifferenceCount;

            if (!dry.IsAvailable || dry.Outcome == "Failed")
                blockers.Add("Migration dry-run preview failed.");
            if (!dry.SideEffectCheckPassed)
                blockers.Add(
                    $"Migration dry-run side-effect check failed ({dry.SideEffectDifferenceCount} difference(s)).");
            else if (dry.SideEffectDifferenceCount > 0)
                blockers.Add(
                    $"Migration dry-run reported {dry.SideEffectDifferenceCount} side-effect difference(s) despite check passing — investigate.");

            foreach (var w in dry.Warnings)
                warnings.Add($"Migration dry-run: {w}");
        }
        catch (System.Exception ex)
        {
            blockers.Add($"Migration dry-run preview failed: {ex.Message}");
        }

        // ── 7. Recent preflight / inventory export presence (cheap warning). ─
        try
        {
            var legacyDb = _paths.GetLegacyDbPath();
            var baseDir  = Path.GetDirectoryName(legacyDb)
                           ?? Path.Combine(
                                  System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                                  "PosSystem");
            if (!HasRecentFile(Path.Combine(baseDir, "logs", "preflight"), RecentExportDays))
                warnings.Add($"No preflight export found in the last {RecentExportDays} days.");
            if (!HasRecentFile(Path.Combine(baseDir, "logs", "inventory"), RecentExportDays))
                warnings.Add($"No inventory export found in the last {RecentExportDays} days.");
        }
        catch
        {
            // Cheap warning helpers must never block the gate result.
        }

        // ── 8. Recommended steps. ───────────────────────────────────────────
        recommendedSteps.Add("Run Export Preflight Report and review OverallStatus before real migration.");
        recommendedSteps.Add("Run Preview Migration Dry-Run and confirm side-effect check is Passed.");
        recommendedSteps.Add("Take an external backup of %LocalAppData%\\PosSystem before real migration.");
        recommendedSteps.Add("Do not enable tenant DB runtime mode until migration verification passes.");
        if (diag is not null && diag.Sales.PendingSalesCount > 0)
            recommendedSteps.Add("Ensure all pending offline sales are synced before migration.");
        if (diag is not null && diag.Sales.PoisonSalesCount > 0)
            recommendedSteps.Add("Resolve poison sales before migration.");

        // ── 9. Classify. ────────────────────────────────────────────────────
        var status = blockers.Count > 0
            ? "Blocked"
            : (warnings.Count > 0 ? "AllowedWithWarnings" : "Allowed");
        var canExecute = blockers.Count == 0;

        return new RealMigrationExecutionGateReport
        {
            CheckedAtUtc                              = System.DateTime.UtcNow,
            Status                                    = status,
            CanExecuteRealMigration                   = canExecute,
            TenantSubdomain                           = resolvedTenant,
            ActiveDbPath                              = _paths.CurrentDbPath,
            RuntimeTenantDbEnabled                    = runtimeFlag,
            IsTenantScoped                            = _paths.IsTenantScoped,
            MigrationFeatureEnabled                   = migrationFlag,
            SharedToTenantMigrated                    = alreadyMigrated,
            SharedToTenantMigratedAt                  = migratedAt,
            PendingSalesCount                         = diag?.Sales.PendingSalesCount ?? 0,
            PoisonSalesCount                          = diag?.Sales.PoisonSalesCount  ?? 0,
            CutoverStatus                             = cutoverStatus,
            MigrationDryRunOutcome                    = dryRunOutcome,
            MigrationDryRunSideEffectCheckPassed      = dryRunSideOk,
            MigrationDryRunSideEffectDifferenceCount  = dryRunSideDiffs,
            BlockingReasons                           = blockers,
            Warnings                                  = warnings,
            RecommendedSteps                          = recommendedSteps,
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool HasRecentFile(string dirPath, int days)
    {
        try
        {
            if (!Directory.Exists(dirPath)) return false;
            var threshold = System.DateTime.UtcNow.AddDays(-days);
            foreach (var f in Directory.EnumerateFiles(dirPath, "*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (new FileInfo(f).LastWriteTimeUtc >= threshold) return true;
                }
                catch
                {
                    // skip unreadable file
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}
