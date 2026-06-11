using System;
using System.IO;

namespace PosSystem.Data;

// Default implementation of ILocalDatabasePathProvider. Starts in legacy mode
// (single shared pos.db) so the app behaves exactly like it did before Phase
// 10.3A. Phase 10.3B will call UseTenantDatabase(...) from the login flow.
//
// Path layout:
//   %LocalAppData%\PosSystem\pos.db                       ← legacy (default)
//   %LocalAppData%\PosSystem\tenants\<subdomain>\pos.db   ← tenant mode
//
// Construction creates only the base directory; tenant subdirectories are
// NOT created here. The first caller that actually opens a tenant DB is
// responsible for ensuring its directory exists (Phase 10.3B / 10.4).
public sealed class TenantAwareLocalDatabasePathProvider : ILocalDatabasePathProvider
{
    private readonly string _baseDir;
    private readonly object _lock = new();
    private string? _currentTenant; // null = legacy mode

    public event EventHandler? DatabasePathChanged;

    public TenantAwareLocalDatabasePathProvider()
    {
        _baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PosSystem");
        Directory.CreateDirectory(_baseDir);
    }

    public string CurrentDbPath
    {
        get
        {
            lock (_lock)
            {
                return _currentTenant is null
                    ? GetLegacyDbPath()
                    : GetTenantDbPath(_currentTenant);
            }
        }
    }

    public string CurrentConnectionString => $"Data Source={CurrentDbPath}";

    public bool IsTenantScoped
    {
        get { lock (_lock) { return _currentTenant is not null; } }
    }

    public string GetLegacyDbPath() => Path.Combine(_baseDir, "pos.db");

    public string GetTenantDbPath(string tenantSubdomain)
    {
        if (string.IsNullOrWhiteSpace(tenantSubdomain))
            throw new ArgumentException("Tenant subdomain required", nameof(tenantSubdomain));
        var safe = SanitizeTenant(tenantSubdomain);
        return Path.Combine(_baseDir, "tenants", safe, "pos.db");
    }

    public void UseLegacyDatabase()
    {
        bool changed;
        lock (_lock)
        {
            changed = _currentTenant is not null;
            _currentTenant = null;
        }
        if (changed) DatabasePathChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UseTenantDatabase(string tenantSubdomain)
    {
        if (string.IsNullOrWhiteSpace(tenantSubdomain))
            throw new ArgumentException("Tenant subdomain required", nameof(tenantSubdomain));

        var safe = SanitizeTenant(tenantSubdomain);
        bool changed;
        lock (_lock)
        {
            changed = !string.Equals(_currentTenant, safe, StringComparison.Ordinal);
            _currentTenant = safe;
        }
        if (changed) DatabasePathChanged?.Invoke(this, EventArgs.Empty);
    }

    // Tenant subdomains come from user input; normalize so case typos and
    // stray whitespace don't spawn parallel tenant directories, and replace
    // any character that's illegal in a Windows directory name.
    private static string SanitizeTenant(string raw)
    {
        var s = raw.Trim().ToLowerInvariant();
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s;
    }
}
