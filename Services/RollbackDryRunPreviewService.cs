namespace PosSystem.Services;

// ── Report DTO (Phase 10.10B) ────────────────────────────────────────────────

// Operator-facing rollback dry-run preview report. All fields describe what
// the real rollback WOULD do — no fields trigger mutation. WouldWriteFiles is
// always false in this phase: the wrapper service exposes preview only.
public sealed class RollbackDryRunPreviewReport
{
    public System.DateTime CheckedAtUtc          { get; init; } = System.DateTime.UtcNow;
    public string Outcome                        { get; init; } = "";
    public bool   IsAvailable                    { get; init; }
    public bool   WouldWriteFiles                { get; init; }

    public string? ReadinessStatus               { get; init; }
    public string? FailureReason                 { get; init; }

    public bool   RuntimeTenantDbEnabled         { get; init; }
    public bool   IsProviderTenantScoped         { get; init; }

    public string? LegacyDbPath                  { get; init; }
    public bool   LegacyDbExists                 { get; init; }
    public bool   LegacyDbReadable               { get; init; }

    public string? TenantsDirectoryPath          { get; init; }
    public string? PlannedTenantsArchivePath     { get; init; }

    public bool   WouldDisableRuntimeFlag        { get; init; }
    public bool   WouldArchiveTenantsDirectory   { get; init; }
    public bool   WouldRestoreLegacyFromBackup   { get; init; }

    public bool   SideEffectCheckPassed          { get; init; }
    public int    SideEffectDifferenceCount      { get; init; }
    public System.Collections.Generic.List<string> SideEffectDifferences
        { get; init; } = new();

    public System.Collections.Generic.List<string> PlannedSteps
        { get; init; } = new();
    public System.Collections.Generic.List<string> Warnings
        { get; init; } = new();
    public System.Collections.Generic.List<string> Errors
        { get; init; } = new();
}

// ── Service ──────────────────────────────────────────────────────────────────

// Single-purpose wrapper around TenantDbRollbackExecutor that exposes ONLY a
// safe rollback dry-run preview. Options are hardcoded by CreateSafeDryRunOptions()
// — every caller path goes through it; there is no overload or property that
// lets the consumer flip DryRunOnly off, set Force, or supply the confirmation
// phrase. The ViewModel consumes this wrapper instead of holding a reference
// to TenantDbRollbackExecutor itself.
//
// What this service NEVER does:
//   • Execute a real rollback (no DryRunOnly=false path exists here).
//   • Apply Force=true to the executor.
//   • Supply the rollback confirmation phrase
//     (TenantDbRollbackExecutor.RequiredConfirmationPhrase). The phrase is
//     not stored, not displayed, not exported.
//   • Disable the tenant_db_runtime_enabled flag (hardcoded options say it
//     WOULD, but the executor is in dry-run, so it doesn't actually).
//   • Rename / archive the tenants\ directory.
//   • Restore or copy a legacy backup.
//   • Switch the path provider.
//   • Write a rollback audit log (WriteAuditLog=false).
//
// Side-effect guard: before/after snapshots of the same set of files watched
// by MigrationDryRunPreviewService. Differences are surfaced — never auto-
// reverted.
public sealed class RollbackDryRunPreviewService
{
    private readonly TenantDbRollbackExecutor          _executor;
    private readonly TenantDbRollbackReadinessChecker  _checker;
    private readonly Data.ILocalDatabasePathProvider   _paths;

    public RollbackDryRunPreviewService(
        TenantDbRollbackExecutor executor,
        TenantDbRollbackReadinessChecker checker,
        Data.ILocalDatabasePathProvider paths)
    {
        _executor = executor;
        _checker  = checker;
        _paths    = paths;
    }

    public async System.Threading.Tasks.Task<RollbackDryRunPreviewReport> PreviewAsync(
        System.Threading.CancellationToken ct = default)
    {
        // 1. Pre-snapshot.
        DryRunSideEffectSnapshot? before = null;
        string? snapshotError = null;
        try
        {
            before = CaptureSnapshot();
        }
        catch (System.Exception ex)
        {
            snapshotError = $"pre-snapshot failed: {ex.Message}";
        }

        try
        {
            // 2. Read readiness for the status field (read-only).
            TenantDbRollbackReadinessReport? readiness = null;
            try
            {
                readiness = _checker.Check();
            }
            catch
            {
                readiness = null;
            }

            // 3. Run the executor in dry-run with hardcoded safe options.
            var result = await _executor.ExecuteAsync(CreateSafeDryRunOptions(), ct);

            // 4. Planned steps come from the executor (it already labels them
            //    with [DRY-RUN] prefixes on the dry-run path; readiness steps
            //    on the NoOp / Rejected paths).
            var planned = new System.Collections.Generic.List<string>(result.Steps);

            var warnings = new System.Collections.Generic.List<string>();
            var errors   = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(result.FailureReason))
                errors.Add(result.FailureReason!);

            // 5. Post-snapshot + diff.
            var (sideEffectPassed, diffs) = TryComputeSideEffectDifferences(before, snapshotError);
            if (!sideEffectPassed)
                warnings.Add("Rollback dry-run produced unexpected filesystem/settings side effects.");

            // 6. Synthesize a forward-looking archive path purely for display.
            //    The real executor stamps the path at real-rollback time, so
            //    use a placeholder timestamp marker rather than fake fixed UTC.
            string? plannedArchive = null;
            if (result.TenantsDirectoryArchived
                || (readiness?.TenantsDirectoryExists ?? false))
            {
                plannedArchive = $"{result.TenantsDirectory}.before-rollback-<utc-at-execution>";
            }

            // Translate hardcoded options + observed state into "would do" booleans.
            // These mirror the executor's actual decision logic without flipping
            // DryRunOnly off.
            var wouldDisableFlag    = result.RuntimeFlagWasEnabled; // DisableRuntimeFlag=true && runtime flag was on
            var wouldArchiveTenants = readiness?.TenantsDirectoryExists ?? false;
            // RestoreLegacyFromBackupIfMissing is hardcoded false in this phase.
            const bool wouldRestoreLegacy = false;

            var outcomeText = result.Outcome.ToString();
            if (result.DryRun && result.Outcome != TenantDbRollbackOutcome.DryRun)
                outcomeText += " (dry-run)";

            return new RollbackDryRunPreviewReport
            {
                CheckedAtUtc                 = System.DateTime.UtcNow,
                Outcome                      = outcomeText,
                IsAvailable                  = true,
                WouldWriteFiles              = false,
                ReadinessStatus              = readiness?.Status.ToString(),
                FailureReason                = result.FailureReason,
                RuntimeTenantDbEnabled       = result.RuntimeFlagWasEnabled,
                IsProviderTenantScoped       = _paths.IsTenantScoped,
                LegacyDbPath                 = result.LegacyDbPath,
                LegacyDbExists               = readiness?.LegacyDbExists ?? false,
                LegacyDbReadable             = readiness?.LegacyDbReadable ?? false,
                TenantsDirectoryPath         = result.TenantsDirectory,
                PlannedTenantsArchivePath    = plannedArchive,
                WouldDisableRuntimeFlag      = wouldDisableFlag,
                WouldArchiveTenantsDirectory = wouldArchiveTenants,
                WouldRestoreLegacyFromBackup = wouldRestoreLegacy,
                SideEffectCheckPassed        = sideEffectPassed,
                SideEffectDifferenceCount    = diffs.Count,
                SideEffectDifferences        = diffs,
                PlannedSteps                 = planned,
                Warnings                     = warnings,
                Errors                       = errors,
            };
        }
        catch (System.Exception ex)
        {
            // Defensive: even on executor/checker exception, run the side-effect
            // diff best-effort and never crash the UI.
            var (sideEffectPassed, diffs) = TryComputeSideEffectDifferences(before, snapshotError);
            return new RollbackDryRunPreviewReport
            {
                CheckedAtUtc              = System.DateTime.UtcNow,
                Outcome                   = "Failed",
                IsAvailable               = false,
                WouldWriteFiles           = false,
                Errors                    = new System.Collections.Generic.List<string> { ex.Message },
                SideEffectCheckPassed     = sideEffectPassed,
                SideEffectDifferenceCount = diffs.Count,
                SideEffectDifferences     = diffs,
            };
        }
    }

    // Hardcoded safe dry-run options. The wrapper has no path that overrides
    // any of these. The confirmation phrase is null — the raw phrase is never
    // stored or routed through this service.
    private static TenantDbRollbackOptions CreateSafeDryRunOptions() =>
        new()
        {
            DryRunOnly                       = true,
            Force                            = false,
            ConfirmationPhrase               = null,
            ArchiveTenantsDirectory          = true,
            DisableRuntimeFlag               = true,
            RestoreLegacyFromBackupIfMissing = false,
            WriteAuditLog                    = false,
        };

    // ── Side-effect snapshot/diff helpers ───────────────────────────────────
    //
    // Mirrors MigrationDryRunPreviewService so the behavior is uniform across
    // both preview surfaces. logs/diagnostics/ is excluded for the same
    // reason — the dashboard's Export Diagnostics JSON action writes there
    // legitimately as a separate operator-initiated action.

    private (bool passed, System.Collections.Generic.List<string> diffs)
        TryComputeSideEffectDifferences(
            DryRunSideEffectSnapshot? before,
            string? preSnapshotError)
    {
        var diffs = new System.Collections.Generic.List<string>();

        if (before is null)
        {
            diffs.Add(preSnapshotError ?? "pre-snapshot unavailable");
            return (false, diffs);
        }

        DryRunSideEffectSnapshot after;
        try
        {
            after = CaptureSnapshot();
        }
        catch (System.Exception ex)
        {
            diffs.Add($"post-snapshot failed: {ex.Message}");
            return (false, diffs);
        }

        var beforeMap = new System.Collections.Generic.Dictionary<string, FileSnapshotEntry>(
            System.StringComparer.OrdinalIgnoreCase);
        foreach (var e in before.Files) beforeMap[e.Path] = e;

        var afterMap = new System.Collections.Generic.Dictionary<string, FileSnapshotEntry>(
            System.StringComparer.OrdinalIgnoreCase);
        foreach (var e in after.Files) afterMap[e.Path] = e;

        var keys = new System.Collections.Generic.HashSet<string>(
            System.StringComparer.OrdinalIgnoreCase);
        foreach (var k in beforeMap.Keys) keys.Add(k);
        foreach (var k in afterMap.Keys)  keys.Add(k);

        foreach (var key in keys)
        {
            beforeMap.TryGetValue(key, out var b);
            afterMap.TryGetValue(key,  out var a);

            var bExists = b is { Exists: true };
            var aExists = a is { Exists: true };

            if (!bExists && aExists)
            {
                diffs.Add($"added: {key} (size={a!.Length}, mtime={Format(a.LastWriteTimeUtc)})");
                continue;
            }
            if (bExists && !aExists)
            {
                diffs.Add($"removed: {key}");
                continue;
            }
            if (bExists && aExists)
            {
                if (b!.Length != a!.Length || b.LastWriteTimeUtc != a.LastWriteTimeUtc)
                {
                    diffs.Add(
                        $"changed: {key} " +
                        $"(size {b.Length}→{a.Length}, " +
                        $"mtime {Format(b.LastWriteTimeUtc)}→{Format(a.LastWriteTimeUtc)})");
                }
            }
        }

        return (diffs.Count == 0, diffs);
    }

    private static string Format(System.DateTime? t) =>
        t is null ? "n/a" : t.Value.ToString("o");

    private DryRunSideEffectSnapshot CaptureSnapshot()
    {
        var legacyDb   = _paths.GetLegacyDbPath();
        var baseDir    = System.IO.Path.GetDirectoryName(legacyDb)
                         ?? System.IO.Path.Combine(
                                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                                "PosSystem");

        var globalJson = System.IO.Path.Combine(baseDir, "global_settings.json");
        var tenantsDir = System.IO.Path.Combine(baseDir, "tenants");
        var backupsDir = System.IO.Path.Combine(baseDir, "backups");
        var migLogsDir = System.IO.Path.Combine(baseDir, "logs", "migrations");
        var rbLogsDir  = System.IO.Path.Combine(baseDir, "logs", "rollbacks");

        var files = new System.Collections.Generic.List<FileSnapshotEntry>();

        files.Add(CaptureFile(legacyDb));
        files.Add(CaptureFile(globalJson));
        CaptureDirectoryRecursive(tenantsDir, files);
        CaptureDirectoryRecursive(backupsDir, files);
        CaptureDirectoryRecursive(migLogsDir, files);
        CaptureDirectoryRecursive(rbLogsDir,  files);

        return new DryRunSideEffectSnapshot
        {
            CapturedAtUtc              = System.DateTime.UtcNow,
            LegacyDbPath               = legacyDb,
            GlobalSettingsPath         = globalJson,
            TenantsDirectoryPath       = tenantsDir,
            BackupsDirectoryPath       = backupsDir,
            MigrationLogsDirectoryPath = migLogsDir,
            RollbackLogsDirectoryPath  = rbLogsDir,
            Files                      = files,
        };
    }

    private static FileSnapshotEntry CaptureFile(string path)
    {
        try
        {
            var fi = new System.IO.FileInfo(path);
            if (!fi.Exists)
            {
                return new FileSnapshotEntry
                {
                    Path   = path,
                    Exists = false,
                };
            }
            return new FileSnapshotEntry
            {
                Path             = path,
                Exists           = true,
                Length           = fi.Length,
                LastWriteTimeUtc = fi.LastWriteTimeUtc,
            };
        }
        catch
        {
            return new FileSnapshotEntry
            {
                Path   = path,
                Exists = false,
            };
        }
    }

    private static void CaptureDirectoryRecursive(
        string dirPath,
        System.Collections.Generic.List<FileSnapshotEntry> sink)
    {
        try
        {
            if (!System.IO.Directory.Exists(dirPath))
                return;

            foreach (var f in System.IO.Directory.EnumerateFiles(
                         dirPath, "*", System.IO.SearchOption.AllDirectories))
            {
                sink.Add(CaptureFile(f));
            }
        }
        catch
        {
            // Best-effort: an unreadable directory is recorded as no entries.
            // The diff will then report any newly-appearing file as added,
            // which is the conservative (safer) behavior.
        }
    }
}
