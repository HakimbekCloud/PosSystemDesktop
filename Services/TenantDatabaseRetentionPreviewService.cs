using System.IO;

namespace PosSystem.Services;

// ── Options / Item / Report DTOs (Phase 10.11B) ──────────────────────────────

public sealed class TenantDatabaseRetentionPreviewOptions
{
    public int DiagnosticsLogRetentionDays          { get; init; } = 30;
    public int PreflightLogRetentionDays            { get; init; } = 30;
    public int MigrationLogRetentionDays            { get; init; } = 90;
    public int RollbackLogRetentionDays             { get; init; } = 90;
    public int BackupRetentionDays                  { get; init; } = 90;
    public int ArchivedTenantDirectoryRetentionDays { get; init; } = 90;
    public int BrokenLegacyDbRetentionDays          { get; init; } = 90;
}

public sealed class RetentionPreviewItem
{
    public string         Category                  { get; init; } = "";
    public string         Path                      { get; init; } = "";
    public string         Name                      { get; init; } = "";
    public long           SizeBytes                 { get; init; }
    public string         SizeText                  { get; init; } = "";
    public System.DateTime? LastWriteTimeUtc        { get; init; }
    public int?           AgeDays                   { get; init; }
    public string         Reason                    { get; init; } = "";
    public bool           CandidateForFutureCleanup { get; init; }
}

public sealed class TenantDatabaseRetentionPreviewReport
{
    public System.DateTime CheckedAtUtc   { get; init; } = System.DateTime.UtcNow;
    public bool           PreviewOnly     { get; init; } = true;
    public string         Summary         { get; init; } = "";

    public int            CandidateCount  { get; init; }
    public long           CandidateBytes  { get; init; }
    public string         CandidateSizeText { get; init; } = "";

    public int            ProtectedItemCount { get; init; }
    public long           ProtectedBytes     { get; init; }
    public string         ProtectedSizeText  { get; init; } = "";

    public System.Collections.Generic.List<RetentionPreviewItem> Candidates
        { get; init; } = new();
    public System.Collections.Generic.List<RetentionPreviewItem> ProtectedItems
        { get; init; } = new();

    public System.Collections.Generic.List<string> Warnings { get; init; } = new();
    public System.Collections.Generic.List<string> Errors   { get; init; } = new();
}

// ── Service ──────────────────────────────────────────────────────────────────

// Strictly read-only classification of disk artifacts into "candidate for
// future cleanup" vs "protected". Built for the Migration Operations dashboard
// so an operator can see what a hypothetical future retention/cleanup tool
// would propose to remove — without removing anything.
//
// What this service does:
//   • Calls TenantDatabaseInventoryService.GetInventoryAsync (already
//     read-only) for tenant DBs / backups / archived dirs / broken legacy.
//   • Enumerates log directories itself (the inventory service exposes only
//     summary counts/sizes per log dir, not per-file metadata).
//   • Bucketizes into Candidates / ProtectedItems with a human-readable
//     reason on every entry.
//   • Emits a "preview-only — no cleanup executor exists" warning whenever
//     candidates are found.
//
// What this service NEVER does:
//   • Delete, move, rename, or copy any file or directory.
//   • Open any SQLite DB.
//   • Modify global_settings.json or any settings row.
//   • Invoke the migrator, rollback executor, or TenantScopeService.
//   • Switch the path provider.
public sealed class TenantDatabaseRetentionPreviewService
{
    // 500 MB — trigger a warning when cumulative candidate size crosses this.
    private const long LargeCandidateThresholdBytes = 500L * 1024 * 1024;

    // Latest backup older than this raises a freshness warning. Distinct from
    // BackupRetentionDays (which classifies non-latest backups as candidates).
    private const int  StaleBackupWarningDays = 30;

    private readonly TenantDatabaseInventoryService _inventory;

    public TenantDatabaseRetentionPreviewService(TenantDatabaseInventoryService inventory)
    {
        _inventory = inventory;
    }

    public async System.Threading.Tasks.Task<TenantDatabaseRetentionPreviewReport> PreviewAsync(
        TenantDatabaseRetentionPreviewOptions? options = null,
        System.Threading.CancellationToken ct = default)
    {
        var opts           = options ?? new TenantDatabaseRetentionPreviewOptions();
        var now            = System.DateTime.UtcNow;
        var candidates     = new System.Collections.Generic.List<RetentionPreviewItem>();
        var protectedItems = new System.Collections.Generic.List<RetentionPreviewItem>();
        var warnings       = new System.Collections.Generic.List<string>();
        var errors         = new System.Collections.Generic.List<string>();

        TenantDatabaseInventoryReport inventory;
        try
        {
            inventory = await _inventory.GetInventoryAsync(ct);
        }
        catch (System.Exception ex)
        {
            errors.Add($"Inventory failed: {ex.Message}");
            return new TenantDatabaseRetentionPreviewReport
            {
                CheckedAtUtc       = System.DateTime.UtcNow,
                PreviewOnly        = true,
                Summary            = "PREVIEW ONLY — inventory unavailable.",
                CandidateSizeText  = "0 B",
                ProtectedSizeText  = "0 B",
                Warnings           = warnings,
                Errors             = errors,
            };
        }

        foreach (var e in inventory.Errors)   errors.Add($"Inventory: {e}");
        foreach (var w in inventory.Warnings) warnings.Add($"Inventory: {w}");

        // 1. Legacy DB — always protected.
        if (inventory.LegacyDb is { Exists: true })
        {
            protectedItems.Add(BuildItem(
                category: "legacy-db",
                path:     inventory.LegacyDb.Path,
                name:     inventory.LegacyDb.Name,
                size:     inventory.LegacyDb.SizeBytes,
                mtime:    inventory.LegacyDb.LastWriteTimeUtc,
                now:      now,
                reason:   "Protected: active legacy database",
                candidate: false));
        }

        // 2. Tenant DBs — always protected.
        foreach (var t in inventory.TenantDatabases)
        {
            if (!t.DbExists) continue;
            protectedItems.Add(BuildItem(
                category: "tenant-db",
                path:     t.DbPath,
                name:     $"{t.TenantSubdomain}/pos.db",
                size:     t.SizeBytes,
                mtime:    t.LastWriteTimeUtc,
                now:      now,
                reason:   "Protected: tenant database",
                candidate: false));
        }

        // 3. Backups — latest protected; older-than-retention candidates.
        if (inventory.BackupFiles.Count == 0)
        {
            warnings.Add("No backup files exist under backups\\.");
        }
        else
        {
            var sortedBackups = inventory.BackupFiles
                .OrderByDescending(b => b.LastWriteTimeUtc ?? System.DateTime.MinValue)
                .ToList();

            var latest = sortedBackups[0];
            protectedItems.Add(BuildItem(
                category: "backup",
                path:     latest.Path,
                name:     latest.Name,
                size:     latest.SizeBytes,
                mtime:    latest.LastWriteTimeUtc,
                now:      now,
                reason:   "Protected: latest backup",
                candidate: false));

            if (latest.LastWriteTimeUtc is { } latestMtime &&
                (now - latestMtime).TotalDays > StaleBackupWarningDays)
            {
                warnings.Add(
                    $"Latest backup is older than {StaleBackupWarningDays} days " +
                    $"({(int)(now - latestMtime).TotalDays}d old).");
            }

            foreach (var b in sortedBackups.Skip(1))
            {
                var ageDays = b.LastWriteTimeUtc is null
                    ? (int?)null
                    : (int)(now - b.LastWriteTimeUtc.Value).TotalDays;
                if (ageDays is { } ad && ad > opts.BackupRetentionDays)
                {
                    candidates.Add(BuildItem(
                        category: "backup",
                        path:     b.Path,
                        name:     b.Name,
                        size:     b.SizeBytes,
                        mtime:    b.LastWriteTimeUtc,
                        now:      now,
                        reason:   $"Candidate: backup older than {opts.BackupRetentionDays} days",
                        candidate: true));
                }
            }
        }

        // 4. Archived tenant directories — existence-based warning + age-based candidates.
        if (inventory.ArchivedTenantDirectories.Count > 0)
        {
            warnings.Add(
                $"{inventory.ArchivedTenantDirectories.Count} archived tenant directory(ies) exist " +
                "(retained for manual recovery after rollback).");
        }
        foreach (var a in inventory.ArchivedTenantDirectories)
        {
            var ageDays = a.LastWriteTimeUtc is null
                ? (int?)null
                : (int)(now - a.LastWriteTimeUtc.Value).TotalDays;
            if (ageDays is { } ad && ad > opts.ArchivedTenantDirectoryRetentionDays)
            {
                candidates.Add(BuildItem(
                    category: "archived-tenants-dir",
                    path:     a.Path,
                    name:     a.Name,
                    size:     a.SizeBytes,
                    mtime:    a.LastWriteTimeUtc,
                    now:      now,
                    reason:   $"Candidate: archived tenant dir older than {opts.ArchivedTenantDirectoryRetentionDays} days",
                    candidate: true));
            }
        }

        // 5. Broken legacy DB files — existence-based warning + age-based candidates.
        if (inventory.BrokenLegacyDbFiles.Count > 0)
        {
            warnings.Add(
                $"{inventory.BrokenLegacyDbFiles.Count} broken legacy DB file(s) exist " +
                "(set aside by the rollback executor's restore branch).");
        }
        foreach (var b in inventory.BrokenLegacyDbFiles)
        {
            var ageDays = b.LastWriteTimeUtc is null
                ? (int?)null
                : (int)(now - b.LastWriteTimeUtc.Value).TotalDays;
            if (ageDays is { } ad && ad > opts.BrokenLegacyDbRetentionDays)
            {
                candidates.Add(BuildItem(
                    category: "broken-legacy-db",
                    path:     b.Path,
                    name:     b.Name,
                    size:     b.SizeBytes,
                    mtime:    b.LastWriteTimeUtc,
                    now:      now,
                    reason:   $"Candidate: broken legacy DB older than {opts.BrokenLegacyDbRetentionDays} days",
                    candidate: true));
            }
        }

        // 6. Logs — newest per category protected, older-than-retention candidates.
        ClassifyLogDirectory("log-diagnostics", inventory.DiagnosticsLogs.DirectoryPath,
            opts.DiagnosticsLogRetentionDays, candidates, protectedItems, errors, now, ct);
        ClassifyLogDirectory("log-preflight",   inventory.PreflightLogs.DirectoryPath,
            opts.PreflightLogRetentionDays, candidates, protectedItems, errors, now, ct);
        ClassifyLogDirectory("log-migrations",  inventory.MigrationLogs.DirectoryPath,
            opts.MigrationLogRetentionDays, candidates, protectedItems, errors, now, ct);
        ClassifyLogDirectory("log-rollbacks",   inventory.RollbackLogs.DirectoryPath,
            opts.RollbackLogRetentionDays, candidates, protectedItems, errors, now, ct);

        // 7. Aggregate.
        long candidateBytes = 0;
        foreach (var c in candidates) candidateBytes += c.SizeBytes;
        long protectedBytes = 0;
        foreach (var p in protectedItems) protectedBytes += p.SizeBytes;

        if (candidateBytes > LargeCandidateThresholdBytes)
        {
            warnings.Add(
                $"Cleanup candidate total size is large: {FormatSize(candidateBytes)}.");
        }

        if (candidates.Count > 0)
        {
            warnings.Add(
                "Retention preview found cleanup candidates, but no cleanup executor exists. " +
                "This phase is preview-only.");
        }

        var summary =
            $"PREVIEW ONLY — candidates={candidates.Count} ({FormatSize(candidateBytes)}); " +
            $"protected={protectedItems.Count} ({FormatSize(protectedBytes)}).";

        return new TenantDatabaseRetentionPreviewReport
        {
            CheckedAtUtc       = System.DateTime.UtcNow,
            PreviewOnly        = true,
            Summary            = summary,
            CandidateCount     = candidates.Count,
            CandidateBytes     = candidateBytes,
            CandidateSizeText  = FormatSize(candidateBytes),
            ProtectedItemCount = protectedItems.Count,
            ProtectedBytes     = protectedBytes,
            ProtectedSizeText  = FormatSize(protectedBytes),
            Candidates         = candidates,
            ProtectedItems     = protectedItems,
            Warnings           = warnings,
            Errors             = errors,
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void ClassifyLogDirectory(
        string category,
        string dirPath,
        int retentionDays,
        System.Collections.Generic.List<RetentionPreviewItem> candidates,
        System.Collections.Generic.List<RetentionPreviewItem> protectedItems,
        System.Collections.Generic.List<string> errors,
        System.DateTime now,
        System.Threading.CancellationToken ct)
    {
        try
        {
            if (!Directory.Exists(dirPath)) return;

            var files = new System.Collections.Generic.List<FileInfo>();
            foreach (var f in Directory.EnumerateFiles(dirPath, "*", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var fi = new FileInfo(f);
                    if (fi.Exists) files.Add(fi);
                }
                catch (System.Exception ex)
                {
                    errors.Add($"Cannot stat {f}: {ex.Message}");
                }
            }

            if (files.Count == 0) return;

            files.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            var newest = files[0];

            protectedItems.Add(BuildItem(
                category: category,
                path:     newest.FullName,
                name:     newest.Name,
                size:     newest.Length,
                mtime:    newest.LastWriteTimeUtc,
                now:      now,
                reason:   $"Protected: newest log in {category}",
                candidate: false));

            for (int i = 1; i < files.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var f = files[i];
                var ageDays = (int)(now - f.LastWriteTimeUtc).TotalDays;
                if (ageDays > retentionDays)
                {
                    candidates.Add(BuildItem(
                        category: category,
                        path:     f.FullName,
                        name:     f.Name,
                        size:     f.Length,
                        mtime:    f.LastWriteTimeUtc,
                        now:      now,
                        reason:   $"Candidate: {category} older than {retentionDays} days",
                        candidate: true));
                }
            }
        }
        catch (System.OperationCanceledException) { throw; }
        catch (System.Exception ex)
        {
            errors.Add($"Cannot classify {dirPath}: {ex.Message}");
        }
    }

    private static RetentionPreviewItem BuildItem(
        string category,
        string path,
        string name,
        long size,
        System.DateTime? mtime,
        System.DateTime now,
        string reason,
        bool candidate)
    {
        int? age = mtime is null ? (int?)null : (int)(now - mtime.Value).TotalDays;
        return new RetentionPreviewItem
        {
            Category                  = category,
            Path                      = path,
            Name                      = name,
            SizeBytes                 = size,
            SizeText                  = FormatSize(size),
            LastWriteTimeUtc          = mtime,
            AgeDays                   = age,
            Reason                    = reason,
            CandidateForFutureCleanup = candidate,
        };
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)            return $"{bytes} B";
        if (bytes < 1024L * 1024)    return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
