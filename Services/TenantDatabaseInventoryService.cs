using System.IO;

namespace PosSystem.Services;

// ── Inventory DTOs (Phase 10.11A) ────────────────────────────────────────────

public sealed class InventoryFileItem
{
    public string         Path             { get; init; } = "";
    public string         Name             { get; init; } = "";
    public bool           Exists           { get; init; }
    public long           SizeBytes        { get; init; }
    public string         SizeText         { get; init; } = "";
    public System.DateTime? LastWriteTimeUtc { get; init; }
}

public sealed class InventoryDirectoryItem
{
    public string         Path             { get; init; } = "";
    public string         Name             { get; init; } = "";
    public bool           Exists           { get; init; }
    public long           SizeBytes        { get; init; }
    public string         SizeText         { get; init; } = "";
    public System.DateTime? LastWriteTimeUtc { get; init; }
    public int            FileCount        { get; init; }
}

public sealed class TenantDbInventoryItem
{
    public string         TenantSubdomain     { get; init; } = "";
    public string         TenantDirectoryPath { get; init; } = "";
    public string         DbPath              { get; init; } = "";
    public bool           DbExists            { get; init; }
    public long           SizeBytes           { get; init; }
    public string         SizeText            { get; init; } = "";
    public System.DateTime? LastWriteTimeUtc  { get; init; }
}

public sealed class InventoryLogSummary
{
    public string         DirectoryPath { get; init; } = "";
    public bool           Exists        { get; init; }
    public int            FileCount     { get; init; }
    public long           SizeBytes     { get; init; }
    public string         SizeText      { get; init; } = "";
    public System.DateTime? NewestFileUtc { get; init; }
}

public sealed class TenantDatabaseInventoryReport
{
    public System.DateTime CheckedAtUtc      { get; init; } = System.DateTime.UtcNow;

    public string         BaseDirectoryPath { get; init; } = "";
    public string         LegacyDbPath      { get; init; } = "";
    public InventoryFileItem? LegacyDb      { get; init; }

    public System.Collections.Generic.List<TenantDbInventoryItem> TenantDatabases
        { get; init; } = new();
    public System.Collections.Generic.List<InventoryFileItem> BackupFiles
        { get; init; } = new();
    public System.Collections.Generic.List<InventoryDirectoryItem> ArchivedTenantDirectories
        { get; init; } = new();
    public System.Collections.Generic.List<InventoryFileItem> BrokenLegacyDbFiles
        { get; init; } = new();

    public InventoryLogSummary MigrationLogs   { get; init; } = new();
    public InventoryLogSummary RollbackLogs    { get; init; } = new();
    public InventoryLogSummary DiagnosticsLogs { get; init; } = new();
    public InventoryLogSummary PreflightLogs   { get; init; } = new();

    public long           TotalKnownBytes    { get; init; }
    public string         TotalKnownSizeText { get; init; } = "";

    public System.Collections.Generic.List<string> Warnings { get; init; } = new();
    public System.Collections.Generic.List<string> Errors   { get; init; } = new();
}

// ── Service ──────────────────────────────────────────────────────────────────

// Strictly read-only filesystem inventory of every storage location PosSystem
// uses under %LocalAppData%\PosSystem\. Built for the Migration Operations
// dashboard so an operator can see what's on disk before any future cleanup
// or retention tool is introduced.
//
// What this service does:
//   • Calls FileInfo / DirectoryInfo / Directory.EnumerateFiles /
//     Directory.EnumerateDirectories — metadata only.
//   • Aggregates sizes, file counts, newest-modified timestamps.
//   • Returns one cohesive report, never throws to callers — per-path
//     failures land in Errors and the rest of the inventory still completes.
//
// What this service NEVER does:
//   • Open any SQLite DB for read or write.
//   • Delete, move, rename, or copy any file or directory.
//   • Modify global_settings.json or any settings row.
//   • Invoke the migrator, rollback executor, or TenantScopeService.
//   • Switch the path provider.
public sealed class TenantDatabaseInventoryService
{
    private const string ArchivedTenantsPrefix = "tenants.before-rollback-";
    private const string BrokenLegacyPrefix    = "pos.db.broken-";

    private readonly Data.ILocalDatabasePathProvider _paths;

    public TenantDatabaseInventoryService(Data.ILocalDatabasePathProvider paths)
    {
        _paths = paths;
    }

    public System.Threading.Tasks.Task<TenantDatabaseInventoryReport> GetInventoryAsync(
        System.Threading.CancellationToken ct = default)
        => System.Threading.Tasks.Task.FromResult(Build(ct));

    private TenantDatabaseInventoryReport Build(System.Threading.CancellationToken ct)
    {
        var warnings = new System.Collections.Generic.List<string>();
        var errors   = new System.Collections.Generic.List<string>();

        var legacyPath = _paths.GetLegacyDbPath();
        var baseDir    = Path.GetDirectoryName(legacyPath)
                         ?? Path.Combine(
                                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                                "PosSystem");

        // 1. Legacy DB.
        var legacy = CaptureFile(legacyPath, errors);

        // 2. Tenant DBs under tenants\<sub>\pos.db.
        var tenantDbs = new System.Collections.Generic.List<TenantDbInventoryItem>();
        var tenantsDir = Path.Combine(baseDir, "tenants");
        try
        {
            if (Directory.Exists(tenantsDir))
            {
                foreach (var sub in Directory.EnumerateDirectories(tenantsDir).OrderBy(s => s))
                {
                    ct.ThrowIfCancellationRequested();
                    var dbPath = Path.Combine(sub, "pos.db");
                    var fi = TryFileInfo(dbPath, errors);
                    tenantDbs.Add(new TenantDbInventoryItem
                    {
                        TenantSubdomain     = Path.GetFileName(sub) ?? "",
                        TenantDirectoryPath = sub,
                        DbPath              = dbPath,
                        DbExists            = fi?.Exists ?? false,
                        SizeBytes           = fi is { Exists: true } ? fi.Length : 0,
                        SizeText            = FormatSize(fi is { Exists: true } ? fi.Length : 0),
                        LastWriteTimeUtc    = fi is { Exists: true } ? fi.LastWriteTimeUtc : null,
                    });
                }
            }
        }
        catch (System.OperationCanceledException) { throw; }
        catch (System.Exception ex)
        {
            errors.Add($"Failed to enumerate {tenantsDir}: {ex.Message}");
        }

        // 3. Backups under backups\.
        var backupsDir = Path.Combine(baseDir, "backups");
        var backups = EnumerateFilesAsItems(backupsDir, "*", errors, ct);

        // 4. Archived tenant directories — siblings of tenants\ named tenants.before-rollback-*.
        var archives = new System.Collections.Generic.List<InventoryDirectoryItem>();
        try
        {
            if (Directory.Exists(baseDir))
            {
                foreach (var dir in Directory.EnumerateDirectories(baseDir, ArchivedTenantsPrefix + "*")
                                             .OrderBy(d => d))
                {
                    ct.ThrowIfCancellationRequested();
                    archives.Add(CaptureDirectoryRecursive(dir, errors, ct));
                }
            }
        }
        catch (System.OperationCanceledException) { throw; }
        catch (System.Exception ex)
        {
            errors.Add($"Failed to enumerate archived tenant dirs under {baseDir}: {ex.Message}");
        }

        // 5. Broken legacy DB files — siblings of pos.db named pos.db.broken-*.
        var broken = new System.Collections.Generic.List<InventoryFileItem>();
        try
        {
            if (Directory.Exists(baseDir))
            {
                foreach (var f in Directory.EnumerateFiles(baseDir, BrokenLegacyPrefix + "*")
                                           .OrderBy(f => f))
                {
                    ct.ThrowIfCancellationRequested();
                    var item = CaptureFile(f, errors);
                    if (item is not null) broken.Add(item);
                }
            }
        }
        catch (System.OperationCanceledException) { throw; }
        catch (System.Exception ex)
        {
            errors.Add($"Failed to enumerate broken legacy DB files under {baseDir}: {ex.Message}");
        }

        // 6. Log summaries.
        var migLogs   = SummarizeLogDirectory(Path.Combine(baseDir, "logs", "migrations"),  errors, ct);
        var rbLogs    = SummarizeLogDirectory(Path.Combine(baseDir, "logs", "rollbacks"),   errors, ct);
        var diagLogs  = SummarizeLogDirectory(Path.Combine(baseDir, "logs", "diagnostics"), errors, ct);
        var pfLogs    = SummarizeLogDirectory(Path.Combine(baseDir, "logs", "preflight"),   errors, ct);

        // 7. Aggregate known size.
        long total = 0;
        if (legacy?.Exists == true) total += legacy.SizeBytes;
        foreach (var t in tenantDbs) total += t.SizeBytes;
        foreach (var b in backups)   total += b.SizeBytes;
        foreach (var a in archives)  total += a.SizeBytes;
        foreach (var b in broken)    total += b.SizeBytes;
        total += migLogs.SizeBytes + rbLogs.SizeBytes + diagLogs.SizeBytes + pfLogs.SizeBytes;

        return new TenantDatabaseInventoryReport
        {
            CheckedAtUtc              = System.DateTime.UtcNow,
            BaseDirectoryPath         = baseDir,
            LegacyDbPath              = legacyPath,
            LegacyDb                  = legacy,
            TenantDatabases           = tenantDbs,
            BackupFiles               = backups,
            ArchivedTenantDirectories = archives,
            BrokenLegacyDbFiles       = broken,
            MigrationLogs             = migLogs,
            RollbackLogs              = rbLogs,
            DiagnosticsLogs           = diagLogs,
            PreflightLogs             = pfLogs,
            TotalKnownBytes           = total,
            TotalKnownSizeText        = FormatSize(total),
            Warnings                  = warnings,
            Errors                    = errors,
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static FileInfo? TryFileInfo(string path, System.Collections.Generic.List<string> errors)
    {
        try { return new FileInfo(path); }
        catch (System.Exception ex)
        {
            errors.Add($"Cannot stat {path}: {ex.Message}");
            return null;
        }
    }

    private static InventoryFileItem? CaptureFile(
        string path,
        System.Collections.Generic.List<string> errors)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists)
            {
                return new InventoryFileItem
                {
                    Path     = path,
                    Name     = Path.GetFileName(path) ?? "",
                    Exists   = false,
                    SizeBytes = 0,
                    SizeText = FormatSize(0),
                };
            }
            return new InventoryFileItem
            {
                Path             = path,
                Name             = fi.Name,
                Exists           = true,
                SizeBytes        = fi.Length,
                SizeText         = FormatSize(fi.Length),
                LastWriteTimeUtc = fi.LastWriteTimeUtc,
            };
        }
        catch (System.Exception ex)
        {
            errors.Add($"Cannot stat {path}: {ex.Message}");
            return null;
        }
    }

    private static System.Collections.Generic.List<InventoryFileItem> EnumerateFilesAsItems(
        string dirPath,
        string searchPattern,
        System.Collections.Generic.List<string> errors,
        System.Threading.CancellationToken ct)
    {
        var list = new System.Collections.Generic.List<InventoryFileItem>();
        try
        {
            if (!Directory.Exists(dirPath)) return list;
            foreach (var f in Directory.EnumerateFiles(dirPath, searchPattern, SearchOption.TopDirectoryOnly)
                                       .OrderBy(f => f))
            {
                ct.ThrowIfCancellationRequested();
                var item = CaptureFile(f, errors);
                if (item is not null) list.Add(item);
            }
        }
        catch (System.OperationCanceledException) { throw; }
        catch (System.Exception ex)
        {
            errors.Add($"Failed to enumerate {dirPath}: {ex.Message}");
        }
        return list;
    }

    private static InventoryDirectoryItem CaptureDirectoryRecursive(
        string dirPath,
        System.Collections.Generic.List<string> errors,
        System.Threading.CancellationToken ct)
    {
        try
        {
            var di = new DirectoryInfo(dirPath);
            if (!di.Exists)
            {
                return new InventoryDirectoryItem
                {
                    Path      = dirPath,
                    Name      = Path.GetFileName(dirPath) ?? "",
                    Exists    = false,
                    SizeBytes = 0,
                    SizeText  = FormatSize(0),
                    FileCount = 0,
                };
            }

            long size = 0;
            int  count = 0;
            System.DateTime newest = di.LastWriteTimeUtc;
            try
            {
                foreach (var f in Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var fi = new FileInfo(f);
                        if (fi.Exists)
                        {
                            size += fi.Length;
                            count++;
                            if (fi.LastWriteTimeUtc > newest) newest = fi.LastWriteTimeUtc;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        errors.Add($"Cannot stat {f}: {ex.Message}");
                    }
                }
            }
            catch (System.OperationCanceledException) { throw; }
            catch (System.Exception ex)
            {
                errors.Add($"Cannot enumerate {dirPath}: {ex.Message}");
            }

            return new InventoryDirectoryItem
            {
                Path             = dirPath,
                Name             = di.Name,
                Exists           = true,
                SizeBytes        = size,
                SizeText         = FormatSize(size),
                LastWriteTimeUtc = newest,
                FileCount        = count,
            };
        }
        catch (System.OperationCanceledException) { throw; }
        catch (System.Exception ex)
        {
            errors.Add($"Cannot inventory directory {dirPath}: {ex.Message}");
            return new InventoryDirectoryItem
            {
                Path      = dirPath,
                Name      = Path.GetFileName(dirPath) ?? "",
                Exists    = false,
                SizeBytes = 0,
                SizeText  = FormatSize(0),
                FileCount = 0,
            };
        }
    }

    private static InventoryLogSummary SummarizeLogDirectory(
        string dirPath,
        System.Collections.Generic.List<string> errors,
        System.Threading.CancellationToken ct)
    {
        if (!Directory.Exists(dirPath))
        {
            return new InventoryLogSummary
            {
                DirectoryPath = dirPath,
                Exists        = false,
                FileCount     = 0,
                SizeBytes     = 0,
                SizeText      = FormatSize(0),
            };
        }

        long size = 0;
        int  count = 0;
        System.DateTime? newest = null;

        try
        {
            foreach (var f in Directory.EnumerateFiles(dirPath, "*", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var fi = new FileInfo(f);
                    if (fi.Exists)
                    {
                        size += fi.Length;
                        count++;
                        if (newest is null || fi.LastWriteTimeUtc > newest) newest = fi.LastWriteTimeUtc;
                    }
                }
                catch (System.Exception ex)
                {
                    errors.Add($"Cannot stat {f}: {ex.Message}");
                }
            }
        }
        catch (System.OperationCanceledException) { throw; }
        catch (System.Exception ex)
        {
            errors.Add($"Cannot enumerate {dirPath}: {ex.Message}");
        }

        return new InventoryLogSummary
        {
            DirectoryPath = dirPath,
            Exists        = true,
            FileCount     = count,
            SizeBytes     = size,
            SizeText      = FormatSize(size),
            NewestFileUtc = newest,
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
