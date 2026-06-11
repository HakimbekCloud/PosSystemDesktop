using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using PosSystem.Data;
using PosSystem.Data.Repositories;

namespace PosSystem.Services;

public class AuthService(
    ApiClient api,
    SettingsRepository settings,
    GlobalSettingsRepository globalSettings,
    ILocalDatabasePathProvider pathProvider,
    IDbContextFactory<AppDbContext> dbFactory)
{
    public bool HasValidSession() =>
        !string.IsNullOrEmpty(settings.GetDecrypted("auth_token"));

    public string? GetCurrentUserName() =>
        settings.Get("user_name");

    public long GetCurrentUserId() =>
        long.TryParse(settings.Get("user_id"), out var id) ? id : 0;

    // Phase 10.9B: backend already returns User.Role on /api/v1/auth/login.
    // LoginAsync persists it to Settings["user_role"]; this getter exposes it
    // to OperatorAccessService for role-gated operator surfaces. Empty when
    // the session was created before Phase 10.9B (no role recorded) or when
    // the user isn't logged in.
    public string GetCurrentUserRole() =>
        settings.Get("user_role") ?? "";

    // Current session's tenant — used by sync filters, sales tagging, overlay
    // queries. Removed on logout. Reads from the legacy SettingsRepository.
    public string GetLastTenantSubdomain() =>
        settings.Get("tenant_subdomain") ?? "";

    // For the login screen prefill: prefer the last-used tenant remembered in
    // global storage (persists across logout). Falls back to the legacy
    // in-session key for pre-Phase-10.2 installs.
    public string GetPrefillTenantSubdomain() =>
        globalSettings.Get("last_tenant_subdomain")
        ?? settings.Get("tenant_subdomain")
        ?? "";

    // api_base_url is machine-level (typically a single backend per
    // deployment). Read from global first, fall back to legacy for upgrades.
    public string GetSavedServerUrl() =>
        globalSettings.Get("api_base_url")
        ?? settings.Get("api_base_url")
        ?? "";

    public async Task<(bool Success, string Message)> LoginAsync(
        string tenantSubdomain, string username, string password,
        string? serverUrl = null)
    {
        if (string.IsNullOrWhiteSpace(tenantSubdomain))
            return (false, "Tashkilot kodini kiriting");

        var runtimeModeActive = globalSettings.Get("tenant_db_runtime_enabled") == "1";

        // Phase 10.5B.1: when runtime tenant DB mode is enabled, AuthService
        // refuses to write session credentials into the legacy pos.db. The
        // caller (LoginViewModel) is responsible for running the readiness
        // gate and TenantScopeService.SwitchToTenantAsync before calling here.
        if (runtimeModeActive && !pathProvider.IsTenantScoped)
        {
            return (false,
                "Tenant DB runtime mode is enabled, but tenant DB is not active. " +
                "Login must switch to tenant DB before authentication.");
        }

        // Cross-tenant safety: even after a successful switch, refuse to log in
        // under a different tenant subdomain than the one the provider is
        // pointed at. Operator must restart the app or log out so the runtime
        // returns to legacy mode and the next login re-runs the readiness gate.
        if (pathProvider.IsTenantScoped)
        {
            var requestedPath = pathProvider.GetTenantDbPath(tenantSubdomain);
            if (!string.Equals(pathProvider.CurrentDbPath, requestedPath,
                System.StringComparison.OrdinalIgnoreCase))
            {
                return (false,
                    "Boshqa tashkilotga kirish uchun ilovani qayta ishga tushiring. " +
                    "(Switching tenants requires an app restart while runtime tenant DB mode is on.)");
            }
        }

        // If the user typed a server URL, persist it; otherwise keep the saved one.
        if (!string.IsNullOrWhiteSpace(serverUrl))
            globalSettings.Set("api_base_url", serverUrl.Trim());

        settings.Set("tenant_subdomain", tenantSubdomain.Trim());
        api.ApplyBaseUrl();
        api.ApplyTenantHeader();

        try
        {
            var response = await api.LoginAsync(username, password);

            settings.SetEncrypted("auth_token",    response.AccessToken);
            settings.SetEncrypted("refresh_token", response.RefreshToken);
            settings.Set("user_name",     response.User.Username);
            settings.Set("user_id",       response.User.Id.ToString());
            settings.Set("user_role",     response.User.Role ?? "");
            if (response.Tenant is not null)
                settings.Set("tenant_name", response.Tenant.Name);

            // Remember the tenant globally so the login screen can prefill it
            // after logout / app restart. Done only on successful auth so a
            // mistyped subdomain doesn't poison the prefill.
            globalSettings.Set("last_tenant_subdomain", tenantSubdomain.Trim());

            api.ApplyAuthToken();
            return (true, "");
        }
        catch (HttpRequestException ex) when
            (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return (false, "Login yoki parol noto'g'ri");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public void Logout()
    {
        // Best-effort server-side revocation. Must run before ClearUserData
        // so the tokens are still readable when we build the logout request.
        // Fire-and-forget on the thread-pool; the caller (PosViewModel) does
        // not need to await the result because the cashier's next action
        // (navigating to the login screen) is not blocked by a network call.
        _ = Task.Run(async () =>
        {
            try { await api.LogoutAsync(); }
            catch { /* network failure is non-fatal; tokens expire naturally */ }
        });

        ClearUserData();

        // Re-apply auth headers so the HttpClient no longer carries the old token.
        api.ApplyAuthToken();
        api.ApplyTenantHeader();
    }

    // Phase 10.5C: logout cleanup is conditional on runtime tenant DB mode.
    //
    //   • Legacy shared-DB mode (tenant_db_runtime_enabled != "1") OR provider
    //     not tenant-scoped → ClearLegacyUserData(). Wipes catalog + tenant
    //     routing settings + synced sales so a different tenant can log in
    //     next on the shared file. Unsynced sales are preserved (Phase 0).
    //
    //   • Tenant DB runtime mode AND provider tenant-scoped → ClearSessionOnly().
    //     Only the auth/identity rows are removed; tenant catalog, sales,
    //     pending/poison rows, retry state, sync watermarks, bootstrap
    //     markers and routing UUIDs all stay so the cashier can resume
    //     against cached data after re-login.
    private void ClearUserData()
    {
        var runtimeMode = globalSettings.Get("tenant_db_runtime_enabled") == "1";
        if (runtimeMode && pathProvider.IsTenantScoped)
            ClearSessionOnly();
        else
            ClearLegacyUserData();
    }

    private void ClearSessionOnly()
    {
        // Tenant DB runtime mode: keep tenant catalog/sales/watermarks and only
        // remove the current-cashier session keys. Catalog (Products, Customers,
        // Categories, PriceLists, ProductTypes), Sales/SaleItems (both synced
        // history and pending/poison rows), bootstrap_completed_at,
        // last_*_sync_at, last_stock_reconcile_at, default_branch_uuid,
        // default_cashbox_uuid, default_currency_id, default_price_list_id,
        // cashbox_uuid_cash/card/bank — all stay.
        foreach (var key in new[]
        {
            "auth_token", "refresh_token",
            "user_name",  "user_id",  "user_role", "tenant_name", "tenant_subdomain",
        })
            settings.Remove(key);
    }

    private void ClearLegacyUserData()
    {
        // Legacy shared-DB mode (pre-Phase-10): wipe catalog so a different
        // tenant logging in next on the same pos.db doesn't see stale data.
        // EXCEPTION: unsynced sales (Synced = 0) survive — Phase 0 contract.
        using var db = dbFactory.CreateDbContext();
        db.Database.ExecuteSqlRaw(
            "DELETE FROM SaleItems WHERE SaleLocalId IN " +
            "(SELECT LocalId FROM Sales WHERE Synced = 1)");
        db.Database.ExecuteSqlRaw("DELETE FROM Sales WHERE Synced = 1");
        db.Database.ExecuteSqlRaw("DELETE FROM Products");
        db.Database.ExecuteSqlRaw("DELETE FROM Customers");
        db.Database.ExecuteSqlRaw("DELETE FROM PriceLists");
        db.Database.ExecuteSqlRaw("DELETE FROM ProductTypes");

        // Per-tenant incremental-sync watermarks must be cleared too, or the
        // next sync after re-login would issue `?updatedAfter=<old watermark>`
        // against an empty local cache and the backend's strict `>` filter
        // would skip every product older than that watermark — exactly the
        // "products vanish on re-login" failure. Captured before tenant_subdomain
        // is removed below.
        var tenant = settings.Get("tenant_subdomain");
        if (!string.IsNullOrEmpty(tenant))
        {
            foreach (var perTenantKey in new[]
            {
                $"last_product_sync_at:{tenant}",
                $"last_customer_sync_at:{tenant}",
                $"last_stock_reconcile_at:{tenant}",
                $"bootstrap_completed_at:{tenant}",
            })
                settings.Remove(perTenantKey);
        }

        // Clear user- and tenant-specific settings.
        // api_base_url, tablet_mode, ui_scale are intentionally kept (global).
        foreach (var key in new[]
        {
            "auth_token", "refresh_token",
            "user_name",  "user_id",  "user_role", "tenant_name", "tenant_subdomain",
            "default_branch_uuid",  "default_cashbox_uuid",
            "default_currency_id",  "default_price_list_id",
            "last_sync_at"
        })
            settings.Remove(key);
    }
}
