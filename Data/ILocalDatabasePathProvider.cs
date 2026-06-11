namespace PosSystem.Data;

// Resolves the path/connection string the app should use to open its local
// SQLite database. Phase 10.1 introduced the seam; Phase 10.3A extends it
// with tenant-aware switching primitives. The shipped implementation defaults
// to legacy mode (single shared pos.db) and never switches to a tenant DB
// unless an explicit caller invokes UseTenantDatabase(...). Phase 10.3B will
// wire that call site into the login flow.
public interface ILocalDatabasePathProvider
{
    // The path the provider is configured to use right now. In legacy mode
    // this is the shared %LocalAppData%\PosSystem\pos.db; in tenant mode it
    // is %LocalAppData%\PosSystem\tenants\<sub>\pos.db.
    string CurrentDbPath           { get; }

    // Convenience: "Data Source=<CurrentDbPath>".
    string CurrentConnectionString { get; }

    // True after UseTenantDatabase(...) succeeds. False in legacy mode (the
    // current shipping default).
    bool   IsTenantScoped          { get; }

    // Pure path helpers — do NOT mutate provider state, do NOT create
    // directories. Future migration code uses these to enumerate or copy.
    string GetLegacyDbPath();
    string GetTenantDbPath(string tenantSubdomain);

    // Switch into legacy mode. No-op if already legacy. Fires
    // DatabasePathChanged when the path actually changes.
    void   UseLegacyDatabase();

    // Switch into tenant mode for the given subdomain. No-op when already
    // pointing at the same tenant. Fires DatabasePathChanged on change.
    // Does NOT open the DB, does NOT create the directory.
    void   UseTenantDatabase(string tenantSubdomain);

    // Raised after a successful mode/path change so future phases can
    // dispose pooled DbContexts, drain sync, rebuild repositories, etc.
    event System.EventHandler? DatabasePathChanged;
}
