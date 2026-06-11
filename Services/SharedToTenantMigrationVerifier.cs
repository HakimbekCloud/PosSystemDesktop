using System.IO;
using Microsoft.Data.Sqlite;
using PosSystem.Data;
using PosSystem.Data.Repositories;

namespace PosSystem.Services;

// ── Report DTOs ──────────────────────────────────────────────────────────────

public sealed class TenantDbVerificationResult
{
    public string Subdomain               { get; init; } = "";
    public string TenantDbPath            { get; init; } = "";
    public bool   TenantDbExists          { get; init; }
    public bool   HasPerTenantMarker      { get; init; }
    public int    ExpectedSales           { get; init; }
    public int    ActualSales             { get; init; }
    public int    ExpectedSaleItems       { get; init; }
    public int    ActualSaleItems         { get; init; }
    public bool   IsOrphan                { get; init; }
    public bool   IsLastUsed              { get; init; }
    public bool   OrphanQuarantineValid   { get; init; }   // only meaningful for _orphan
    public int    ActualProducts          { get; init; }
    public int    ActualCustomers         { get; init; }
    public bool   Verified                { get; init; }
    public System.Collections.Generic.IReadOnlyList<string> Issues
        { get; init; } = System.Array.Empty<string>();
}

public sealed class MigrationVerificationReport
{
    public string SourceDbPath           { get; init; } = "";
    public bool   SourceDbExists         { get; init; }
    public bool   GlobalMarkerPresent    { get; init; }
    public int    OrphanCountInSource    { get; init; }
    public System.Collections.Generic.IReadOnlyList<TenantDbVerificationResult> Tenants
        { get; init; } = System.Array.Empty<TenantDbVerificationResult>();
    public bool   AllVerified            { get; init; }
    public System.DateTime GeneratedAtUtc { get; init; } = System.DateTime.UtcNow;
}

// ── Verifier ─────────────────────────────────────────────────────────────────

// Read-only post-migration check. Opens the legacy pos.db and every per-tenant
// DB found under tenants\, both in SQLite read-only mode, and compares row
// counts + quarantine markers + per-tenant migration markers. Does NOT modify
// any file. Safe to call at any time after a real migration; meaningless
// before one (returns AllVerified=false with no tenant entries).
public sealed class SharedToTenantMigrationVerifier
{
    private const string TenantMarkerKey  = "migrated_from_shared_at";
    private const string OrphanSubdomain  = "_orphan";

    private readonly ILocalDatabasePathProvider _pathProvider;
    private readonly GlobalSettingsRepository   _global;

    public SharedToTenantMigrationVerifier(
        ILocalDatabasePathProvider pathProvider,
        GlobalSettingsRepository global)
    {
        _pathProvider = pathProvider;
        _global       = global;
    }

    public System.Threading.Tasks.Task<MigrationVerificationReport> VerifyAsync(
        System.Threading.CancellationToken ct = default)
    {
        var sourcePath = _pathProvider.GetLegacyDbPath();
        var sourceExists = File.Exists(sourcePath);
        var globalMarker = !string.IsNullOrEmpty(_global.Get("shared_to_tenant_migrated_at"));

        if (!sourceExists)
        {
            return System.Threading.Tasks.Task.FromResult(new MigrationVerificationReport
            {
                SourceDbPath = sourcePath,
                SourceDbExists = false,
                GlobalMarkerPresent = globalMarker,
                AllVerified = false,
            });
        }

        var sourceDir = Path.GetDirectoryName(sourcePath)!;
        var tenantsDir = Path.Combine(sourceDir, "tenants");
        var lastUsed = (_global.Get("last_tenant_subdomain") ?? "").Trim();

        var tenants = new System.Collections.Generic.List<TenantDbVerificationResult>();
        var orphanInSource = QueryInt(sourcePath, "SELECT COUNT(*) FROM Sales WHERE TenantSubdomain = ''");

        if (Directory.Exists(tenantsDir))
        {
            foreach (var subDir in Directory.EnumerateDirectories(tenantsDir).OrderBy(s => s))
            {
                ct.ThrowIfCancellationRequested();
                var sub = Path.GetFileName(subDir);
                var dbPath = Path.Combine(subDir, "pos.db");
                tenants.Add(VerifyOneTenant(sourcePath, sub, dbPath, lastUsed));
            }
        }

        var allOk = globalMarker
                    && tenants.Count > 0
                    && tenants.All(t => t.Verified);

        return System.Threading.Tasks.Task.FromResult(new MigrationVerificationReport
        {
            SourceDbPath        = sourcePath,
            SourceDbExists      = true,
            GlobalMarkerPresent = globalMarker,
            OrphanCountInSource = orphanInSource,
            Tenants             = tenants,
            AllVerified         = allOk,
        });
    }

    // ── Per-tenant verification ──────────────────────────────────────────────

    private TenantDbVerificationResult VerifyOneTenant(
        string sourcePath, string subdomain, string tenantDbPath, string lastUsed)
    {
        var issues = new System.Collections.Generic.List<string>();
        var isOrphan = subdomain == OrphanSubdomain;
        var isLastUsed = string.Equals(subdomain, lastUsed, System.StringComparison.OrdinalIgnoreCase);

        if (!File.Exists(tenantDbPath))
        {
            issues.Add($"Target DB missing: {tenantDbPath}");
            return new TenantDbVerificationResult
            {
                Subdomain    = subdomain,
                TenantDbPath = tenantDbPath,
                IsOrphan     = isOrphan,
                IsLastUsed   = isLastUsed,
                Issues       = issues,
                Verified     = false,
            };
        }

        // Expected counts in source (read-only).
        var expectedSales = isOrphan
            ? QueryInt(sourcePath, "SELECT COUNT(*) FROM Sales WHERE TenantSubdomain = ''")
            : QueryInt(sourcePath, "SELECT COUNT(*) FROM Sales WHERE TenantSubdomain = @s",
                        ("@s", subdomain));
        var expectedItems = isOrphan
            ? QueryInt(sourcePath,
                "SELECT COUNT(*) FROM SaleItems WHERE SaleLocalId IN " +
                "(SELECT LocalId FROM Sales WHERE TenantSubdomain = '')")
            : QueryInt(sourcePath,
                "SELECT COUNT(*) FROM SaleItems WHERE SaleLocalId IN " +
                "(SELECT LocalId FROM Sales WHERE TenantSubdomain = @s)",
                ("@s", subdomain));

        // Actual counts in tenant DB (read-only).
        var actualSales = QueryInt(tenantDbPath, "SELECT COUNT(*) FROM Sales");
        var actualItems = QueryInt(tenantDbPath, "SELECT COUNT(*) FROM SaleItems");

        var hasMarker = !string.IsNullOrEmpty(QueryString(tenantDbPath,
            "SELECT Value FROM Settings WHERE Key = @k", ("@k", TenantMarkerKey)));

        if (!hasMarker) issues.Add("Per-tenant marker missing.");
        if (actualSales != expectedSales)
            issues.Add($"Sales count mismatch: expected {expectedSales}, got {actualSales}.");
        if (actualItems != expectedItems)
            issues.Add($"SaleItems count mismatch: expected {expectedItems}, got {actualItems}.");

        // Orphan-specific: every sale in _orphan must be poison + carry the
        // quarantine message.
        bool orphanQuarantineValid = true;
        if (isOrphan && actualSales > 0)
        {
            var nonPoison = QueryInt(tenantDbPath,
                "SELECT COUNT(*) FROM Sales WHERE IsPoison = 0 OR LastSyncError = ''");
            if (nonPoison > 0)
            {
                orphanQuarantineValid = false;
                issues.Add($"Orphan tenant has {nonPoison} sales lacking poison/quarantine markers.");
            }
        }

        var actualProducts  = QueryInt(tenantDbPath, "SELECT COUNT(*) FROM Products");
        var actualCustomers = QueryInt(tenantDbPath, "SELECT COUNT(*) FROM Customers");

        // Catalog should only be present in the last-used tenant DB. Empty
        // catalog in non-last-used DBs is expected, not a failure.
        if (!isLastUsed && !isOrphan && (actualProducts > 0 || actualCustomers > 0))
            issues.Add($"Non-last-used tenant has unexpected catalog rows " +
                       $"(Products={actualProducts}, Customers={actualCustomers}).");

        var verified = issues.Count == 0;

        return new TenantDbVerificationResult
        {
            Subdomain             = subdomain,
            TenantDbPath          = tenantDbPath,
            TenantDbExists        = true,
            HasPerTenantMarker    = hasMarker,
            ExpectedSales         = expectedSales,
            ActualSales           = actualSales,
            ExpectedSaleItems     = expectedItems,
            ActualSaleItems       = actualItems,
            IsOrphan              = isOrphan,
            IsLastUsed            = isLastUsed,
            OrphanQuarantineValid = orphanQuarantineValid,
            ActualProducts        = actualProducts,
            ActualCustomers       = actualCustomers,
            Issues                = issues,
            Verified              = verified,
        };
    }

    // ── Read-only SQLite helpers ─────────────────────────────────────────────

    private static int QueryInt(string dbPath, string sql,
        params (string Name, object Value)[] parameters)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in parameters) cmd.Parameters.AddWithValue(n, v ?? System.DBNull.Value);
        var result = cmd.ExecuteScalar();
        return result is null || result is System.DBNull
            ? 0
            : System.Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string? QueryString(string dbPath, string sql,
        params (string Name, object Value)[] parameters)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in parameters) cmd.Parameters.AddWithValue(n, v ?? System.DBNull.Value);
        return cmd.ExecuteScalar() as string;
    }
}
