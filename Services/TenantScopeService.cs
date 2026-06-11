using System.IO;
using Microsoft.EntityFrameworkCore;
using PosSystem.Data;

namespace PosSystem.Services;

// Orchestrates a safe switch of the local DB target. Holds the sync gate and
// pauses the background timer + connectivity listener while the path provider
// flips, ensures the target directory exists, then runs EF Migrate on the new
// database before restoring sync.
//
// Phase 10.3B introduces this service in DI but does NOT call it from any
// production flow. The app continues to use the shared legacy pos.db because
// nothing invokes SwitchToTenantAsync. Phase 10.5 / 10.4 will wire it in.
public sealed class TenantScopeService
{
    // L3: set for the duration of a path-provider switch. PauseAsync drains the
    // background sync and holds _syncGate, but UI-triggered HTTP requests (shift
    // probe, operator clients) are NOT routed through the gate and could still
    // fly mid-switch — hitting the wrong DB-backed settings / a half-flipped
    // provider. TenantAuthHeaderHandler checks this flag and fails such requests
    // fast with a clear exception instead of letting them race the switch. Static
    // because the handler is constructed by the HttpClient pipeline, not by DI of
    // this service; volatile for cross-thread visibility (the switch runs on a
    // different thread from the UI request). Switches are short and serialized by
    // _syncGate, so the window is tiny.
    private static volatile bool _switchInProgress;
    internal static bool IsSwitchInProgress => _switchInProgress;

    private readonly ILocalDatabasePathProvider     _pathProvider;
    private readonly SyncService                    _sync;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public TenantScopeService(
        ILocalDatabasePathProvider pathProvider,
        SyncService sync,
        IDbContextFactory<AppDbContext> dbFactory)
    {
        _pathProvider = pathProvider;
        _sync         = sync;
        _dbFactory    = dbFactory;
    }

    public async System.Threading.Tasks.Task SwitchToTenantAsync(string tenantSubdomain)
    {
        if (string.IsNullOrWhiteSpace(tenantSubdomain))
            throw new System.ArgumentException("Tenant subdomain required", nameof(tenantSubdomain));

        await PerformSwitchAsync(() => _pathProvider.UseTenantDatabase(tenantSubdomain));
    }

    public System.Threading.Tasks.Task SwitchToLegacyAsync()
        => PerformSwitchAsync(_pathProvider.UseLegacyDatabase);

    // Core lifecycle: pause sync → flip path → ensure directory → run migrations
    // → resume sync. Every step except the path flip is idempotent. Exceptions
    // bubble after the finally block restores sync to its prior state.
    private async System.Threading.Tasks.Task PerformSwitchAsync(System.Action switchAction)
    {
        bool resumeBackground = _sync.IsBackgroundRunning;
        await _sync.PauseAsync();
        _switchInProgress = true; // L3: fail-fast any non-sync request mid-switch
        try
        {
            switchAction();

            // Make sure the new tenant subdirectory exists before EF tries to
            // open the file. CurrentDbPath reads from the provider after the
            // switch and reflects the new target.
            var path = _pathProvider.CurrentDbPath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // Apply migrations to the new (or freshly-created) DB. On a brand
            // new tenant file this lays down the full Phase 8 InitialCreate
            // schema; on an existing tenant DB it's a no-op.
            using var db = _dbFactory.CreateDbContext();
            db.Database.Migrate();
        }
        finally
        {
            _switchInProgress = false;
            _sync.Resume(resumeBackground);
        }
    }
}
