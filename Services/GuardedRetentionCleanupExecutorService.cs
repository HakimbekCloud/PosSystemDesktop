using System.IO;
using System.Text.Json;
using PosSystem.Data.Repositories;

namespace PosSystem.Services;

// ── Options / Result DTOs (Phase 10.16A) ─────────────────────────────────────

public sealed class GuardedRetentionCleanupExecutionOptions
{
    public bool   Force                          { get; init; }

    // Must equal
    // GuardedRetentionCleanupExecutorService.RequiredConfirmationPhrase verbatim.
    // The raw value is NEVER logged or copied into the result.
    public string? ConfirmationPhrase            { get; init; }

    public bool   ExternalBackupAcknowledged     { get; init; }
    public string? ExternalBackupNote            { get; init; }

    // Path to a recently-reviewed inventory bundle (Phase 10.11C). Must
    // exist, be .json, live under logs\inventory\, and be < 7 days old.
    // Prefix tricks (`inventory_evil\…`) are rejected.
    public string? ReviewedInventoryExportPath   { get; init; }

    // Per-category opt-ins. Candidates from a disabled category are reported
    // as Skipped (Reason: option disabled) without deletion.
    public bool   DeleteDiagnosticsLogs           { get; init; } = true;
    public bool   DeletePreflightLogs             { get; init; } = true;
    public bool   DeleteMigrationLogs             { get; init; } = true;
    public bool   DeleteRollbackLogs              { get; init; } = true;
    public bool   DeleteOldBackups                { get; init; } = true;
    public bool   DeleteArchivedTenantDirectories { get; init; } = true;
    public bool   DeleteBrokenLegacyDbFiles       { get; init; } = true;

    public bool   WriteAuditLog                   { get; init; } = true;
}

public sealed class RetentionCleanupItemResult
{
    public string         Category          { get; init; } = "";
    public string         Path              { get; init; } = "";
    public string         Name              { get; init; } = "";
    public long           SizeBytes         { get; init; }

    // Deleted | Skipped | Failed
    public string         Action            { get; init; } = "";
    public string         Reason            { get; init; } = "";
    public string?        Error             { get; init; }
}

public sealed class GuardedRetentionCleanupExecutionResult
{
    public System.DateTime StartedAtUtc           { get; init; }
    public System.DateTime CompletedAtUtc         { get; init; }

    // Rejected | NoOp | Success | Failed
    public string Outcome                         { get; init; } = "Rejected";

    // True only when at least one delete was actually attempted (Deleted +
    // Failed). Stays false for Rejected and NoOp.
    public bool   CleanupExecuted                 { get; init; }

    public bool   ConfirmationPhraseAccepted      { get; init; }
    public bool   ExternalBackupAcknowledged      { get; init; }
    public string? ReviewedInventoryExportPath    { get; init; }

    public int    CandidateCountBefore            { get; init; }
    public long   CandidateBytesBefore            { get; init; }
    public string CandidateSizeTextBefore         { get; init; } = "";

    public int    DeletedItemCount                { get; init; }
    public long   DeletedBytes                    { get; init; }
    public string DeletedSizeText                 { get; init; } = "";

    public int    SkippedItemCount                { get; init; }
    public int    FailedItemCount                 { get; init; }

    public System.Collections.Generic.List<string> Steps           { get; init; } = new();
    public System.Collections.Generic.List<string> BlockingReasons { get; init; } = new();
    public System.Collections.Generic.List<string> Warnings        { get; init; } = new();
    public System.Collections.Generic.List<string> Errors          { get; init; } = new();

    public System.Collections.Generic.List<RetentionCleanupItemResult> Items
        { get; init; } = new();
}

// ── Service ──────────────────────────────────────────────────────────────────

// Strict wrapper that's the ONLY legitimate code path that deletes files
// classified as cleanup candidates by TenantDatabaseRetentionPreviewService.
// Phase 10.16A introduces the wrapper in DI; no UI surface, no auto-invocation.
//
// Guard sequence (all collected, then evaluated together):
//   1. Force=true
//   2. ConfirmationPhrase == RequiredConfirmationPhrase
//   3. ExternalBackupAcknowledged=true
//   4. ReviewedInventoryExportPath under logs\inventory\, .json, recent
//   5. Diagnostics: no pending sales, no poison sales
//   6. Retention preview obtainable + non-error; NoOp when CandidateCount==0
//
// If any guard fails: Outcome=Rejected, CleanupExecuted=false, NO deletion.
// On NoOp (no candidates): Outcome=NoOp, CleanupExecuted=false, NO deletion.
// Otherwise: iterates retention candidates, applies category-option filter,
// applies per-item safety re-check, deletes only items that survive both.
// One failure does not stop subsequent items.
//
// What this wrapper NEVER does:
//   • Delete the active legacy pos.db, any tenant DB, any path under the
//     live tenants\ directory, any protected-list path, or any path outside
//     the small allow-list of categorised roots.
//   • Invoke the migrator, rollback executor, runtime cutover executor.
//   • Switch the path provider.
//   • Auto-logout / auto-restart.
//   • Log raw confirmation phrases.
public sealed class GuardedRetentionCleanupExecutorService
{
    public const string RequiredConfirmationPhrase = "EXECUTE_RETENTION_CLEANUP";

    public const string OutcomeRejected = "Rejected";
    public const string OutcomeNoOp     = "NoOp";
    public const string OutcomeSuccess  = "Success";
    public const string OutcomeFailed   = "Failed";

    private const int    RecentExportDays    = 7;
    private const string InventorySubdir     = "inventory";
    private const string CleanupLogSubdir    = "retention-cleanup";

    private const string ActionDeleted = "Deleted";
    private const string ActionSkipped = "Skipped";
    private const string ActionFailed  = "Failed";

    private const string ArchivedTenantsPrefix = "tenants.before-rollback-";
    private const string BrokenLegacyPrefix    = "pos.db.broken-";

    private const string CategoryLogDiagnostics    = "log-diagnostics";
    private const string CategoryLogPreflight      = "log-preflight";
    private const string CategoryLogMigrations     = "log-migrations";
    private const string CategoryLogRollbacks      = "log-rollbacks";
    private const string CategoryBackup            = "backup";
    private const string CategoryArchivedTenants   = "archived-tenants-dir";
    private const string CategoryBrokenLegacyDb    = "broken-legacy-db";

    // Phrases scrubbed from every audit log entry.
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

    private readonly TenantDatabaseRetentionPreviewService _retention;
    private readonly OperatorDiagnosticsService            _diagnostics;
    private readonly GlobalSettingsRepository              _global;
    private readonly Data.ILocalDatabasePathProvider       _paths;

    public GuardedRetentionCleanupExecutorService(
        TenantDatabaseRetentionPreviewService retention,
        OperatorDiagnosticsService diagnostics,
        GlobalSettingsRepository global,
        Data.ILocalDatabasePathProvider paths)
    {
        _retention   = retention;
        _diagnostics = diagnostics;
        _global      = global;
        _paths       = paths;
    }

    public async System.Threading.Tasks.Task<GuardedRetentionCleanupExecutionResult> ExecuteAsync(
        GuardedRetentionCleanupExecutionOptions options,
        System.Threading.CancellationToken ct = default)
    {
        if (options is null) throw new System.ArgumentNullException(nameof(options));

        var started  = System.DateTime.UtcNow;
        var steps    = new System.Collections.Generic.List<string>();
        var blockers = new System.Collections.Generic.List<string>();
        var warnings = new System.Collections.Generic.List<string>();
        var errors   = new System.Collections.Generic.List<string>();

        // ── Guard 1 — Force. ────────────────────────────────────────────────
        if (!options.Force)
            blockers.Add("Retention cleanup requires Force=true.");
        else
            steps.Add("Guard passed: Force=true.");

        // ── Guard 2 — Confirmation phrase (ordinal, case-sensitive). ────────
        var phraseAccepted = string.Equals(
            options.ConfirmationPhrase,
            RequiredConfirmationPhrase,
            System.StringComparison.Ordinal);
        if (!phraseAccepted)
            blockers.Add(
                "Retention cleanup requires the exact ConfirmationPhrase " +
                "(see GuardedRetentionCleanupExecutorService.RequiredConfirmationPhrase). " +
                "This deliberate double-confirmation prevents accidental cleanup.");
        else
            steps.Add("Guard passed: confirmation phrase accepted.");

        // ── Guard 3 — External backup acknowledgement. ──────────────────────
        if (!options.ExternalBackupAcknowledged)
        {
            blockers.Add(
                "Retention cleanup requires ExternalBackupAcknowledged=true. " +
                "Operator must capture an off-machine backup of " +
                @"%LocalAppData%\PosSystem before proceeding.");
        }
        else
        {
            steps.Add("Guard passed: external backup acknowledged.");
            if (string.IsNullOrWhiteSpace(options.ExternalBackupNote))
                warnings.Add("ExternalBackupAcknowledged=true but no ExternalBackupNote was provided.");
        }

        // ── Guard 4 — Reviewed inventory export. ────────────────────────────
        var legacyDb = _paths.GetLegacyDbPath();
        var baseDir  = Path.GetDirectoryName(legacyDb)
                       ?? Path.Combine(
                              System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                              "PosSystem");
        var expectedInventoryDir = Path.Combine(baseDir, "logs", InventorySubdir);

        if (ValidateReviewedExport(
                "Inventory export",
                options.ReviewedInventoryExportPath,
                expectedInventoryDir,
                blockers))
        {
            steps.Add("Guard passed: reviewed inventory export is recent and under expected directory.");
        }

        // ── Guard 5 — Diagnostics (pending/poison sales). ───────────────────
        try
        {
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

        // ── Guard 6 — Retention preview. ────────────────────────────────────
        TenantDatabaseRetentionPreviewReport? preview = null;
        try
        {
            preview = await _retention.PreviewAsync(null, ct);
            if (preview is null)
            {
                blockers.Add("Retention preview report unavailable.");
            }
            else
            {
                if (preview.Errors.Count > 0)
                {
                    foreach (var e in preview.Errors)
                        blockers.Add($"Retention preview: {e}");
                }
                foreach (var w in preview.Warnings) warnings.Add($"Retention preview: {w}");
                steps.Add($"Guard passed: retention preview obtained ({preview.CandidateCount} candidate(s)).");
            }
        }
        catch (System.Exception ex)
        {
            blockers.Add($"Retention preview failed: {ex.Message}");
        }

        // ── Evaluate guards. Any blocker → reject without deleting anything.
        if (blockers.Count > 0)
        {
            steps.Add($"Rejected at guard stage: {blockers.Count} blocker(s). No cleanup attempted.");
            var rejected = new GuardedRetentionCleanupExecutionResult
            {
                StartedAtUtc                = started,
                CompletedAtUtc              = System.DateTime.UtcNow,
                Outcome                     = OutcomeRejected,
                CleanupExecuted             = false,
                ConfirmationPhraseAccepted  = phraseAccepted,
                ExternalBackupAcknowledged  = options.ExternalBackupAcknowledged,
                ReviewedInventoryExportPath = options.ReviewedInventoryExportPath,
                CandidateCountBefore        = preview?.CandidateCount ?? 0,
                CandidateBytesBefore        = preview?.CandidateBytes ?? 0,
                CandidateSizeTextBefore     = preview?.CandidateSizeText ?? "0 B",
                DeletedSizeText             = FormatSize(0),
                Steps                       = steps,
                BlockingReasons             = blockers,
                Warnings                    = warnings,
                Errors                      = errors,
            };
            WriteAuditLogIfRequested(options, rejected);
            return rejected;
        }

        // Guard 6 NoOp: zero candidates.
        if (preview!.CandidateCount == 0)
        {
            steps.Add("Retention preview returned no candidates — nothing to clean.");
            var noop = new GuardedRetentionCleanupExecutionResult
            {
                StartedAtUtc                = started,
                CompletedAtUtc              = System.DateTime.UtcNow,
                Outcome                     = OutcomeNoOp,
                CleanupExecuted             = false,
                ConfirmationPhraseAccepted  = phraseAccepted,
                ExternalBackupAcknowledged  = options.ExternalBackupAcknowledged,
                ReviewedInventoryExportPath = options.ReviewedInventoryExportPath,
                CandidateCountBefore        = 0,
                CandidateBytesBefore        = 0,
                CandidateSizeTextBefore     = preview.CandidateSizeText,
                DeletedSizeText             = FormatSize(0),
                Steps                       = steps,
                BlockingReasons             = blockers,
                Warnings                    = warnings,
                Errors                      = errors,
            };
            WriteAuditLogIfRequested(options, noop);
            return noop;
        }

        // ── All guards passed — iterate candidates with per-item safety. ────
        steps.Add($"Iterating {preview.Candidates.Count} candidate(s).");

        // Build a normalized-path set of every protected item so per-item
        // safety can short-circuit.
        var protectedSet = new System.Collections.Generic.HashSet<string>(
            System.StringComparer.OrdinalIgnoreCase);
        foreach (var p in preview.ProtectedItems)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(p.Path))
                    protectedSet.Add(Path.GetFullPath(p.Path));
            }
            catch
            {
                // Skip unnormalizable protected paths — they can't match a
                // candidate path either.
            }
        }

        string activeLegacyDbFull;
        try { activeLegacyDbFull = Path.GetFullPath(legacyDb); }
        catch { activeLegacyDbFull = legacyDb; }

        var items = new System.Collections.Generic.List<RetentionCleanupItemResult>();
        int deletedCount = 0;
        long deletedBytes = 0;
        int skippedCount  = 0;
        int failedCount   = 0;

        foreach (var cand in preview.Candidates)
        {
            ct.ThrowIfCancellationRequested();

            // Category-option filter.
            if (!IsCategoryEnabled(cand.Category, options))
            {
                items.Add(new RetentionCleanupItemResult
                {
                    Category  = cand.Category,
                    Path      = cand.Path,
                    Name      = cand.Name,
                    SizeBytes = cand.SizeBytes,
                    Action    = ActionSkipped,
                    Reason    = $"Category '{cand.Category}' disabled by options.",
                });
                skippedCount++;
                continue;
            }

            // Per-item safety re-check.
            if (!IsSafeToDelete(cand, baseDir, activeLegacyDbFull, protectedSet, out var skipReason))
            {
                items.Add(new RetentionCleanupItemResult
                {
                    Category  = cand.Category,
                    Path      = cand.Path,
                    Name      = cand.Name,
                    SizeBytes = cand.SizeBytes,
                    Action    = ActionSkipped,
                    Reason    = $"Safety check failed: {skipReason}",
                });
                skippedCount++;
                continue;
            }

            // Delete.
            try
            {
                if (cand.Category == CategoryArchivedTenants)
                {
                    Directory.Delete(cand.Path, recursive: true);
                }
                else
                {
                    File.Delete(cand.Path);
                }

                items.Add(new RetentionCleanupItemResult
                {
                    Category  = cand.Category,
                    Path      = cand.Path,
                    Name      = cand.Name,
                    SizeBytes = cand.SizeBytes,
                    Action    = ActionDeleted,
                    Reason    = $"Candidate deleted from {cand.Category}.",
                });
                deletedCount++;
                deletedBytes += cand.SizeBytes;
            }
            catch (System.Exception ex)
            {
                items.Add(new RetentionCleanupItemResult
                {
                    Category  = cand.Category,
                    Path      = cand.Path,
                    Name      = cand.Name,
                    SizeBytes = cand.SizeBytes,
                    Action    = ActionFailed,
                    Reason    = "Delete threw.",
                    Error     = ex.Message,
                });
                failedCount++;
                errors.Add($"Delete failed for {cand.Path}: {ex.Message}");
            }
        }

        // Outcome classification.
        string outcome;
        bool executed = (deletedCount + failedCount) > 0;
        if (!executed)
        {
            outcome = OutcomeNoOp;
            steps.Add($"All {preview.Candidates.Count} candidate(s) were Skipped — no delete attempt made.");
        }
        else if (failedCount > 0)
        {
            outcome = OutcomeFailed;
            steps.Add(
                $"Cleanup finished with failures: deleted={deletedCount}, " +
                $"failed={failedCount}, skipped={skippedCount}.");
        }
        else
        {
            outcome = OutcomeSuccess;
            steps.Add(
                $"Cleanup completed: deleted={deletedCount}, skipped={skippedCount}.");
        }

        var finalResult = new GuardedRetentionCleanupExecutionResult
        {
            StartedAtUtc                = started,
            CompletedAtUtc              = System.DateTime.UtcNow,
            Outcome                     = outcome,
            CleanupExecuted             = executed,
            ConfirmationPhraseAccepted  = phraseAccepted,
            ExternalBackupAcknowledged  = options.ExternalBackupAcknowledged,
            ReviewedInventoryExportPath = options.ReviewedInventoryExportPath,
            CandidateCountBefore        = preview.CandidateCount,
            CandidateBytesBefore        = preview.CandidateBytes,
            CandidateSizeTextBefore     = preview.CandidateSizeText,
            DeletedItemCount            = deletedCount,
            DeletedBytes                = deletedBytes,
            DeletedSizeText             = FormatSize(deletedBytes),
            SkippedItemCount            = skippedCount,
            FailedItemCount             = failedCount,
            Steps                       = steps,
            BlockingReasons             = blockers,
            Warnings                    = warnings,
            Errors                      = errors,
            Items                       = items,
        };

        WriteAuditLogIfRequested(options, finalResult);
        return finalResult;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool IsCategoryEnabled(
        string category,
        GuardedRetentionCleanupExecutionOptions options)
        => category switch
        {
            CategoryLogDiagnostics  => options.DeleteDiagnosticsLogs,
            CategoryLogPreflight    => options.DeletePreflightLogs,
            CategoryLogMigrations   => options.DeleteMigrationLogs,
            CategoryLogRollbacks    => options.DeleteRollbackLogs,
            CategoryBackup          => options.DeleteOldBackups,
            CategoryArchivedTenants => options.DeleteArchivedTenantDirectories,
            CategoryBrokenLegacyDb  => options.DeleteBrokenLegacyDbFiles,
            _                        => false,
        };

    // Independent per-item safety verification. Re-checks every dangerous
    // invariant — does not trust the preview blindly. Returns false (with a
    // reason) for any item that should not be deleted.
    private static bool IsSafeToDelete(
        RetentionPreviewItem item,
        string baseDir,
        string activeLegacyDbFull,
        System.Collections.Generic.HashSet<string> protectedSet,
        out string skipReason)
    {
        skipReason = "";

        if (string.IsNullOrWhiteSpace(item.Path))
        {
            skipReason = "path is null/empty.";
            return false;
        }

        string fullItemPath;
        try { fullItemPath = Path.GetFullPath(item.Path); }
        catch (System.Exception ex) { skipReason = $"path not normalizable: {ex.Message}"; return false; }

        if (protectedSet.Contains(fullItemPath))
        {
            skipReason = "path appears in retention preview ProtectedItems list.";
            return false;
        }

        if (string.Equals(fullItemPath, activeLegacyDbFull, System.StringComparison.OrdinalIgnoreCase))
        {
            skipReason = "path is the active legacy pos.db.";
            return false;
        }

        // Block any path under the live tenants\ directory. Archived
        // directories are siblings of tenants\, not children, so this check
        // catches anything that drifts into the live tenant tree.
        var tenantsDir = Path.Combine(baseDir, "tenants");
        var tenantsDirWithSep = EnsureTrailingDirectorySeparator(Path.GetFullPath(tenantsDir));
        if (fullItemPath.StartsWith(tenantsDirWithSep, System.StringComparison.OrdinalIgnoreCase))
        {
            skipReason = "path is under the live tenants\\ directory.";
            return false;
        }

        // Existence check just before delete.
        var category = item.Category ?? "";
        if (category == CategoryArchivedTenants)
        {
            if (!Directory.Exists(fullItemPath))
            {
                skipReason = "archived tenant directory does not exist.";
                return false;
            }
            if (!IsArchivedTenantsDirectory(fullItemPath, baseDir))
            {
                skipReason = "path is not a sibling tenants.before-rollback-* directory.";
                return false;
            }
            return true;
        }

        if (!File.Exists(fullItemPath))
        {
            skipReason = "file does not exist.";
            return false;
        }

        switch (category)
        {
            case CategoryLogDiagnostics:
                return IsWithinExpectedRoot(fullItemPath,
                    Path.Combine(baseDir, "logs", "diagnostics"), out skipReason);
            case CategoryLogPreflight:
                return IsWithinExpectedRoot(fullItemPath,
                    Path.Combine(baseDir, "logs", "preflight"), out skipReason);
            case CategoryLogMigrations:
                return IsWithinExpectedRoot(fullItemPath,
                    Path.Combine(baseDir, "logs", "migrations"), out skipReason);
            case CategoryLogRollbacks:
                return IsWithinExpectedRoot(fullItemPath,
                    Path.Combine(baseDir, "logs", "rollbacks"), out skipReason);
            case CategoryBackup:
                return IsWithinExpectedRoot(fullItemPath,
                    Path.Combine(baseDir, "backups"), out skipReason);
            case CategoryBrokenLegacyDb:
                if (!IsBrokenLegacyDbFile(fullItemPath, baseDir))
                {
                    skipReason = "path is not a sibling pos.db.broken-* file.";
                    return false;
                }
                return true;
            default:
                skipReason = $"unknown category '{category}'.";
                return false;
        }
    }

    private static bool IsWithinExpectedRoot(string fullItemPath, string expectedRoot, out string skipReason)
    {
        try
        {
            var expectedRootFull = EnsureTrailingDirectorySeparator(Path.GetFullPath(expectedRoot));
            if (!fullItemPath.StartsWith(expectedRootFull, System.StringComparison.OrdinalIgnoreCase))
            {
                skipReason = $"path is outside allowed root {expectedRoot}.";
                return false;
            }
            skipReason = "";
            return true;
        }
        catch (System.Exception ex)
        {
            skipReason = $"root normalisation failed: {ex.Message}";
            return false;
        }
    }

    private static bool IsArchivedTenantsDirectory(string fullItemPath, string baseDir)
    {
        try
        {
            var parentDir = Path.GetDirectoryName(fullItemPath);
            if (string.IsNullOrEmpty(parentDir)) return false;
            var parentFull = Path.GetFullPath(parentDir);
            var baseFull   = Path.GetFullPath(baseDir);
            if (!string.Equals(parentFull, baseFull, System.StringComparison.OrdinalIgnoreCase))
                return false;
            var name = Path.GetFileName(fullItemPath);
            return name.StartsWith(ArchivedTenantsPrefix, System.StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsBrokenLegacyDbFile(string fullItemPath, string baseDir)
    {
        try
        {
            var parentDir = Path.GetDirectoryName(fullItemPath);
            if (string.IsNullOrEmpty(parentDir)) return false;
            var parentFull = Path.GetFullPath(parentDir);
            var baseFull   = Path.GetFullPath(baseDir);
            if (!string.Equals(parentFull, baseFull, System.StringComparison.OrdinalIgnoreCase))
                return false;
            var name = Path.GetFileName(fullItemPath);
            return name.StartsWith(BrokenLegacyPrefix, System.StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

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

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)            return $"{bytes} B";
        if (bytes < 1024L * 1024)    return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    // ── Audit log writer ─────────────────────────────────────────────────────

    private static void WriteAuditLogIfRequested(
        GuardedRetentionCleanupExecutionOptions options,
        GuardedRetentionCleanupExecutionResult result)
    {
        if (!options.WriteAuditLog) return;

        try
        {
            var logDir = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "PosSystem", "logs", CleanupLogSubdir);
            Directory.CreateDirectory(logDir);

            var stamp = System.DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
            var path  = Path.Combine(logDir, $"retention-cleanup-{stamp}-{result.Outcome}.json");

            // Strict allow-list. ExternalBackupNote replaced by a boolean.
            // Raw ConfirmationPhrase is never serialised.
            var safeOptions = new
            {
                options.Force,
                ConfirmationPhraseProvided        = !string.IsNullOrEmpty(options.ConfirmationPhrase),
                ConfirmationPhraseAccepted        = result.ConfirmationPhraseAccepted,
                options.ExternalBackupAcknowledged,
                ExternalBackupNoteProvided        = !string.IsNullOrEmpty(options.ExternalBackupNote),
                options.WriteAuditLog,
                options.ReviewedInventoryExportPath,
                options.DeleteDiagnosticsLogs,
                options.DeletePreflightLogs,
                options.DeleteMigrationLogs,
                options.DeleteRollbackLogs,
                options.DeleteOldBackups,
                options.DeleteArchivedTenantDirectories,
                options.DeleteBrokenLegacyDbFiles,
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
            .Replace(PhraseCleanup,         "<redacted-confirmation-phrase>")
            .Replace(PhraseRollbackWrapper, "<redacted-confirmation-phrase>")
            .Replace(PhraseRollbackInner,   "<redacted-confirmation-phrase>")
            .Replace(PhraseRollbackLegacy,  "<redacted-confirmation-phrase>")
            .Replace(PhraseMigration,       "<redacted-confirmation-phrase>")
            .Replace(PhraseRuntime,         "<redacted-confirmation-phrase>");
    }
}
