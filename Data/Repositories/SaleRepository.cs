using Microsoft.EntityFrameworkCore;
using PosSystem.Core.Entities;

namespace PosSystem.Data.Repositories;

public class SaleRepository(IDbContextFactory<AppDbContext> factory)
{
    public void Add(Sale sale)
    {
        using var db = factory.CreateDbContext();
        db.Sales.Add(sale);
        db.SaveChanges();
    }

    public List<Sale> GetPendingSync()
    {
        using var db = factory.CreateDbContext();
        return [.. db.Sales.Include(s => s.Items).Where(s => !s.Synced)];
    }

    // Tenant-scoped: only sales belonging to the given tenant. An empty or
    // mismatched TenantSubdomain returns nothing — never push under a wrong tenant.
    public List<Sale> GetPendingSyncForTenant(string tenantSubdomain)
    {
        if (string.IsNullOrEmpty(tenantSubdomain)) return [];
        using var db = factory.CreateDbContext();
        return [.. db.Sales.Include(s => s.Items)
            .Where(s => !s.Synced && s.TenantSubdomain == tenantSubdomain)];
    }

    // Push-loop input: pending sales whose backoff window has elapsed and that
    // have not been quarantined as poison. Tenant-scoped to keep a Tenant-A
    // sale from leaking to a Tenant-B push session.
    public List<Sale> GetReadyToPushForTenant(string tenantSubdomain, DateTime now)
    {
        if (string.IsNullOrEmpty(tenantSubdomain)) return [];
        using var db = factory.CreateDbContext();
        return [.. db.Sales.Include(s => s.Items)
            .Where(s => !s.Synced
                        && !s.IsPoison
                        && s.TenantSubdomain == tenantSubdomain
                        && (s.NextRetryAt == null || s.NextRetryAt <= now))];
    }

    // Sales still affecting the local stock overlay: anything unsynced, plus
    // anything synced after the last product-sync reconcile point (server stock
    // has not yet been re-pulled to reflect that sale's decrement).
    public List<Sale> GetUnreconciledForTenant(string tenantSubdomain, DateTime lastReconcileAt)
    {
        if (string.IsNullOrEmpty(tenantSubdomain)) return [];
        using var db = factory.CreateDbContext();
        return [.. db.Sales.Include(s => s.Items)
            .Where(s => s.TenantSubdomain == tenantSubdomain
                        && (!s.Synced
                            || (s.SyncedAt != null && s.SyncedAt > lastReconcileAt)))];
    }

    public List<Sale> GetRecent(int count = 20)
    {
        using var db = factory.CreateDbContext();
        return [.. db.Sales
            .Include(s => s.Items)
            .OrderByDescending(s => s.CreatedAt)
            .Take(count)];
    }

    public void MarkSynced(string localId, string serverUuid)
    {
        using var db = factory.CreateDbContext();
        var sale = db.Sales.FirstOrDefault(s => s.LocalId == localId);
        if (sale is null) return;
        sale.Synced     = true;
        sale.ServerUuid = serverUuid;
        sale.SyncedAt   = DateTime.UtcNow;
        // Clear retry/poison bookkeeping so a successful sale never carries stale
        // error/backoff/poison fields that could confuse the operator panel.
        sale.RetryCount    = 0;
        sale.NextRetryAt   = null;
        sale.LastSyncError = "";
        sale.IsPoison      = false;
        db.SaveChanges();
    }

    public int GetPendingCount()
    {
        using var db = factory.CreateDbContext();
        return db.Sales.Count(s => !s.Synced);
    }

    public int GetPendingCountForTenant(string tenantSubdomain)
    {
        if (string.IsNullOrEmpty(tenantSubdomain)) return 0;
        using var db = factory.CreateDbContext();
        return db.Sales.Count(s => !s.Synced && s.TenantSubdomain == tenantSubdomain);
    }

    public int GetPoisonCountForTenant(string tenantSubdomain)
    {
        if (string.IsNullOrEmpty(tenantSubdomain)) return 0;
        using var db = factory.CreateDbContext();
        return db.Sales.Count(s => !s.Synced && s.IsPoison && s.TenantSubdomain == tenantSubdomain);
    }

    // Operator-visible failure list: any unsynced sale that has actually failed
    // at least once (LastSyncError set) OR is quarantined as poison. Newly-made
    // sales waiting for their first sync don't appear here — they show up only
    // after a real attempt fails.
    public List<Sale> GetFailedForTenant(string tenantSubdomain)
    {
        if (string.IsNullOrEmpty(tenantSubdomain)) return [];
        using var db = factory.CreateDbContext();
        return [.. db.Sales
            .Where(s => !s.Synced
                        && s.TenantSubdomain == tenantSubdomain
                        && (s.IsPoison || s.LastSyncError != ""))
            .OrderByDescending(s => s.IsPoison)
            .ThenByDescending(s => s.CreatedAt)];
    }

    // Records a transient failure: increments RetryCount, stores the error and
    // the next eligible attempt time. The next sync tick will skip the sale
    // until that time elapses.
    public void MarkRetryFailure(string localId, int retryCount, DateTime nextRetryAt, string errorMessage)
    {
        using var db = factory.CreateDbContext();
        var sale = db.Sales.FirstOrDefault(s => s.LocalId == localId);
        if (sale is null) return;
        sale.RetryCount    = retryCount;
        sale.NextRetryAt   = nextRetryAt;
        sale.LastSyncError = Truncate(errorMessage, 500);
        sale.IsPoison      = false;
        db.SaveChanges();
    }

    // Records a permanent failure: sale is quarantined and will not be retried
    // until an operator calls RequeueForRetry.
    public void MarkPoison(string localId, string errorMessage)
    {
        using var db = factory.CreateDbContext();
        var sale = db.Sales.FirstOrDefault(s => s.LocalId == localId);
        if (sale is null) return;
        sale.IsPoison      = true;
        sale.LastSyncError = Truncate(errorMessage, 500);
        db.SaveChanges();
    }

    // Operator-initiated unpoison: clears IsPoison/NextRetryAt/RetryCount so
    // the sale is eligible at the next push. Caller is expected to trigger a
    // sync after this returns.
    public void RequeueForRetry(string localId)
    {
        using var db = factory.CreateDbContext();
        var sale = db.Sales.FirstOrDefault(s => s.LocalId == localId);
        if (sale is null) return;
        sale.IsPoison    = false;
        sale.RetryCount  = 0;
        sale.NextRetryAt = null;
        // Keep LastSyncError as historical context until the next attempt overwrites it.
        db.SaveChanges();
    }

    // Bulk unblock for the current tenant — fires from the toolbar
    // "Hammasini qayta urinish" button. Resets two stuck states:
    //
    //   • Poisoned rows (IsPoison=true)           — permanent-failure quarantine.
    //   • Backoff rows (NextRetryAt > now)        — waiting for the 5m/15m/.../4h
    //                                               schedule to elapse.
    //
    // Both states keep a sale out of GetReadyToPushForTenant. The operator's
    // mental model when clicking the button is "retry everything stuck right
    // now" — earlier behavior only touched the poison rows, so a transient-
    // failure sale in mid-backoff would silently sit out the retry. Reset all
    // three retry fields so the very next push loop picks them up. Brand-new
    // unsynced sales (IsPoison=false AND NextRetryAt=null) are not touched —
    // they were never stuck.
    //
    // LastSyncError is intentionally preserved as historical context; the
    // next attempt overwrites it via MarkRetryFailure / MarkPoison, or
    // MarkSynced clears it on success.
    public int RequeueAllPoisonForTenant(string tenantSubdomain)
    {
        if (string.IsNullOrEmpty(tenantSubdomain)) return 0;
        using var db = factory.CreateDbContext();
        var rows = db.Sales
            .Where(s => !s.Synced
                        && s.TenantSubdomain == tenantSubdomain
                        && (s.IsPoison || s.NextRetryAt != null))
            .ToList();
        foreach (var s in rows)
        {
            s.IsPoison    = false;
            s.RetryCount  = 0;
            s.NextRetryAt = null;
        }
        if (rows.Count > 0) db.SaveChanges();
        return rows.Count;
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max));
}
