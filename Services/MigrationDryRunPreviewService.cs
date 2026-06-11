namespace PosSystem.Services;

// ── Side-effect snapshot DTOs (Phase 10.10A.1) ──────────────────────────────

// One filesystem entry captured for the side-effect guard. Path is stored as
// the absolute path so comparisons across before/after snapshots are
// unambiguous. LastWriteTimeUtc and Length are nullable so a "missing"
// entry can still be represented with Exists=false.
public sealed class FileSnapshotEntry
{
    public string          Path             { get; init; } = "";
    public bool            Exists           { get; init; }
    public long?           Length           { get; init; }
    public System.DateTime? LastWriteTimeUtc { get; init; }
}

// Captures the state of the small set of files/dirs the migrator could touch
// during a real run. The same paths are captured before and after the dry-run
// preview; any difference is treated as an unexpected side effect.
//
// IMPORTANT: SQLite DB files are recorded by size + mtime only — they are
// never opened for write. Diagnostics export directory is deliberately
// excluded because the dashboard's Export Diagnostics JSON action writes
// there legitimately and may run independently of the dry-run preview.
public sealed class DryRunSideEffectSnapshot
{
    public System.DateTime CapturedAtUtc            { get; init; } = System.DateTime.UtcNow;
    public string          LegacyDbPath             { get; init; } = "";
    public string          GlobalSettingsPath       { get; init; } = "";
    public string          TenantsDirectoryPath     { get; init; } = "";
    public string          BackupsDirectoryPath     { get; init; } = "";
    public string          MigrationLogsDirectoryPath { get; init; } = "";
    public string          RollbackLogsDirectoryPath { get; init; } = "";

    public System.Collections.Generic.IReadOnlyList<FileSnapshotEntry> Files
    { get; init; } = new System.Collections.Generic.List<FileSnapshotEntry>();
}

// ── Report DTO ───────────────────────────────────────────────────────────────

public sealed class MigrationDryRunPreviewReport
{
    public System.DateTime CheckedAtUtc { get; init; } = System.DateTime.UtcNow;
    public string Outcome             { get; init; } = "";
    public bool   IsAvailable         { get; init; }

    // Always false for Phase 10.10A. Reserved for future surfaces that might
    // legitimately need to advertise that a planned execution would write
    // files — the preview is never one of them.
    public bool   WouldWriteFiles     { get; init; }

    public int    TenantCount         { get; init; }
    public int    WarningCount        { get; init; }
    public int    ErrorCount          { get; init; }

    public System.Collections.Generic.List<string> Details
        { get; init; } = new();
    public System.Collections.Generic.List<string> Warnings
        { get; init; } = new();
    public System.Collections.Generic.List<string> Errors
        { get; init; } = new();

    // ── Side-effect guard (Phase 10.10A.1) ──────────────────────────────────

    // True when before/after snapshots are identical for every watched file.
    // False if any difference was detected, or if the snapshot itself failed
    // (in which case the failure is recorded in SideEffectDifferences too).
    public bool   SideEffectCheckPassed     { get; init; }
    public int    SideEffectDifferenceCount { get; init; }
    public System.Collections.Generic.List<string> SideEffectDifferences
        { get; init; } = new();
}

// ── Service ──────────────────────────────────────────────────────────────────

// Single-purpose wrapper around SharedToTenantDatabaseMigrator that exposes
// ONLY a safe dry-run preview. The real migrator's Force / AllowWhenFeatureDisabled /
// WriteAuditLog knobs are hardcoded to safe values; DryRunOnly is hardcoded
// to true. ViewModels that need preview information consume this wrapper
// instead of holding a reference to the migrator itself.
//
// The wrapper has no method that runs a real migration. Operators wanting
// real migration must resolve SharedToTenantDatabaseMigrator directly from
// DI (debugger / future operator CLI) and provide all four real-run guards
// (DryRunOnly=false + Force=true + feature flag OR override) themselves.
//
// Phase 10.10A.1 adds a side-effect guard: before and after the dry-run, a
// snapshot of the small set of files the migrator could touch is captured.
// Any difference is surfaced in the report and added to the dashboard's
// warnings/errors so an operator can spot accidental mutation.
public sealed class MigrationDryRunPreviewService
{
    private readonly SharedToTenantDatabaseMigrator   _migrator;
    private readonly Data.ILocalDatabasePathProvider  _paths;

    public MigrationDryRunPreviewService(
        SharedToTenantDatabaseMigrator migrator,
        Data.ILocalDatabasePathProvider paths)
    {
        _migrator = migrator;
        _paths    = paths;
    }

    public async System.Threading.Tasks.Task<MigrationDryRunPreviewReport> PreviewAsync(
        System.Threading.CancellationToken ct = default)
    {
        // 1. Capture pre-dry-run snapshot.
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
            // 2. Run the dry-run preview with hardcoded safe options.
            var result = await _migrator.MigrateAsync(CreateSafeDryRunOptions(), ct);

            var details = new System.Collections.Generic.List<string>();
            foreach (var t in result.Tenants)
            {
                details.Add(
                    $"{t.Subdomain} → {t.TargetDbPath} :: {t.Outcome}" +
                    (t.SalesCopied  > 0 ? $", sales={t.SalesCopied}"     : "") +
                    (t.SaleItemsCopied > 0 ? $", items={t.SaleItemsCopied}" : "") +
                    (t.SettingsCopied > 0 ? $", settings={t.SettingsCopied}" : "") +
                    (t.CatalogCopied      ? ", catalog=copied"           : ""));
            }
            if (result.OrphanSalesQuarantined > 0)
                details.Add($"_orphan → would quarantine {result.OrphanSalesQuarantined} untagged sale(s)");

            var warnings = new System.Collections.Generic.List<string>();
            var errors   = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(result.FailureReason))
                errors.Add(result.FailureReason!);

            // 3. Capture post-dry-run snapshot and diff.
            var (sideEffectPassed, diffs) = TryComputeSideEffectDifferences(before, snapshotError);
            if (!sideEffectPassed)
            {
                warnings.Add("Migration dry-run produced unexpected filesystem/settings side effects.");
            }

            return new MigrationDryRunPreviewReport
            {
                CheckedAtUtc              = System.DateTime.UtcNow,
                Outcome                   = result.Outcome.ToString() + (result.DryRun ? " (dry-run)" : ""),
                IsAvailable               = true,
                WouldWriteFiles           = false,
                TenantCount               = result.Tenants.Count,
                Details                   = details,
                Warnings                  = warnings,
                Errors                    = errors,
                WarningCount              = warnings.Count,
                ErrorCount                = errors.Count,
                SideEffectCheckPassed     = sideEffectPassed,
                SideEffectDifferenceCount = diffs.Count,
                SideEffectDifferences     = diffs,
            };
        }
        catch (System.Exception ex)
        {
            // Even on dry-run failure we still try to record what we know
            // about the side-effect state. Best-effort — don't crash the UI.
            var (sideEffectPassed, diffs) = TryComputeSideEffectDifferences(before, snapshotError);

            var errors = new System.Collections.Generic.List<string> { ex.Message };
            return new MigrationDryRunPreviewReport
            {
                CheckedAtUtc              = System.DateTime.UtcNow,
                Outcome                   = "Failed",
                IsAvailable               = false,
                WouldWriteFiles           = false,
                Errors                    = errors,
                ErrorCount                = errors.Count,
                SideEffectCheckPassed     = sideEffectPassed,
                SideEffectDifferenceCount = diffs.Count,
                SideEffectDifferences     = diffs,
            };
        }
    }

    // Hardcoded safe dry-run options. No code path in this service overrides
    // these — a caller that wants a real migration must bypass this wrapper
    // entirely and provide all four guards directly to MigrateAsync.
    private static SharedToTenantMigrationOptions CreateSafeDryRunOptions() =>
        new()
        {
            DryRunOnly               = true,
            Force                    = false,
            AllowWhenFeatureDisabled = false,
            WriteAuditLog            = false,
        };

    // ── Side-effect snapshot/diff helpers ───────────────────────────────────

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

        // Index both snapshots by absolute path for diffing.
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

    // Captures the small set of files/dirs the migrator could touch in a real
    // run. Read-only: never opens, modifies, or deletes anything. Diagnostics
    // export directory is excluded by design.
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
