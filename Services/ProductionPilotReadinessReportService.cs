using System.IO;
using System.Text.Json;
using PosSystem.Data.Repositories;

namespace PosSystem.Services;

// ── DTOs (Phase 10.17A) ──────────────────────────────────────────────────────

public sealed class ProductionPilotReadinessCheck
{
    // Free-form area label (e.g. "Flags", "Diagnostics", "Cutover").
    public string Area    { get; init; } = "";
    // Specific check name (e.g. "tenant_db_runtime_enabled").
    public string Name    { get; init; } = "";
    // Pass | Warning | Blocked | Info | Unknown
    public string Status  { get; init; } = "Unknown";
    public string Message { get; init; } = "";
}

public sealed class ProductionPilotReadinessReport
{
    public System.DateTime GeneratedAtUtc { get; init; } = System.DateTime.UtcNow;

    // Ready | ReadyWithWarnings | Blocked | Unknown
    public string  OverallStatus           { get; init; } = "Unknown";
    public string? TenantSubdomain         { get; init; }

    public System.Collections.Generic.List<ProductionPilotReadinessCheck> Checks
        { get; init; } = new();

    public System.Collections.Generic.List<string> BlockingReasons       { get; init; } = new();
    public System.Collections.Generic.List<string> Warnings              { get; init; } = new();
    public System.Collections.Generic.List<string> RecommendedNextSteps  { get; init; } = new();

    public string? DiagnosticsSummary      { get; init; }
    public string? MigrationSummary        { get; init; }
    public string? RuntimeCutoverSummary   { get; init; }
    public string? RollbackSummary         { get; init; }
    public string? RetentionCleanupSummary { get; init; }
    public string? BackendPermissionSummary { get; init; }

    public string? ExportPath              { get; init; }
}

// ── Service ──────────────────────────────────────────────────────────────────

// Strictly read-only readiness aggregator. Composes existing diagnostics +
// gates + checkers + preview into a single pilot-readiness verdict suitable
// for a "can we run a controlled production pilot?" decision.
//
// What this service does:
//   • Calls only read-only services (diagnostics, cutover gate, rollback
//     checker, verifier, retention preview).
//   • Reads global-settings flags via Get only.
//   • Reads filesystem mtimes via FileInfo (recent-export warnings + runbook
//     existence checks).
//   • Optionally writes one redacted JSON report under logs\pilot-readiness\.
//
// What this service NEVER does:
//   • Invoke any guarded executor (migration / runtime cutover / rollback /
//     retention cleanup wrappers are not injected).
//   • Invoke the underlying migrator, rollback executor, or
//     TenantScopeService.
//   • Mutate any setting / DB / file beyond the optional report file.
//   • Switch the path provider.
//   • Auto-logout / auto-restart.
public sealed class ProductionPilotReadinessReportService
{
    private const string ReadinessLogSubdir = "pilot-readiness";

    private const string FlagDashboard       = "operator_migration_dashboard_enabled";
    private const string FlagRealMigrationUi = "operator_real_migration_ui_enabled";
    private const string FlagRuntimeCutoverUi = "operator_runtime_cutover_ui_enabled";
    private const string FlagRollbackUi      = "operator_rollback_ui_enabled";
    private const string FlagCleanupUi       = "operator_retention_cleanup_ui_enabled";
    private const string FlagMigrationEnable = "shared_to_tenant_migration_enabled";
    private const string FlagRuntimeEnabled  = "tenant_db_runtime_enabled";
    private const string KeyMigratedAt       = "shared_to_tenant_migrated_at";
    private const string KeyLastTenant       = "last_tenant_subdomain";

    // Statuses
    private const string StatusPass    = "Pass";
    private const string StatusWarning = "Warning";
    private const string StatusBlocked = "Blocked";
    private const string StatusInfo    = "Info";
    private const string StatusUnknown = "Unknown";

    // Overall statuses
    private const string OverallReady             = "Ready";
    private const string OverallReadyWithWarnings = "ReadyWithWarnings";
    private const string OverallBlocked           = "Blocked";
    private const string OverallUnknown           = "Unknown";

    // Phrases scrubbed from every export
    private const string PhraseCleanup            = "EXECUTE_RETENTION_CLEANUP";
    private const string PhraseRollbackWrapper    = "EXECUTE_TENANT_DB_RUNTIME_ROLLBACK";
    private const string PhraseRollbackInner      = "ROLLBACK_TO_LEGACY_POS_DB";
    private const string PhraseRollbackLegacy     = "I UNDERSTAND TENANT DB ROLLBACK";
    private const string PhraseMigration          = "EXECUTE_REAL_TENANT_DB_MIGRATION";
    private const string PhraseRuntime            = "ENABLE_TENANT_DB_RUNTIME_MODE";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly OperatorDiagnosticsService          _diagnostics;
    private readonly TenantCutoverReadinessGate          _cutoverGate;
    private readonly TenantDbRollbackReadinessChecker    _rollbackChecker;
    private readonly SharedToTenantMigrationVerifier     _verifier;
    private readonly TenantDatabaseRetentionPreviewService _retention;
    private readonly GlobalSettingsRepository            _global;
    private readonly Data.ILocalDatabasePathProvider     _paths;
    private readonly BackendOperatorPermissionSnapshotService _backendPermissionSnapshot;

    public ProductionPilotReadinessReportService(
        OperatorDiagnosticsService diagnostics,
        TenantCutoverReadinessGate cutoverGate,
        TenantDbRollbackReadinessChecker rollbackChecker,
        SharedToTenantMigrationVerifier verifier,
        TenantDatabaseRetentionPreviewService retention,
        GlobalSettingsRepository global,
        Data.ILocalDatabasePathProvider paths,
        BackendOperatorPermissionSnapshotService backendPermissionSnapshot)
    {
        _diagnostics               = diagnostics;
        _cutoverGate               = cutoverGate;
        _rollbackChecker           = rollbackChecker;
        _verifier                  = verifier;
        _retention                 = retention;
        _global                    = global;
        _paths                     = paths;
        _backendPermissionSnapshot = backendPermissionSnapshot;
    }

    public async System.Threading.Tasks.Task<ProductionPilotReadinessReport> GenerateAsync(
        string? tenantSubdomain,
        System.Threading.CancellationToken ct = default)
    {
        var checks   = new System.Collections.Generic.List<ProductionPilotReadinessCheck>();
        var blockers = new System.Collections.Generic.List<string>();
        var warnings = new System.Collections.Generic.List<string>();

        // Resolve tenant once.
        var resolvedTenant = string.IsNullOrWhiteSpace(tenantSubdomain)
            ? _global.Get(KeyLastTenant)
            : tenantSubdomain.Trim();

        // ── Area A — Operator access / flags. ───────────────────────────────
        AddFlagCheck(checks, FlagDashboard, "1", StatusPass, "Dashboard flag is enabled.",
            warningWhenMissing: "Migration Operations dashboard flag is off — dashboard cannot be opened.");
        AddDestructiveUiFlagCheck(checks, FlagRealMigrationUi, "Real migration");
        AddDestructiveUiFlagCheck(checks, FlagRuntimeCutoverUi, "Runtime cutover");
        AddDestructiveUiFlagCheck(checks, FlagRollbackUi, "Rollback");
        AddDestructiveUiFlagCheck(checks, FlagCleanupUi, "Retention cleanup");

        var migrationEnabled = _global.Get(FlagMigrationEnable) == "1";
        checks.Add(new ProductionPilotReadinessCheck
        {
            Area    = "Flags",
            Name    = FlagMigrationEnable,
            Status  = migrationEnabled ? StatusPass : StatusInfo,
            Message = migrationEnabled
                ? "Server-side migration enable flag is on."
                : "Server-side migration enable flag is off (no migration planned).",
        });

        var runtimeEnabled = _global.Get(FlagRuntimeEnabled) == "1";
        checks.Add(new ProductionPilotReadinessCheck
        {
            Area    = "Flags",
            Name    = FlagRuntimeEnabled,
            Status  = runtimeEnabled ? StatusWarning : StatusInfo,
            Message = runtimeEnabled
                ? "Runtime tenant DB mode is currently enabled — pilot already in tenant-runtime mode."
                : "Runtime tenant DB mode is off (legacy mode).",
        });

        var migratedAt = _global.Get(KeyMigratedAt);
        checks.Add(new ProductionPilotReadinessCheck
        {
            Area    = "Flags",
            Name    = KeyMigratedAt,
            Status  = string.IsNullOrEmpty(migratedAt) ? StatusInfo : StatusInfo,
            Message = string.IsNullOrEmpty(migratedAt)
                ? "Migration marker absent (no real migration has run on this install)."
                : $"Migration marker present (stamped {migratedAt}).",
        });

        if (string.IsNullOrWhiteSpace(resolvedTenant))
        {
            checks.Add(new ProductionPilotReadinessCheck
            {
                Area    = "Flags",
                Name    = KeyLastTenant,
                Status  = StatusWarning,
                Message = "No tenant subdomain provided and last_tenant_subdomain is empty.",
            });
        }
        else
        {
            checks.Add(new ProductionPilotReadinessCheck
            {
                Area    = "Flags",
                Name    = KeyLastTenant,
                Status  = StatusPass,
                Message = $"Tenant resolved: '{resolvedTenant}'.",
            });
        }

        // ── Area B — Diagnostics. ───────────────────────────────────────────
        OperatorDiagnosticsReport? diag = null;
        string? diagnosticsSummary = null;
        try
        {
            diag = await _diagnostics.GetReportAsync(resolvedTenant, ct);
        }
        catch (System.Exception ex)
        {
            checks.Add(new ProductionPilotReadinessCheck
            {
                Area    = "Diagnostics",
                Name    = "GetReportAsync",
                Status  = StatusBlocked,
                Message = $"Diagnostics threw: {ex.Message}",
            });
        }

        if (diag is null)
        {
            checks.Add(new ProductionPilotReadinessCheck
            {
                Area    = "Diagnostics",
                Name    = "Report availability",
                Status  = StatusBlocked,
                Message = "Diagnostics report unavailable.",
            });
        }
        else
        {
            checks.Add(new ProductionPilotReadinessCheck
            {
                Area    = "Diagnostics",
                Name    = "Pending sales",
                Status  = diag.Sales.PendingSalesCount > 0 ? StatusBlocked : StatusPass,
                Message = diag.Sales.PendingSalesCount > 0
                    ? $"{diag.Sales.PendingSalesCount} pending sales exist."
                    : "No pending sales.",
            });
            checks.Add(new ProductionPilotReadinessCheck
            {
                Area    = "Diagnostics",
                Name    = "Poison sales",
                Status  = diag.Sales.PoisonSalesCount > 0 ? StatusBlocked : StatusPass,
                Message = diag.Sales.PoisonSalesCount > 0
                    ? $"{diag.Sales.PoisonSalesCount} poison sales exist."
                    : "No poison sales.",
            });

            foreach (var w in diag.Warnings)
            {
                checks.Add(new ProductionPilotReadinessCheck
                {
                    Area    = "Diagnostics",
                    Name    = "Warning",
                    Status  = StatusWarning,
                    Message = w,
                });
            }

            diagnosticsSummary =
                $"ActiveDb={diag.ActiveDbPath}; tenantScoped={diag.IsTenantScoped}; " +
                $"pending={diag.Sales.PendingSalesCount}; poison={diag.Sales.PoisonSalesCount}; " +
                $"products={diag.Cache.ProductsCount}; customers={diag.Cache.CustomersCount}";
        }

        // ── Area C — Migration readiness (composed from existing signals). ──
        string? migrationSummary = null;
        var migrationConcerns = new System.Collections.Generic.List<string>();
        if (!migrationEnabled)
            migrationConcerns.Add("server-side migration flag off");
        if (!string.IsNullOrEmpty(migratedAt))
            migrationConcerns.Add("migration marker already present");
        if (diag is not null && (diag.Sales.PendingSalesCount > 0 || diag.Sales.PoisonSalesCount > 0))
            migrationConcerns.Add("pending/poison sales");
        if (_paths.IsTenantScoped)
            migrationConcerns.Add("provider already tenant-scoped");

        if (migrationConcerns.Count == 0)
        {
            checks.Add(new ProductionPilotReadinessCheck
            {
                Area    = "Migration",
                Name    = "Composite readiness",
                Status  = StatusPass,
                Message = "Composite migration readiness signals look clean.",
            });
            migrationSummary = "Composite readiness: clean.";
        }
        else
        {
            checks.Add(new ProductionPilotReadinessCheck
            {
                Area    = "Migration",
                Name    = "Composite readiness",
                Status  = StatusWarning,
                Message = "Concerns: " + string.Join("; ", migrationConcerns),
            });
            migrationSummary = "Composite readiness concerns: " + string.Join("; ", migrationConcerns);
        }

        // ── Area D — Migration verification. ────────────────────────────────
        try
        {
            var verify = await _verifier.VerifyAsync(ct);
            if (verify is null)
            {
                checks.Add(new ProductionPilotReadinessCheck
                {
                    Area    = "Verification",
                    Name    = "VerifyAsync",
                    Status  = StatusBlocked,
                    Message = "Verifier returned null.",
                });
            }
            else if (verify.GlobalMarkerPresent)
            {
                if (verify.AllVerified)
                {
                    checks.Add(new ProductionPilotReadinessCheck
                    {
                        Area    = "Verification",
                        Name    = "AllVerified",
                        Status  = StatusPass,
                        Message = $"All {verify.Tenants.Count} tenant(s) verified.",
                    });
                }
                else
                {
                    checks.Add(new ProductionPilotReadinessCheck
                    {
                        Area    = "Verification",
                        Name    = "AllVerified",
                        Status  = StatusBlocked,
                        Message = "Migration marker present but AllVerified=false.",
                    });
                }

                if (!string.IsNullOrWhiteSpace(resolvedTenant))
                {
                    var match = verify.Tenants.FirstOrDefault(t =>
                        string.Equals(t.Subdomain, resolvedTenant,
                            System.StringComparison.OrdinalIgnoreCase));
                    if (match is null)
                    {
                        checks.Add(new ProductionPilotReadinessCheck
                        {
                            Area    = "Verification",
                            Name    = "Tenant entry",
                            Status  = StatusBlocked,
                            Message = $"Verifier has no entry for tenant '{resolvedTenant}'.",
                        });
                    }
                    else if (!match.Verified)
                    {
                        checks.Add(new ProductionPilotReadinessCheck
                        {
                            Area    = "Verification",
                            Name    = "Tenant entry",
                            Status  = StatusBlocked,
                            Message = $"Tenant '{resolvedTenant}' not Verified: {string.Join("; ", match.Issues)}",
                        });
                    }
                }
            }
            else
            {
                checks.Add(new ProductionPilotReadinessCheck
                {
                    Area    = "Verification",
                    Name    = "Marker",
                    Status  = StatusInfo,
                    Message = "Migration marker not present — verification not applicable yet.",
                });
            }
        }
        catch (System.Exception ex)
        {
            checks.Add(new ProductionPilotReadinessCheck
            {
                Area    = "Verification",
                Name    = "VerifyAsync",
                Status  = StatusBlocked,
                Message = $"Verifier threw: {ex.Message}",
            });
        }

        // ── Area E — Runtime cutover readiness. ─────────────────────────────
        string? cutoverSummary = null;
        if (!string.IsNullOrWhiteSpace(resolvedTenant))
        {
            try
            {
                var cutover = await _cutoverGate.CheckAsync(resolvedTenant, ct);
                cutoverSummary = $"Status={cutover.Status}";
                var status = cutover.Status switch
                {
                    TenantDbCutoverReadinessStatus.Allowed             => StatusPass,
                    TenantDbCutoverReadinessStatus.AllowedWithWarnings => StatusWarning,
                    TenantDbCutoverReadinessStatus.Disabled            => StatusInfo,
                    TenantDbCutoverReadinessStatus.Blocked             => StatusBlocked,
                    _                                                  => StatusUnknown,
                };
                checks.Add(new ProductionPilotReadinessCheck
                {
                    Area    = "Cutover",
                    Name    = "ReadinessStatus",
                    Status  = status,
                    Message = $"Cutover gate: {cutover.Status}.",
                });
            }
            catch (System.Exception ex)
            {
                checks.Add(new ProductionPilotReadinessCheck
                {
                    Area    = "Cutover",
                    Name    = "CheckAsync",
                    Status  = StatusBlocked,
                    Message = $"Cutover gate threw: {ex.Message}",
                });
            }
        }
        else
        {
            checks.Add(new ProductionPilotReadinessCheck
            {
                Area    = "Cutover",
                Name    = "ReadinessStatus",
                Status  = StatusInfo,
                Message = "Cutover readiness not evaluated — no tenant resolved.",
            });
        }

        checks.Add(new ProductionPilotReadinessCheck
        {
            Area    = "Cutover",
            Name    = "Provider tenant-scoped",
            Status  = _paths.IsTenantScoped ? StatusWarning : StatusInfo,
            Message = _paths.IsTenantScoped
                ? "Path provider is currently tenant-scoped."
                : "Path provider is in legacy mode.",
        });

        // ── Area F — Rollback readiness. ────────────────────────────────────
        string? rollbackSummary = null;
        try
        {
            var rb = _rollbackChecker.Check();
            rollbackSummary = $"Status={rb.Status}; canRollback={rb.CanRollback}";
            var status = rb.Status switch
            {
                TenantDbRollbackReadinessStatus.Ready                  => StatusPass,
                TenantDbRollbackReadinessStatus.ReadyWithWarnings      => StatusWarning,
                TenantDbRollbackReadinessStatus.NotInTenantRuntimeMode => StatusInfo,
                TenantDbRollbackReadinessStatus.Blocked                => StatusBlocked,
                _                                                       => StatusUnknown,
            };
            checks.Add(new ProductionPilotReadinessCheck
            {
                Area    = "Rollback",
                Name    = "ReadinessStatus",
                Status  = status,
                Message = $"Rollback checker: {rb.Status}.",
            });
        }
        catch (System.Exception ex)
        {
            checks.Add(new ProductionPilotReadinessCheck
            {
                Area    = "Rollback",
                Name    = "Check",
                Status  = StatusBlocked,
                Message = $"Rollback checker threw: {ex.Message}",
            });
        }

        // ── Area G — Retention cleanup preview. ─────────────────────────────
        string? retentionSummary = null;
        try
        {
            var preview = await _retention.PreviewAsync(null, ct);
            if (preview is null)
            {
                checks.Add(new ProductionPilotReadinessCheck
                {
                    Area    = "RetentionCleanup",
                    Name    = "PreviewAsync",
                    Status  = StatusBlocked,
                    Message = "Retention preview returned null.",
                });
            }
            else
            {
                if (preview.Errors.Count > 0)
                {
                    foreach (var e in preview.Errors)
                    {
                        checks.Add(new ProductionPilotReadinessCheck
                        {
                            Area    = "RetentionCleanup",
                            Name    = "PreviewError",
                            Status  = StatusBlocked,
                            Message = e,
                        });
                    }
                }
                checks.Add(new ProductionPilotReadinessCheck
                {
                    Area    = "RetentionCleanup",
                    Name    = "Candidates",
                    Status  = preview.CandidateCount == 0 ? StatusInfo : StatusInfo,
                    Message = preview.CandidateCount == 0
                        ? "No cleanup candidates."
                        : $"{preview.CandidateCount} candidate(s) ({preview.CandidateSizeText}).",
                });
                checks.Add(new ProductionPilotReadinessCheck
                {
                    Area    = "RetentionCleanup",
                    Name    = "Protected items",
                    Status  = StatusInfo,
                    Message = $"{preview.ProtectedItemCount} protected item(s) ({preview.ProtectedSizeText}).",
                });
                retentionSummary =
                    $"candidates={preview.CandidateCount} ({preview.CandidateSizeText}); " +
                    $"protected={preview.ProtectedItemCount} ({preview.ProtectedSizeText})";
            }
        }
        catch (System.Exception ex)
        {
            checks.Add(new ProductionPilotReadinessCheck
            {
                Area    = "RetentionCleanup",
                Name    = "PreviewAsync",
                Status  = StatusBlocked,
                Message = $"Retention preview threw: {ex.Message}",
            });
        }

        // ── Area H — Runbook existence (best-effort under AppContext base). ─
        var docsDir = Path.Combine(System.AppContext.BaseDirectory, "docs");
        CheckRunbook(checks, docsDir, "operator-tenant-db-migration-runbook.md");
        CheckRunbook(checks, docsDir, "operator-retention-cleanup-runbook.md");
        CheckRunbook(checks, docsDir, "tenant-db-rollback.md");

        // ── Area I — Backend operator permissions (Phase 10.19E). ───────────
        string? backendPermissionSummary = null;
        try
        {
            var snapshot = await _backendPermissionSnapshot.GenerateAsync(resolvedTenant, ct);
            backendPermissionSummary = ComposeBackendPermissionSummary(snapshot);
            AddBackendPermissionChecks(checks, snapshot);
        }
        catch (System.OperationCanceledException) { throw; }
        catch (System.Exception ex)
        {
            // Snapshot service is fail-closed; an exception here is unexpected.
            checks.Add(new ProductionPilotReadinessCheck
            {
                Area    = "BackendPermissions",
                Name    = "snapshot",
                Status  = StatusBlocked,
                Message = $"Backend permission snapshot threw: {ex.Message}",
            });
            backendPermissionSummary = $"snapshot threw: {ex.Message}";
        }

        // ── Aggregate. ──────────────────────────────────────────────────────
        foreach (var c in checks)
        {
            switch (c.Status)
            {
                case StatusBlocked:
                    blockers.Add($"{c.Area}/{c.Name}: {c.Message}");
                    break;
                case StatusWarning:
                    warnings.Add($"{c.Area}/{c.Name}: {c.Message}");
                    break;
            }
        }

        string overall;
        if (blockers.Count > 0)         overall = OverallBlocked;
        else if (warnings.Count > 0)    overall = OverallReadyWithWarnings;
        else                            overall = OverallReady;

        // Recommended next steps.
        var nextSteps = new System.Collections.Generic.List<string>();
        switch (overall)
        {
            case OverallBlocked:
                nextSteps.Add("Resolve pending/poison sales before any destructive operation.");
                nextSteps.Add("Rerun diagnostics and the relevant readiness checker until status improves.");
                nextSteps.Add("Refresh preflight and inventory exports immediately before any cleanup window.");
                nextSteps.Add("Do not execute migration, runtime cutover, rollback, or cleanup until blockers are resolved.");
                break;
            case OverallReadyWithWarnings:
                nextSteps.Add("Review every warning with support / owner before scheduling the pilot window.");
                nextSteps.Add("Confirm an external/off-machine backup of %LocalAppData%\\PosSystem\\ is in place.");
                nextSteps.Add("Run the pilot against a test tenant or low-risk production window per docs/operator-tenant-db-migration-runbook.md.");
                break;
            case OverallReady:
                nextSteps.Add("Proceed only according to docs/operator-tenant-db-migration-runbook.md.");
                nextSteps.Add("Enable destructive UI flags only for the exact operation window, then disable.");
                nextSteps.Add("Archive every audit log and external backup after the pilot completes.");
                break;
            default:
                nextSteps.Add("Investigation required — overall status is Unknown.");
                break;
        }

        var report = new ProductionPilotReadinessReport
        {
            GeneratedAtUtc          = System.DateTime.UtcNow,
            OverallStatus           = overall,
            TenantSubdomain         = resolvedTenant,
            Checks                  = checks,
            BlockingReasons         = blockers,
            Warnings                = warnings,
            RecommendedNextSteps    = nextSteps,
            DiagnosticsSummary      = diagnosticsSummary,
            MigrationSummary        = migrationSummary,
            RuntimeCutoverSummary   = cutoverSummary,
            RollbackSummary         = rollbackSummary,
            RetentionCleanupSummary = retentionSummary,
            BackendPermissionSummary = backendPermissionSummary,
        };

        // Best-effort export. Failure recorded as a warning on the in-memory
        // report, never crashes.
        var exportPath = TryExport(report);
        if (!string.IsNullOrEmpty(exportPath))
        {
            return new ProductionPilotReadinessReport
            {
                GeneratedAtUtc          = report.GeneratedAtUtc,
                OverallStatus           = report.OverallStatus,
                TenantSubdomain         = report.TenantSubdomain,
                Checks                  = report.Checks,
                BlockingReasons         = report.BlockingReasons,
                Warnings                = report.Warnings,
                RecommendedNextSteps    = report.RecommendedNextSteps,
                DiagnosticsSummary      = report.DiagnosticsSummary,
                MigrationSummary        = report.MigrationSummary,
                RuntimeCutoverSummary   = report.RuntimeCutoverSummary,
                RollbackSummary         = report.RollbackSummary,
                RetentionCleanupSummary = report.RetentionCleanupSummary,
                BackendPermissionSummary = report.BackendPermissionSummary,
                ExportPath              = exportPath,
            };
        }
        else
        {
            // Export failed; preserve the report but flag the failure as a
            // warning on the response.
            report.Warnings.Add("Pilot readiness export failed — see error in the dashboard's Errors card.");
            return report;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void AddFlagCheck(
        System.Collections.Generic.List<ProductionPilotReadinessCheck> checks,
        string flagKey,
        string passValue,
        string statusWhenPass,
        string passMessage,
        string warningWhenMissing)
    {
        var value = _global.Get(flagKey);
        if (value == passValue)
        {
            checks.Add(new ProductionPilotReadinessCheck
            {
                Area    = "Flags",
                Name    = flagKey,
                Status  = statusWhenPass,
                Message = passMessage,
            });
        }
        else
        {
            checks.Add(new ProductionPilotReadinessCheck
            {
                Area    = "Flags",
                Name    = flagKey,
                Status  = StatusWarning,
                Message = warningWhenMissing,
            });
        }
    }

    // Destructive-UI flags are intentionally off most of the time. An off
    // flag is Info (expected), an on flag is Warning (an operator left
    // destructive UI actionable outside an operation window).
    private void AddDestructiveUiFlagCheck(
        System.Collections.Generic.List<ProductionPilotReadinessCheck> checks,
        string flagKey,
        string operationLabel)
    {
        var value = _global.Get(flagKey);
        var on = value == "1";
        checks.Add(new ProductionPilotReadinessCheck
        {
            Area    = "Flags",
            Name    = flagKey,
            Status  = on ? StatusWarning : StatusInfo,
            Message = on
                ? $"{operationLabel} UI is actionable — make sure this is intentional for the current operation window."
                : $"{operationLabel} UI is off (expected default).",
        });
    }

    private static void CheckRunbook(
        System.Collections.Generic.List<ProductionPilotReadinessCheck> checks,
        string docsDir,
        string fileName)
    {
        try
        {
            var full = Path.Combine(docsDir, fileName);
            if (File.Exists(full))
            {
                checks.Add(new ProductionPilotReadinessCheck
                {
                    Area    = "Runbooks",
                    Name    = fileName,
                    Status  = StatusInfo,
                    Message = $"Runbook found at {full}.",
                });
            }
            else
            {
                checks.Add(new ProductionPilotReadinessCheck
                {
                    Area    = "Runbooks",
                    Name    = fileName,
                    Status  = StatusWarning,
                    Message = $"Runbook not found alongside app at {full} — operator must read it from the source repo.",
                });
            }
        }
        catch (System.Exception ex)
        {
            checks.Add(new ProductionPilotReadinessCheck
            {
                Area    = "Runbooks",
                Name    = fileName,
                Status  = StatusUnknown,
                Message = $"Cannot probe runbook file: {ex.Message}",
            });
        }
    }

    // ── Backend permission snapshot → readiness checks (Phase 10.19E) ────
    //
    // Severity rule: when enforcement is enabled, snapshot failures /
    // denials / metadata mismatches are Blocked. When enforcement is
    // disabled, they remain Warning/Info — visibility without blocking
    // the pilot.

    private static void AddBackendPermissionChecks(
        System.Collections.Generic.List<ProductionPilotReadinessCheck> checks,
        BackendOperatorPermissionSnapshot snap)
    {
        var enforce = snap.EnforcementEnabled;

        // I1 — Enforcement flag state.
        checks.Add(new ProductionPilotReadinessCheck
        {
            Area    = "BackendPermissions",
            Name    = "enforcement-flag",
            Status  = enforce ? StatusPass : StatusInfo,
            Message = enforce
                ? "operator_backend_permission_enforcement_enabled=\"1\" — backend permission preflight is active for dangerous operations."
                : $"operator_backend_permission_enforcement_enabled=\"{snap.EnforcementFlagValue}\" — preflight is OFF. Phase 10.19C visibility-only behaviour.",
        });

        // I2 — Backend identity availability.
        checks.Add(new ProductionPilotReadinessCheck
        {
            Area    = "BackendPermissions",
            Name    = "identity-available",
            Status  = snap.IdentityAvailable
                ? StatusPass
                : (enforce ? StatusBlocked : StatusWarning),
            Message = snap.IdentityAvailable
                ? $"Backend identity reachable (role={snap.Role ?? "n/a"}, userId={snap.UserId?.ToString() ?? "n/a"})."
                : "Backend identity unreachable (offline / 401 / 5xx).",
        });

        // I3 — Backend permissions availability.
        checks.Add(new ProductionPilotReadinessCheck
        {
            Area    = "BackendPermissions",
            Name    = "permissions-available",
            Status  = snap.PermissionsAvailable
                ? StatusPass
                : (enforce ? StatusBlocked : StatusWarning),
            Message = snap.PermissionsAvailable
                ? $"Backend permissions reachable ({snap.PermissionCount} total, {snap.DangerousPermissionCount} dangerous, {snap.ReadOnlyPermissionCount} read-only)."
                : "Backend permissions unreachable.",
        });

        // I4 — Backend role.
        var role = snap.Role ?? "";
        if (string.Equals(role, "CASHIER", System.StringComparison.OrdinalIgnoreCase))
        {
            checks.Add(new ProductionPilotReadinessCheck
            {
                Area    = "BackendPermissions",
                Name    = "role",
                Status  = enforce ? StatusBlocked : StatusWarning,
                Message = "Backend reports CASHIER role for this user. CASHIER must not execute operator-maintenance actions.",
            });
        }
        else if (!string.IsNullOrWhiteSpace(role))
        {
            checks.Add(new ProductionPilotReadinessCheck
            {
                Area    = "BackendPermissions",
                Name    = "role",
                Status  = StatusPass,
                Message = $"Backend role: {role}.",
            });
        }
        else
        {
            checks.Add(new ProductionPilotReadinessCheck
            {
                Area    = "BackendPermissions",
                Name    = "role",
                Status  = enforce ? StatusBlocked : StatusInfo,
                Message = "Backend role unknown (identity not available).",
            });
        }

        // I5 — operator.dashboard.open.
        var hasDashboardOpen = snap.Permissions.Contains("operator.dashboard.open");
        checks.Add(new ProductionPilotReadinessCheck
        {
            Area    = "BackendPermissions",
            Name    = "operator.dashboard.open",
            Status  = hasDashboardOpen
                ? StatusPass
                : (enforce ? StatusBlocked : StatusWarning),
            Message = hasDashboardOpen
                ? "Backend grants operator.dashboard.open."
                : "Backend permission operator.dashboard.open is missing while the dashboard is open.",
        });

        // I6 — Dangerous-permission validations.
        AddValidationCheck(checks, snap.MigrationExecute,       enforce);
        AddValidationCheck(checks, snap.CutoverExecute,         enforce);
        AddValidationCheck(checks, snap.RollbackExecute,        enforce);
        AddValidationCheck(checks, snap.RetentionCleanupExecute, enforce);

        // I7 — Permission expiry.
        if (snap.PermissionsExpiresAt is { } exp)
        {
            if (exp < System.DateTime.UtcNow)
            {
                checks.Add(new ProductionPilotReadinessCheck
                {
                    Area    = "BackendPermissions",
                    Name    = "permissions-expiry",
                    Status  = enforce ? StatusBlocked : StatusWarning,
                    Message = $"Backend permission claim is expired (expiresAt={exp:o}).",
                });
            }
            else
            {
                checks.Add(new ProductionPilotReadinessCheck
                {
                    Area    = "BackendPermissions",
                    Name    = "permissions-expiry",
                    Status  = StatusInfo,
                    Message = $"Backend permission claim valid until {exp:o}.",
                });
            }
        }

        // Forward snapshot-level warnings/errors as their own checks so the
        // existing Aggregate loop catches them.
        foreach (var w in snap.Warnings)
        {
            checks.Add(new ProductionPilotReadinessCheck
            {
                Area    = "BackendPermissions",
                Name    = "snapshot-warning",
                Status  = StatusWarning,
                Message = w,
            });
        }
        foreach (var e in snap.Errors)
        {
            checks.Add(new ProductionPilotReadinessCheck
            {
                Area    = "BackendPermissions",
                Name    = "snapshot-error",
                Status  = enforce ? StatusBlocked : StatusWarning,
                Message = e,
            });
        }
    }

    private static void AddValidationCheck(
        System.Collections.Generic.List<ProductionPilotReadinessCheck> checks,
        BackendDangerousPermissionValidationSnapshot v,
        bool enforce)
    {
        string status = v.Status switch
        {
            "Allowed"          => StatusPass,
            "Denied"           => enforce ? StatusBlocked : StatusWarning,
            "Unavailable"      => enforce ? StatusBlocked : StatusWarning,
            "MetadataMismatch" => enforce ? StatusBlocked : StatusWarning,
            _                   => enforce ? StatusBlocked : StatusInfo,
        };

        string message = v.Status switch
        {
            "Allowed" => $"Backend allows {v.PermissionKey} with full metadata.",
            "Denied"  => $"Backend denies {v.PermissionKey}: {v.Reason ?? "no reason"}.",
            "Unavailable"      => $"Backend validate unavailable for {v.PermissionKey}.",
            "MetadataMismatch" => $"Backend allows {v.PermissionKey} but metadata does not require local flag + confirmation phrase + guarded wrapper.",
            _ => $"Backend validation not attempted for {v.PermissionKey}.",
        };

        checks.Add(new ProductionPilotReadinessCheck
        {
            Area    = "BackendPermissions",
            Name    = v.PermissionKey,
            Status  = status,
            Message = message,
        });
    }

    private static string ComposeBackendPermissionSummary(BackendOperatorPermissionSnapshot snap)
    {
        var status = snap.EnforcementEnabled ? "ON" : "OFF";
        var ident  = snap.IdentityAvailable ? "available" : "unavailable";
        var perms  = snap.PermissionsAvailable
            ? $"{snap.PermissionCount}/{snap.DangerousPermissionCount}/{snap.ReadOnlyPermissionCount} (total/dangerous/readonly)"
            : "unavailable";
        var validations =
            $"migrate={snap.MigrationExecute.Status}, " +
            $"cutover={snap.CutoverExecute.Status}, " +
            $"rollback={snap.RollbackExecute.Status}, " +
            $"cleanup={snap.RetentionCleanupExecute.Status}";
        return $"Enforcement={status}; identity={ident}; permissions={perms}; validations={validations}.";
    }

    private static string? TryExport(ProductionPilotReadinessReport report)
    {
        try
        {
            var logDir = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "PosSystem", "logs", ReadinessLogSubdir);
            Directory.CreateDirectory(logDir);

            var stamp     = System.DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
            var basePath  = Path.Combine(logDir, $"pilot-readiness-{stamp}.json");
            var finalPath = ResolveNonCollidingPath(basePath);

            var raw  = JsonSerializer.Serialize(report, JsonOptions);
            var safe = MigrationAuditLogger.RedactSecrets(raw);
            safe     = ScrubConfirmationPhrases(safe);

            var tmp = finalPath + ".tmp";
            File.WriteAllText(tmp, safe);
            if (File.Exists(finalPath))
                File.Replace(tmp, finalPath, destinationBackupFileName: null);
            else
                File.Move(tmp, finalPath);

            return finalPath;
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveNonCollidingPath(string basePath)
    {
        if (!File.Exists(basePath)) return basePath;

        var dir  = Path.GetDirectoryName(basePath) ?? "";
        var name = Path.GetFileNameWithoutExtension(basePath);
        var ext  = Path.GetExtension(basePath);

        for (int i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{name}-{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
        return Path.Combine(dir, $"{name}-{System.DateTime.UtcNow.Ticks}{ext}");
    }

    private static string ScrubConfirmationPhrases(string json)
    {
        if (string.IsNullOrEmpty(json)) return json;
        return json
            .Replace(PhraseCleanup,         "<redacted-confirmation-phrase>")
            .Replace(PhraseRollbackWrapper, "<redacted-confirmation-phrase>")
            .Replace(PhraseRollbackInner,   "<redacted-confirmation-phrase>")
            .Replace(PhraseRollbackLegacy,  "<redacted-confirmation-phrase>")
            .Replace(PhraseMigration,       "<redacted-confirmation-phrase>")
            .Replace(PhraseRuntime,         "<redacted-confirmation-phrase>");
    }
}
