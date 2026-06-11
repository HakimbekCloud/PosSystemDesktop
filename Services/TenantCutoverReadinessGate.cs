using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PosSystem.Data;
using PosSystem.Data.Repositories;

namespace PosSystem.Services;

// ── Status enum ──────────────────────────────────────────────────────────────

public enum TenantDbCutoverReadinessStatus
{
    Disabled,              // tenant_db_runtime_enabled != "1"
    Blocked,               // flag enabled but at least one blocking check failed
    AllowedWithWarnings,   // every blocking check passed, but warnings exist
    Allowed                // every check passed, no warnings
}

// ── Report DTO ───────────────────────────────────────────────────────────────

// Per-tenant readiness snapshot. Phase 10.5A.1 expands the original Ready-bool
// shape into a status enum plus a fanned-out check list so an operator UI can
// surface individual blockers and warnings. The gate is read-only — fields
// represent observation, never side effects.
public sealed class TenantCutoverReadinessReport
{
    public TenantDbCutoverReadinessStatus Status { get; init; }
    public bool   CanCutOver                     { get; init; }

    public string TenantSubdomain                { get; init; } = "";
    public string SanitizedTenantSubdomain       { get; init; } = "";
    public string LegacyDbPath                   { get; init; } = "";
    public string TargetDbPath                   { get; init; } = "";

    public bool   RuntimeFeatureEnabled          { get; init; }
    public bool   GlobalMigrationMarkerPresent   { get; init; }
    public bool   LegacyDbExists                 { get; init; }
    public bool   TargetDbExists                 { get; init; }
    public bool   MigrationHistoryExists         { get; init; }
    public bool   PerTenantMarkerExists          { get; init; }
    public bool   SchemaUpToDate                 { get; init; }
    public bool   ProviderInLegacyMode           { get; init; }
    public bool   VerifierPassed                 { get; init; }

    public System.Collections.Generic.IReadOnlyList<string> Warnings
        { get; init; } = System.Array.Empty<string>();
    public System.Collections.Generic.IReadOnlyList<string> Errors
        { get; init; } = System.Array.Empty<string>();

    public System.DateTime CheckedAtUtc          { get; init; } = System.DateTime.UtcNow;
}

// ── Gate ─────────────────────────────────────────────────────────────────────

// Strict, read-only readiness checks for runtime tenant DB cutover. Phase
// 10.5B will consult this before flipping the provider during login. Phase
// 10.5A.1 only ships the gate — no production call site exists.
public sealed class TenantCutoverReadinessGate
{
    private const string RuntimeFlagKey           = "tenant_db_runtime_enabled";
    private const string GlobalMigrationMarkerKey = "shared_to_tenant_migrated_at";
    private const string TenantMarkerKey          = "migrated_from_shared_at";
    private const string LastTenantKey            = "last_tenant_subdomain";
    private const string OrphanSubdomain          = "_orphan";

    private readonly ILocalDatabasePathProvider _pathProvider;
    private readonly GlobalSettingsRepository   _global;
    private readonly SharedToTenantMigrationVerifier _verifier;

    public TenantCutoverReadinessGate(
        ILocalDatabasePathProvider pathProvider,
        GlobalSettingsRepository global,
        SharedToTenantMigrationVerifier verifier)
    {
        _pathProvider = pathProvider;
        _global       = global;
        _verifier     = verifier;
    }

    public async System.Threading.Tasks.Task<TenantCutoverReadinessReport> CheckAsync(
        string tenantSubdomain,
        System.Threading.CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantSubdomain))
            throw new System.ArgumentException("Tenant subdomain required", nameof(tenantSubdomain));

        var legacyPath = _pathProvider.GetLegacyDbPath();
        var targetPath = _pathProvider.GetTenantDbPath(tenantSubdomain);
        var sanitized  = Path.GetFileName(Path.GetDirectoryName(targetPath)) ?? tenantSubdomain;

        // ── Feature flag is the first gate. When closed, no other check runs:
        //    a Disabled report is the operator-facing truth.
        var runtimeEnabled = _global.Get(RuntimeFlagKey) == "1";
        if (!runtimeEnabled)
        {
            return new TenantCutoverReadinessReport
            {
                Status                       = TenantDbCutoverReadinessStatus.Disabled,
                CanCutOver                   = false,
                TenantSubdomain              = tenantSubdomain,
                SanitizedTenantSubdomain     = sanitized,
                LegacyDbPath                 = legacyPath,
                TargetDbPath                 = targetPath,
                RuntimeFeatureEnabled        = false,
                ProviderInLegacyMode         = !_pathProvider.IsTenantScoped,
                Errors                       = new[] { "Runtime tenant DB mode is disabled." },
            };
        }

        var errors   = new System.Collections.Generic.List<string>();
        var warnings = new System.Collections.Generic.List<string>();

        // 1. Global migration marker.
        var globalMarker = !string.IsNullOrEmpty(_global.Get(GlobalMigrationMarkerKey));
        if (!globalMarker)
            errors.Add($"Shared-to-tenant migration has not completed " +
                       $"(global_settings.json:{GlobalMigrationMarkerKey} is empty).");

        // 2. Legacy pos.db rollback source.
        var legacyExists = File.Exists(legacyPath);
        if (!legacyExists)
            errors.Add("Legacy pos.db is missing; rollback source is not available.");

        // 3. Target tenant DB file.
        var targetExists = File.Exists(targetPath);
        if (!targetExists)
            errors.Add($"Tenant DB file is missing at {targetPath}.");

        // 4-6. Per-tenant marker + schema state (only meaningful if target exists).
        bool markerExists = false;
        bool historyExists = false;
        bool schemaUpToDate = false;

        if (targetExists)
        {
            try
            {
                markerExists = !string.IsNullOrEmpty(
                    ReadStringReadOnly(targetPath,
                        "SELECT Value FROM Settings WHERE Key = @k",
                        ("@k", TenantMarkerKey)));
                if (!markerExists)
                    errors.Add($"Per-tenant migration marker is missing ({TenantMarkerKey}).");
            }
            catch (System.Exception ex)
            {
                errors.Add($"Could not read tenant DB Settings: {ex.Message}");
            }

            historyExists = TableExistsReadOnly(targetPath, "__EFMigrationsHistory");
            if (!historyExists)
                errors.Add("Tenant DB lacks __EFMigrationsHistory; schema state cannot be confirmed.");

            if (historyExists)
            {
                try
                {
                    var opts = new DbContextOptionsBuilder<AppDbContext>()
                        .UseSqlite($"Data Source={targetPath};Mode=ReadOnly")
                        .Options;
                    using var db = new AppDbContext(opts);
                    var pending = (await db.Database.GetPendingMigrationsAsync(ct)
                        .ConfigureAwait(false)).ToList();
                    schemaUpToDate = pending.Count == 0;
                    if (!schemaUpToDate)
                        errors.Add("Tenant DB has pending EF migrations: " + string.Join(", ", pending));
                }
                catch (System.Exception ex)
                {
                    errors.Add($"Could not query tenant DB migrations: {ex.Message}");
                }
            }
        }

        // 7. Path provider must currently be in legacy mode — otherwise a
        //    cutover already happened (or was triggered outside the lifecycle).
        var providerLegacy = !_pathProvider.IsTenantScoped;
        if (!providerLegacy)
            errors.Add("Path provider is already in tenant mode — cutover already happened or " +
                       "the provider was switched outside the supported lifecycle.");

        // 8. Verifier integration. Source of truth for "this tenant's tenant DB
        //    matches what the source has." Issues bubble up as errors.
        bool verifierPassed = false;
        TenantDbVerificationResult? verifierEntry = null;
        try
        {
            var verification = await _verifier.VerifyAsync(ct).ConfigureAwait(false);

            if (!verification.SourceDbExists)
                errors.Add("Verifier reports source pos.db missing.");
            else if (!verification.GlobalMarkerPresent)
                errors.Add("Verifier reports global migration marker missing.");

            verifierEntry = verification.Tenants.FirstOrDefault(t =>
                string.Equals(t.Subdomain, sanitized,        System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Subdomain, tenantSubdomain,  System.StringComparison.OrdinalIgnoreCase));

            if (verifierEntry is null)
            {
                errors.Add("Requested tenant was not found in verification report.");
            }
            else if (!verifierEntry.Verified)
            {
                foreach (var issue in verifierEntry.Issues)
                    errors.Add($"Verifier issue: {issue}");
            }
            else
            {
                verifierPassed = true;
            }

            // 9. Orphan warning (not blocking).
            if (verification.OrphanCountInSource > 0)
                warnings.Add($"Orphan quarantined sales exist in source " +
                             $"({verification.OrphanCountInSource}) and require operator review.");

            var orphanPath = _pathProvider.GetTenantDbPath(OrphanSubdomain);
            if (File.Exists(orphanPath))
                warnings.Add($"Orphan tenant DB exists at {orphanPath} and requires operator review.");
        }
        catch (System.Exception ex)
        {
            errors.Add($"Verifier failed: {ex.Message}");
        }

        // Determine final status.
        TenantDbCutoverReadinessStatus status;
        bool canCutOver;
        if (errors.Count > 0)
        {
            status     = TenantDbCutoverReadinessStatus.Blocked;
            canCutOver = false;
        }
        else if (warnings.Count > 0)
        {
            status     = TenantDbCutoverReadinessStatus.AllowedWithWarnings;
            canCutOver = true;
        }
        else
        {
            status     = TenantDbCutoverReadinessStatus.Allowed;
            canCutOver = true;
        }

        return new TenantCutoverReadinessReport
        {
            Status                       = status,
            CanCutOver                   = canCutOver,
            TenantSubdomain              = tenantSubdomain,
            SanitizedTenantSubdomain     = sanitized,
            LegacyDbPath                 = legacyPath,
            TargetDbPath                 = targetPath,
            RuntimeFeatureEnabled        = runtimeEnabled,
            GlobalMigrationMarkerPresent = globalMarker,
            LegacyDbExists               = legacyExists,
            TargetDbExists               = targetExists,
            MigrationHistoryExists       = historyExists,
            PerTenantMarkerExists        = markerExists,
            SchemaUpToDate               = schemaUpToDate,
            ProviderInLegacyMode         = providerLegacy,
            VerifierPassed               = verifierPassed,
            Warnings                     = warnings,
            Errors                       = errors,
        };
    }

    // Convenience for the future operator UI / Phase 10.5B: check the tenant
    // that the login screen would prefill. Returns a Blocked report (rather
    // than throwing) when no last-used tenant exists yet.
    public System.Threading.Tasks.Task<TenantCutoverReadinessReport> CheckLastUsedTenantAsync(
        System.Threading.CancellationToken ct = default)
    {
        var lastUsed = _global.Get(LastTenantKey);
        if (string.IsNullOrWhiteSpace(lastUsed))
        {
            return System.Threading.Tasks.Task.FromResult(new TenantCutoverReadinessReport
            {
                Status                = TenantDbCutoverReadinessStatus.Blocked,
                CanCutOver            = false,
                TenantSubdomain       = "",
                LegacyDbPath          = _pathProvider.GetLegacyDbPath(),
                RuntimeFeatureEnabled = _global.Get(RuntimeFlagKey) == "1",
                ProviderInLegacyMode  = !_pathProvider.IsTenantScoped,
                Errors                = new[] { "No last-used tenant in global settings; nothing to check." },
            });
        }
        return CheckAsync(lastUsed, ct);
    }

    // ── Read-only SQLite helpers ─────────────────────────────────────────────

    private static string? ReadStringReadOnly(string dbPath, string sql,
        params (string Name, object Value)[] parameters)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in parameters)
            cmd.Parameters.AddWithValue(n, v ?? System.DBNull.Value);
        return cmd.ExecuteScalar() as string;
    }

    private static bool TableExistsReadOnly(string dbPath, string tableName)
    {
        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=@n";
            cmd.Parameters.AddWithValue("@n", tableName);
            var result = cmd.ExecuteScalar();
            return result is not null && result is not System.DBNull;
        }
        catch
        {
            return false;
        }
    }
}
