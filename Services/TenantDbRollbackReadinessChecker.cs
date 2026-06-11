using System.IO;
using Microsoft.Data.Sqlite;
using PosSystem.Data;
using PosSystem.Data.Repositories;

namespace PosSystem.Services;

// ── Status enum ──────────────────────────────────────────────────────────────

public enum TenantDbRollbackReadinessStatus
{
    NotInTenantRuntimeMode,   // tenant_db_runtime_enabled != "1" — rollback not needed
    Ready,                    // runtime mode on, legacy DB readable, no warnings
    ReadyWithWarnings,        // runtime mode on, legacy DB readable, but warnings present
    Blocked,                  // legacy DB missing/unreadable AND no backup available
}

// ── Report DTO ───────────────────────────────────────────────────────────────

public sealed class TenantDbRollbackReadinessReport
{
    public TenantDbRollbackReadinessStatus Status { get; init; }
    public bool   CanRollback                     { get; init; }

    public string LegacyDbPath              { get; init; } = "";
    public bool   LegacyDbExists            { get; init; }
    public bool   LegacyDbReadable          { get; init; }
    public long   LegacyDbSizeBytes         { get; init; }

    public string TenantsDirectory          { get; init; } = "";
    public bool   TenantsDirectoryExists    { get; init; }
    public int    TenantDbCount             { get; init; }
    public System.Collections.Generic.IReadOnlyList<string> TenantDbs
        { get; init; } = System.Array.Empty<string>();

    public string BackupsDirectory          { get; init; } = "";
    public int    LegacyBackupCount         { get; init; }
    public string? MostRecentBackupPath     { get; init; }

    public string GlobalSettingsPath        { get; init; } = "";
    public bool   RuntimeFlagEnabled        { get; init; }
    public bool   GlobalMigrationMarkerPresent { get; init; }
    public bool   ProviderInTenantMode      { get; init; }
    public string? LastTenantSubdomain      { get; init; }

    public System.Collections.Generic.IReadOnlyList<string> Warnings
        { get; init; } = System.Array.Empty<string>();
    public System.Collections.Generic.IReadOnlyList<string> RecommendedSteps
        { get; init; } = System.Array.Empty<string>();

    public System.DateTime GeneratedAtUtc   { get; init; } = System.DateTime.UtcNow;
}

// ── Checker ──────────────────────────────────────────────────────────────────

public sealed class TenantDbRollbackReadinessChecker
{
    private readonly ILocalDatabasePathProvider _pathProvider;
    private readonly GlobalSettingsRepository   _global;

    public TenantDbRollbackReadinessChecker(
        ILocalDatabasePathProvider pathProvider,
        GlobalSettingsRepository global)
    {
        _pathProvider = pathProvider;
        _global       = global;
    }

    public TenantDbRollbackReadinessReport Check()
    {
        var legacyPath = _pathProvider.GetLegacyDbPath();
        var baseDir    = Path.GetDirectoryName(legacyPath) ?? "";
        var tenantsDir = Path.Combine(baseDir, "tenants");
        var backupsDir = Path.Combine(baseDir, "backups");
        var globalJson = Path.Combine(baseDir, "global_settings.json");

        // ── Observations ────────────────────────────────────────────────────
        var legacyExists = File.Exists(legacyPath);
        long legacySize = 0;
        var legacyReadable = false;
        if (legacyExists)
        {
            try
            {
                legacySize = new FileInfo(legacyPath).Length;
                using var conn = new SqliteConnection($"Data Source={legacyPath};Mode=ReadOnly");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT 1";
                cmd.ExecuteScalar();
                legacyReadable = true;
            }
            catch
            {
                legacyReadable = false;
            }
        }

        var tenantDbs = new System.Collections.Generic.List<string>();
        var tenantsExists = Directory.Exists(tenantsDir);
        if (tenantsExists)
        {
            foreach (var sub in Directory.EnumerateDirectories(tenantsDir).OrderBy(s => s))
            {
                var dbPath = Path.Combine(sub, "pos.db");
                if (File.Exists(dbPath))
                    tenantDbs.Add(Path.GetFileName(sub) + " -> " + dbPath);
            }
        }

        var legacyBackups = new System.Collections.Generic.List<FileInfo>();
        if (Directory.Exists(backupsDir))
        {
            foreach (var file in new DirectoryInfo(backupsDir)
                                 .EnumerateFiles("pos.db.backup-*.legacy")
                                 .OrderByDescending(f => f.LastWriteTimeUtc))
                legacyBackups.Add(file);
        }
        var mostRecentBackup = legacyBackups.FirstOrDefault()?.FullName;

        var runtimeFlag    = _global.Get("tenant_db_runtime_enabled") == "1";
        var globalMarker   = !string.IsNullOrEmpty(_global.Get("shared_to_tenant_migrated_at"));
        var lastTenant     = _global.Get("last_tenant_subdomain");
        var providerTenant = _pathProvider.IsTenantScoped;

        // ── Status branching ────────────────────────────────────────────────

        // Common base for the result.
        TenantDbRollbackReadinessReport BaseReport(
            TenantDbRollbackReadinessStatus status,
            bool canRollback,
            System.Collections.Generic.IReadOnlyList<string> warnings,
            System.Collections.Generic.IReadOnlyList<string> steps)
            => new()
            {
                Status                       = status,
                CanRollback                  = canRollback,
                LegacyDbPath                 = legacyPath,
                LegacyDbExists               = legacyExists,
                LegacyDbReadable             = legacyReadable,
                LegacyDbSizeBytes            = legacySize,
                TenantsDirectory             = tenantsDir,
                TenantsDirectoryExists       = tenantsExists,
                TenantDbCount                = tenantDbs.Count,
                TenantDbs                    = tenantDbs,
                BackupsDirectory             = backupsDir,
                LegacyBackupCount            = legacyBackups.Count,
                MostRecentBackupPath         = mostRecentBackup,
                GlobalSettingsPath           = globalJson,
                RuntimeFlagEnabled           = runtimeFlag,
                GlobalMigrationMarkerPresent = globalMarker,
                ProviderInTenantMode         = providerTenant,
                LastTenantSubdomain          = lastTenant,
                Warnings                     = warnings,
                RecommendedSteps             = steps,
            };

        // 1. Runtime mode off → not needed.
        if (!runtimeFlag)
        {
            return BaseReport(
                TenantDbRollbackReadinessStatus.NotInTenantRuntimeMode,
                canRollback: false,
                warnings: System.Array.Empty<string>(),
                steps: new[]
                {
                    "Runtime tenant DB mode is already off; rollback is not required.",
                    "Keep legacy pos.db and tenant DB backups unchanged.",
                });
        }

        // 2. Runtime mode is on. Compute warnings.
        var warnings = new System.Collections.Generic.List<string>();

        warnings.Add("Runtime tenant DB mode is currently enabled (tenant_db_runtime_enabled=1).");

        if (providerTenant)
            warnings.Add("Path provider is currently tenant-scoped. Stop the application before performing manual rollback so the tenant DB is not held open.");

        if (!tenantsExists)
            warnings.Add("tenants\\ directory is missing while runtime mode is on. Startup will fail the readiness gate.");

        if (!globalMarker)
            warnings.Add("Migration marker (shared_to_tenant_migrated_at) is missing while runtime mode is on — tenant runtime mode is likely already misconfigured.");

        if (tenantsExists && tenantDbs.Count == 0)
            warnings.Add("tenants\\ directory exists but contains no per-tenant DB files.");

        if (tenantDbs.Count > 0)
            warnings.Add($"{tenantDbs.Count} tenant DB(s) hold tenant-local data (pending/poison sales, sync watermarks). Archive (do not delete) the tenants\\ directory before disabling runtime mode.");

        if (legacyBackups.Count == 0 && legacyExists && legacyReadable)
            warnings.Add($"No legacy backups found under {backupsDir}, but legacy pos.db is readable. Rollback can proceed via flag-disable alone.");

        // 3. Blocking rule: legacy DB missing/unreadable AND no backup at all.
        var legacyOk = legacyExists && legacyReadable;
        if (!legacyOk && legacyBackups.Count == 0)
        {
            return BaseReport(
                TenantDbRollbackReadinessStatus.Blocked,
                canRollback: false,
                warnings: warnings,
                steps: new[]
                {
                    "Do not proceed until a valid legacy pos.db is restored.",
                    $"Restore from the latest backup if available under {backupsDir}.",
                    "If no backup exists, recover pos.db from external backup or storage before disabling runtime mode.",
                    "After legacy pos.db is restored, re-run this checker to obtain ready-state steps.",
                });
        }

        // 4. Ready / ReadyWithWarnings — safe non-destructive steps.
        var status = warnings.Count > 0
            ? TenantDbRollbackReadinessStatus.ReadyWithWarnings
            : TenantDbRollbackReadinessStatus.Ready;

        var legacyStep3 = legacyOk
            ? "3. Legacy pos.db is present and readable — no restore step required."
            : $"3. Restore legacy pos.db from the most recent backup: copy {mostRecentBackup} over {legacyPath}.";

        var steps = new[]
        {
            "1. Close PosSystem.exe so no SQLite file is held open.",
            $"2. Backup the entire {baseDir} directory (e.g. copy it alongside as a sibling like '{Path.GetFileName(baseDir)}.before-rollback-<utc>').",
            legacyStep3,
            $"4. In {globalJson}, set \"tenant_db_runtime_enabled\" to \"0\" (or remove the key). Do NOT remove migration markers unless you understand the consequences.",
            $"5. Do NOT permanently delete {tenantsDir}.",
            $"6. (Optional) Rename {tenantsDir} to '{Path.GetFileName(tenantsDir)}.before-rollback-<utc>' if you want to prevent accidental tenant-runtime use.",
            "7. Restart PosSystem.exe.",
            "8. Confirm the provider stays legacy — Debug log should NOT contain '[Phase 10.5B] Switched to tenant DB ...'.",
            "9. Validate login, sale, and sync against legacy pos.db.",
            "10. Retain the archived tenant DBs for at least 30 days before considering deletion.",
        };

        return BaseReport(status, canRollback: true, warnings: warnings, steps: steps);
    }
}
