using System.IO;
using System.Text.Json;
using PosSystem.Data.Repositories;

namespace PosSystem.Services;

// ── Options / Result DTOs (Phase 10.14A) ─────────────────────────────────────

public sealed class GuardedRollbackExecutionOptions
{
    public bool   Force                          { get; init; }

    // Must equal
    // GuardedRollbackExecutorService.RequiredConfirmationPhrase verbatim.
    // The raw value is NEVER logged or copied into the result.
    public string? ConfirmationPhrase            { get; init; }

    public bool   ExternalBackupAcknowledged     { get; init; }
    public string? ExternalBackupNote            { get; init; }

    // When rollback readiness is ReadyWithWarnings, this must be true for
    // execution to proceed.
    public bool   AllowWarnings                  { get; init; }

    // Paths to the preflight + inventory bundles the operator reviewed. Both
    // must exist, be .json, live under the expected logs subdirectories, and
    // be modified within the last 7 days. Prefix tricks rejected.
    public string? ReviewedPreflightExportPath   { get; init; }
    public string? ReviewedInventoryExportPath   { get; init; }

    // Passed through to TenantDbRollbackExecutor. Defaults mirror that
    // executor's defaults so a caller can opt in / out without re-reading
    // the inner executor's contract.
    public bool   ArchiveTenantsDirectory        { get; init; } = true;
    public bool   DisableRuntimeFlag             { get; init; } = true;
    public bool   RestoreLegacyFromBackupIfMissing { get; init; } = false;

    public bool   WriteAuditLog                  { get; init; } = true;
}

public sealed class GuardedRollbackExecutionResult
{
    public System.DateTime StartedAtUtc            { get; init; }
    public System.DateTime CompletedAtUtc          { get; init; }

    // Rejected | Success | Failed
    public string Outcome                          { get; init; } = "Rejected";

    // True only when the underlying TenantDbRollbackExecutor was actually
    // invoked. Stays false for every rejection (no rollback mutation can
    // happen before all wrapper guards pass).
    public bool   RollbackExecuted                 { get; init; }

    public string? RollbackReadinessStatus        { get; init; }

    public bool   ConfirmationPhraseAccepted      { get; init; }
    public bool   ExternalBackupAcknowledged      { get; init; }

    public string? ReviewedPreflightExportPath    { get; init; }
    public string? ReviewedInventoryExportPath    { get; init; }

    public bool   RuntimeTenantDbEnabledBefore    { get; init; }
    public bool   RuntimeTenantDbEnabledAfter     { get; init; }
    public bool   IsProviderTenantScopedBefore    { get; init; }

    public System.Collections.Generic.List<string> Steps           { get; init; } = new();
    public System.Collections.Generic.List<string> BlockingReasons { get; init; } = new();
    public System.Collections.Generic.List<string> Warnings        { get; init; } = new();
    public System.Collections.Generic.List<string> Errors          { get; init; } = new();

    // Curated, structural subset of the inner executor's TenantDbRollbackResult.
    // Never includes raw tokens or phrases. Null when the inner executor was
    // not invoked.
    public object? RollbackResultSummary          { get; init; }
}

// ── Service ──────────────────────────────────────────────────────────────────

// Strict wrapper that's the ONLY legitimate code path to call
// TenantDbRollbackExecutor.ExecuteAsync with DryRunOnly=false in this
// codebase. Phase 10.14A introduces the wrapper in DI; no UI surface, no
// auto-invocation. A future operator UI / CLI must construct
// GuardedRollbackExecutionOptions and call ExecuteAsync explicitly.
//
// Guard sequence (all collected, then evaluated together):
//   1. Force=true
//   2. ConfirmationPhrase == RequiredConfirmationPhrase (ordinal, case-sensitive)
//   3. ExternalBackupAcknowledged=true
//   4. ReviewedPreflightExportPath + ReviewedInventoryExportPath both exist,
//      end in .json, live under the expected logs subdirs (prefix-trick safe),
//      mtime within 7 days
//   5. Runtime / provider state: provider not tenant-scoped, runtime flag on
//      OR readiness signals rollback meaningful
//   6. TenantDbRollbackReadinessChecker verdict allows execution
//   7. Diagnostics report obtainable, no pending sales, no poison sales
//
// If any guard fails: Outcome=Rejected, RollbackExecuted=false, NO inner
// executor call. If every guard passes: constructs real-run inner options
// (DryRunOnly=false + Force=true + ConfirmationPhrase=TenantDbRollbackExecutor.RequiredConfirmationPhrase
// + pass-through flags) and invokes the inner executor exactly once.
//
// What this wrapper NEVER does:
//   • Invoke the migrator (SharedToTenantDatabaseMigrator is not injected).
//   • Switch the path provider (TenantScopeService is not injected;
//     ILocalDatabasePathProvider.UseTenantDatabase / UseLegacyDatabase is
//     never called).
//   • Auto-logout / auto-restart / auto-relaunch.
//   • Log the wrapper's or the inner executor's raw confirmation phrase.
//   • Delete files beyond what the inner executor does + this wrapper's
//     own audit log write.
public sealed class GuardedRollbackExecutorService
{
    public const string RequiredConfirmationPhrase = "EXECUTE_TENANT_DB_RUNTIME_ROLLBACK";

    public const string OutcomeRejected = "Rejected";
    public const string OutcomeSuccess  = "Success";
    public const string OutcomeFailed   = "Failed";

    private const string RuntimeFlagKey      = "tenant_db_runtime_enabled";
    private const int    RecentExportDays    = 7;
    private const string PreflightSubdir     = "preflight";
    private const string InventorySubdir     = "inventory";
    private const string ExecutorLogSubdir   = "rollback-executor";

    // Phrases scrubbed from every audit log entry as defense in depth.
    private const string PhraseRollbackWrapper = "EXECUTE_TENANT_DB_RUNTIME_ROLLBACK";
    private const string PhraseRollbackInner   = "ROLLBACK_TO_LEGACY_POS_DB";
    private const string PhraseRollbackLegacy  = "I UNDERSTAND TENANT DB ROLLBACK";
    private const string PhraseMigration       = "EXECUTE_REAL_TENANT_DB_MIGRATION";
    private const string PhraseRuntime         = "ENABLE_TENANT_DB_RUNTIME_MODE";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly TenantDbRollbackReadinessChecker  _readinessChecker;
    private readonly TenantDbRollbackExecutor          _rollbackExecutor;
    private readonly OperatorDiagnosticsService        _diagnostics;
    private readonly GlobalSettingsRepository          _global;
    private readonly Data.ILocalDatabasePathProvider   _paths;

    public GuardedRollbackExecutorService(
        TenantDbRollbackReadinessChecker readinessChecker,
        TenantDbRollbackExecutor rollbackExecutor,
        OperatorDiagnosticsService diagnostics,
        GlobalSettingsRepository global,
        Data.ILocalDatabasePathProvider paths)
    {
        _readinessChecker = readinessChecker;
        _rollbackExecutor = rollbackExecutor;
        _diagnostics      = diagnostics;
        _global           = global;
        _paths            = paths;
    }

    public async System.Threading.Tasks.Task<GuardedRollbackExecutionResult> ExecuteAsync(
        GuardedRollbackExecutionOptions options,
        System.Threading.CancellationToken ct = default)
    {
        if (options is null) throw new System.ArgumentNullException(nameof(options));

        var started  = System.DateTime.UtcNow;
        var steps    = new System.Collections.Generic.List<string>();
        var blockers = new System.Collections.Generic.List<string>();
        var warnings = new System.Collections.Generic.List<string>();
        var errors   = new System.Collections.Generic.List<string>();

        // Snapshot runtime + provider state at the very top so we can report
        // a precise before/after even on a Rejected outcome.
        var runtimeFlagBefore        = _global.Get(RuntimeFlagKey) == "1";
        var providerTenantScopedBefore = _paths.IsTenantScoped;

        // ── Guard 1 — Force. ────────────────────────────────────────────────
        if (!options.Force)
            blockers.Add("Rollback requires Force=true.");
        else
            steps.Add("Guard passed: Force=true.");

        // ── Guard 2 — Confirmation phrase (ordinal, case-sensitive). ────────
        var phraseAccepted = string.Equals(
            options.ConfirmationPhrase,
            RequiredConfirmationPhrase,
            System.StringComparison.Ordinal);
        if (!phraseAccepted)
            blockers.Add(
                "Rollback requires the exact wrapper ConfirmationPhrase " +
                "(see GuardedRollbackExecutorService.RequiredConfirmationPhrase). " +
                "This deliberate double-confirmation prevents accidental rollback.");
        else
            steps.Add("Guard passed: wrapper confirmation phrase accepted.");

        // ── Guard 3 — External backup acknowledgement. ──────────────────────
        if (!options.ExternalBackupAcknowledged)
        {
            blockers.Add(
                "Rollback requires ExternalBackupAcknowledged=true. " +
                "Operator must capture an off-machine backup of " +
                @"%LocalAppData%\PosSystem before proceeding.");
        }
        else
        {
            steps.Add("Guard passed: external backup acknowledged.");
            if (string.IsNullOrWhiteSpace(options.ExternalBackupNote))
                warnings.Add("ExternalBackupAcknowledged=true but no ExternalBackupNote was provided.");
        }

        // ── Guard 4 — Reviewed export files (preflight + inventory). ────────
        var legacyDb = _paths.GetLegacyDbPath();
        var baseDir  = Path.GetDirectoryName(legacyDb)
                       ?? Path.Combine(
                              System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                              "PosSystem");
        var expectedPreflightDir = Path.Combine(baseDir, "logs", PreflightSubdir);
        var expectedInventoryDir = Path.Combine(baseDir, "logs", InventorySubdir);

        if (ValidateReviewedExport(
                "Preflight export",
                options.ReviewedPreflightExportPath,
                expectedPreflightDir,
                blockers))
        {
            steps.Add("Guard passed: reviewed preflight export is recent and under expected directory.");
        }
        if (ValidateReviewedExport(
                "Inventory export",
                options.ReviewedInventoryExportPath,
                expectedInventoryDir,
                blockers))
        {
            steps.Add("Guard passed: reviewed inventory export is recent and under expected directory.");
        }

        // ── Guard 5 — Runtime / provider state. ─────────────────────────────
        // The inner executor refuses when the provider is tenant-scoped
        // (Phase 10.6B.2). Surface that here so the operator sees the precise
        // blocker before any executor call.
        if (providerTenantScopedBefore)
        {
            blockers.Add(
                "Direct check: path provider is currently tenant-scoped. Restart the app in legacy mode before executing rollback.");
        }
        else
        {
            steps.Add("Guard passed: path provider is not tenant-scoped.");
        }

        // ── Guard 6 — Rollback readiness. ───────────────────────────────────
        TenantDbRollbackReadinessReport? readinessReport = null;
        try
        {
            readinessReport = _readinessChecker.Check();
            foreach (var w in readinessReport.Warnings) warnings.Add($"Readiness: {w}");

            switch (readinessReport.Status)
            {
                case TenantDbRollbackReadinessStatus.Ready:
                    steps.Add("Guard passed: rollback readiness is Ready.");
                    break;
                case TenantDbRollbackReadinessStatus.ReadyWithWarnings when options.AllowWarnings:
                    steps.Add("Guard passed: rollback readiness is ReadyWithWarnings, AllowWarnings=true.");
                    break;
                case TenantDbRollbackReadinessStatus.ReadyWithWarnings:
                    blockers.Add(
                        "Rollback readiness is ReadyWithWarnings but options.AllowWarnings=false. " +
                        "Set AllowWarnings=true to proceed despite warnings.");
                    break;
                case TenantDbRollbackReadinessStatus.Blocked:
                    blockers.Add($"Rollback readiness is {readinessReport.Status} — see warnings: {string.Join("; ", readinessReport.Warnings)}.");
                    break;
                case TenantDbRollbackReadinessStatus.NotInTenantRuntimeMode:
                    blockers.Add(
                        "Rollback readiness is NotInTenantRuntimeMode — runtime tenant DB mode is already off, " +
                        "so an explicit rollback is not meaningful.");
                    break;
                default:
                    blockers.Add($"Unknown rollback readiness status: {readinessReport.Status}.");
                    break;
            }
        }
        catch (System.Exception ex)
        {
            blockers.Add($"Rollback readiness check failed: {ex.Message}");
        }

        // ── Guard 7 — Diagnostics (pending/poison sales). ───────────────────
        try
        {
            // Tenant subdomain is not part of rollback options; the
            // diagnostics service falls back to last_tenant_subdomain via Get
            // when null is passed.
            var diag = await _diagnostics.GetReportAsync(null, ct);
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
                if (diag.Sales.PendingSalesCount == 0 && diag.Sales.PoisonSalesCount == 0)
                    steps.Add("Guard passed: no pending/poison sales.");
                foreach (var w in diag.Warnings) warnings.Add($"Diagnostics: {w}");
            }
        }
        catch (System.Exception ex)
        {
            blockers.Add($"Diagnostics subsystem failed: {ex.Message}");
        }

        // ── Evaluate guards. Any blocker → reject without invoking executor.
        if (blockers.Count > 0)
        {
            steps.Add($"Rejected at guard stage: {blockers.Count} blocker(s). No rollback executed.");
            var rejected = new GuardedRollbackExecutionResult
            {
                StartedAtUtc                  = started,
                CompletedAtUtc                = System.DateTime.UtcNow,
                Outcome                       = OutcomeRejected,
                RollbackExecuted              = false,
                RollbackReadinessStatus       = readinessReport?.Status.ToString(),
                ConfirmationPhraseAccepted    = phraseAccepted,
                ExternalBackupAcknowledged    = options.ExternalBackupAcknowledged,
                ReviewedPreflightExportPath   = options.ReviewedPreflightExportPath,
                ReviewedInventoryExportPath   = options.ReviewedInventoryExportPath,
                RuntimeTenantDbEnabledBefore  = runtimeFlagBefore,
                RuntimeTenantDbEnabledAfter   = runtimeFlagBefore,   // unchanged
                IsProviderTenantScopedBefore  = providerTenantScopedBefore,
                Steps                         = steps,
                BlockingReasons               = blockers,
                Warnings                      = warnings,
                Errors                        = errors,
                RollbackResultSummary         = null,
            };
            WriteAuditLogIfRequested(options, rejected);
            return rejected;
        }

        // ── All guards passed — construct real inner-executor options.
        // These flags are hardcoded and never flow from UI/options paths.
        var innerOptions = new TenantDbRollbackOptions
        {
            DryRunOnly                       = false,
            Force                            = true,
            ConfirmationPhrase               = TenantDbRollbackExecutor.RequiredConfirmationPhrase,
            ArchiveTenantsDirectory          = options.ArchiveTenantsDirectory,
            DisableRuntimeFlag               = options.DisableRuntimeFlag,
            RestoreLegacyFromBackupIfMissing = options.RestoreLegacyFromBackupIfMissing,
            WriteAuditLog                    = options.WriteAuditLog,
        };
        steps.Add(
            "All guards passed; constructed inner rollback options " +
            "(DryRunOnly=false, Force=true, ConfirmationPhrase supplied to inner executor).");

        // ── Execute rollback. Exactly one call. ─────────────────────────────
        TenantDbRollbackResult? innerResult = null;
        var executed = false;
        var outcome  = OutcomeFailed;
        try
        {
            executed = true;
            steps.Add("Calling TenantDbRollbackExecutor.ExecuteAsync exactly once.");
            innerResult = await _rollbackExecutor.ExecuteAsync(innerOptions, ct);
            steps.Add($"Inner executor returned: Outcome={innerResult.Outcome}, DryRun={innerResult.DryRun}.");

            outcome = innerResult.Outcome switch
            {
                TenantDbRollbackOutcome.Success => OutcomeSuccess,
                _                                => OutcomeFailed,
            };
            if (outcome == OutcomeFailed && !string.IsNullOrEmpty(innerResult.FailureReason))
                errors.Add(innerResult.FailureReason!);
        }
        catch (System.Exception ex)
        {
            outcome = OutcomeFailed;
            errors.Add($"Inner rollback executor threw: {ex.Message}");
            steps.Add("Exception raised; no automatic migration / no DB switch / no logout/restart.");
        }

        // Re-read runtime flag after the inner executor call to compute the
        // true "after" state. The inner executor may have set it to "0".
        var runtimeFlagAfter = _global.Get(RuntimeFlagKey) == "1";

        var finalResult = new GuardedRollbackExecutionResult
        {
            StartedAtUtc                  = started,
            CompletedAtUtc                = System.DateTime.UtcNow,
            Outcome                       = outcome,
            RollbackExecuted              = executed,
            RollbackReadinessStatus       = readinessReport?.Status.ToString(),
            ConfirmationPhraseAccepted    = phraseAccepted,
            ExternalBackupAcknowledged    = options.ExternalBackupAcknowledged,
            ReviewedPreflightExportPath   = options.ReviewedPreflightExportPath,
            ReviewedInventoryExportPath   = options.ReviewedInventoryExportPath,
            RuntimeTenantDbEnabledBefore  = runtimeFlagBefore,
            RuntimeTenantDbEnabledAfter   = runtimeFlagAfter,
            IsProviderTenantScopedBefore  = providerTenantScopedBefore,
            Steps                         = steps,
            BlockingReasons               = blockers,
            Warnings                      = warnings,
            Errors                        = errors,
            RollbackResultSummary         = innerResult is null ? null : BuildRollbackResultSummary(innerResult),
        };

        WriteAuditLogIfRequested(options, finalResult);
        return finalResult;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool ValidateReviewedExport(
        string label,
        string? path,
        string expectedDirectory,
        System.Collections.Generic.List<string> blockers)
    {
        var startCount = blockers.Count;

        if (string.IsNullOrWhiteSpace(path))
        {
            blockers.Add($"{label}: ReviewedExportPath is required.");
            return false;
        }

        if (!path.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
            blockers.Add($"{label}: must have .json extension ({path}).");

        FileInfo fi;
        try
        {
            fi = new FileInfo(path);
        }
        catch (System.Exception ex)
        {
            blockers.Add($"{label}: cannot stat {path}: {ex.Message}");
            return false;
        }

        if (!fi.Exists)
        {
            blockers.Add($"{label}: file does not exist ({path}).");
            return false;
        }

        if (!IsUnder(fi.FullName, expectedDirectory))
        {
            blockers.Add(
                $"{label}: file must live under {expectedDirectory} (was {fi.FullName}). " +
                "Prefix-similar paths (e.g. <expected>_evil\\...) are rejected.");
        }

        var age = (System.DateTime.UtcNow - fi.LastWriteTimeUtc).TotalDays;
        if (age > RecentExportDays)
        {
            blockers.Add(
                $"{label}: file is older than {RecentExportDays} days " +
                $"({(int)age}d old, mtime {fi.LastWriteTimeUtc:o}).");
        }

        return blockers.Count == startCount;
    }

    private static bool IsUnder(string filePath, string directoryPath)
    {
        try
        {
            var normalizedFile = Path.GetFullPath(filePath);
            var normalizedDir  = EnsureTrailingDirectorySeparator(Path.GetFullPath(directoryPath));
            return normalizedFile.StartsWith(normalizedDir, System.StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string EnsureTrailingDirectorySeparator(string path)
        => Path.TrimEndingDirectorySeparator(path) + Path.DirectorySeparatorChar;

    // Curated, structural subset of the inner executor's result. Picks safe
    // fields meaningful in operator review; raw tokens / phrases are not
    // structurally present.
    private static object BuildRollbackResultSummary(TenantDbRollbackResult r)
        => new
        {
            Outcome                      = r.Outcome.ToString(),
            r.DryRun,
            r.FailureReason,
            r.LegacyDbPath,
            r.TenantsDirectory,
            r.ArchivedTenantsPath,
            r.GlobalSettingsPath,
            r.BackupUsedPath,
            r.BrokenLegacyRenamedTo,
            r.RuntimeFlagWasEnabled,
            r.RuntimeFlagDisabledByThisRun,
            r.TenantsDirectoryArchived,
            r.LegacyRestoredFromBackup,
            Steps                        = r.Steps.ToList(),
            r.AuditLogPath,
            r.StartedAtUtc,
            r.CompletedAtUtc,
        };

    // ── Wrapper audit log writer ─────────────────────────────────────────────
    //
    // Writes one JSON entry per ExecuteAsync invocation under
    //   %LocalAppData%\PosSystem\logs\rollback-executor\
    // Sanitized options view contains only the strict allow-list. The raw
    // wrapper phrase + inner executor phrase never cross this boundary.
    // RedactSecrets + multi-phrase scrub provide defense in depth.
    private static void WriteAuditLogIfRequested(
        GuardedRollbackExecutionOptions options,
        GuardedRollbackExecutionResult result)
    {
        if (!options.WriteAuditLog) return;

        try
        {
            var logDir = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "PosSystem", "logs", ExecutorLogSubdir);
            Directory.CreateDirectory(logDir);

            var stamp = System.DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
            var path  = Path.Combine(logDir, $"rollback-executor-{stamp}-{result.Outcome}.json");

            // Strict allow-list. ExternalBackupNote and TenantSubdomain (n/a
            // here) are deliberately NOT in the options projection.
            var safeOptions = new
            {
                options.Force,
                ConfirmationPhraseProvided       = !string.IsNullOrEmpty(options.ConfirmationPhrase),
                ConfirmationPhraseAccepted       = result.ConfirmationPhraseAccepted,
                options.ExternalBackupAcknowledged,
                ExternalBackupNoteProvided       = !string.IsNullOrEmpty(options.ExternalBackupNote),
                options.AllowWarnings,
                options.WriteAuditLog,
                options.ReviewedPreflightExportPath,
                options.ReviewedInventoryExportPath,
                options.ArchiveTenantsDirectory,
                options.DisableRuntimeFlag,
                options.RestoreLegacyFromBackupIfMissing,
            };

            var entry = new
            {
                TimestampUtc = System.DateTime.UtcNow,
                MachineName  = System.Environment.MachineName,
                OsUser       = System.Environment.UserName,
                Options      = safeOptions,
                Result       = result,
            };

            var raw  = JsonSerializer.Serialize(entry, JsonOptions);
            var safe = MigrationAuditLogger.RedactSecrets(raw);
            safe     = ScrubConfirmationPhrases(safe);

            var tmp = path + ".tmp";
            File.WriteAllText(tmp, safe);
            if (File.Exists(path)) File.Replace(tmp, path, destinationBackupFileName: null);
            else File.Move(tmp, path);
        }
        catch
        {
            // Best effort — audit log write must not influence execution outcome.
        }
    }

    private static string ScrubConfirmationPhrases(string json)
    {
        if (string.IsNullOrEmpty(json)) return json;
        return json
            .Replace(PhraseRollbackWrapper, "<redacted-confirmation-phrase>")
            .Replace(PhraseRollbackInner,   "<redacted-confirmation-phrase>")
            .Replace(PhraseRollbackLegacy,  "<redacted-confirmation-phrase>")
            .Replace(PhraseMigration,       "<redacted-confirmation-phrase>")
            .Replace(PhraseRuntime,         "<redacted-confirmation-phrase>");
    }
}
