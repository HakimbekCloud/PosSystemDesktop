using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using PosSystem.Data;
using PosSystem.Data.Repositories;

namespace PosSystem.Services;

public class AuthService(
    ApiClient api,
    SettingsRepository settings,
    SaleRepository sales,
    IDbContextFactory<AppDbContext> dbFactory)
{
    public bool HasValidSession() =>
        !string.IsNullOrEmpty(settings.Get("auth_token"));

    public string? GetCurrentUserName() =>
        settings.Get("user_name");

    public long GetCurrentUserId() =>
        long.TryParse(settings.Get("user_id"), out var id) ? id : 0;

    public string GetLastTenantSubdomain() =>
        settings.Get("tenant_subdomain") ?? "";

    public string GetSavedServerUrl() =>
        settings.Get("api_base_url") ?? "";

    // Bug C2: the tenant whose business data currently lives in the local DB.
    // Migration from older builds: if `data_tenant` is absent but tenant_subdomain
    // is present (data already belongs to that tenant), treat it as the implicit
    // owner so we never wrongly wipe real data on the first post-update login.
    private string EffectiveDataTenant()
    {
        var dataTenant = settings.Get("data_tenant");
        if (!string.IsNullOrEmpty(dataTenant)) return dataTenant;
        return settings.Get("tenant_subdomain") ?? "";
    }

    // Bug C2: BEFORE login the ViewModel asks how many unsynced sales would be lost
    // if we switch to `newTenant`. Returns 0 when the entered tenant matches the
    // local data's owner (or no owner recorded yet) — switching is what wipes, not
    // re-login to the same tenant.
    public int GetUnsyncedSalesCountForOtherTenant(string newTenant)
    {
        var owner = EffectiveDataTenant();
        if (string.IsNullOrEmpty(owner)) return 0;          // first-ever login
        if (string.Equals(owner, (newTenant ?? "").Trim(),
                StringComparison.OrdinalIgnoreCase))
            return 0;                                        // same tenant → no wipe
        return sales.GetPendingCount();
    }

    public async Task<(bool Success, string Message)> LoginAsync(
        string tenantSubdomain, string username, string password,
        string? serverUrl = null)
    {
        if (string.IsNullOrWhiteSpace(tenantSubdomain))
            return (false, "Tashkilot kodini kiriting");

        var newTenant = tenantSubdomain.Trim();

        // Migration from older builds: data_tenant was never recorded, so the
        // pre-overwrite tenant_subdomain owns whatever data exists locally. Record
        // it NOW — before tenant_subdomain is overwritten below — otherwise a
        // failed login attempt to another tenant would corrupt the ownership check.
        if (string.IsNullOrEmpty(settings.Get("data_tenant")))
        {
            var legacyOwner = settings.Get("tenant_subdomain");
            if (!string.IsNullOrEmpty(legacyOwner))
                settings.Set("data_tenant", legacyOwner);
        }

        // If the user typed a server URL, persist it; otherwise keep the saved one.
        if (!string.IsNullOrWhiteSpace(serverUrl))
            settings.Set("api_base_url", serverUrl.Trim());

        // Per-request headers (Bug H1) read tenant from settings at send time;
        // BaseAddress only changes pre-login, which is safe.
        settings.Set("tenant_subdomain", newTenant);
        api.ApplyBaseUrl();

        try
        {
            var response = await api.LoginAsync(username, password);

            // Bug C2: business data belongs to a tenant. If the new tenant differs
            // from the recorded owner, the local data is another tenant's and must
            // be wiped (the ViewModel has already confirmed any unsynced loss).
            var owner = EffectiveDataTenant();
            if (!string.IsNullOrEmpty(owner) &&
                !string.Equals(owner, newTenant, StringComparison.OrdinalIgnoreCase))
                WipeTenantData();

            settings.Set("auth_token",    response.AccessToken);
            settings.Set("refresh_token", response.RefreshToken);
            settings.Set("user_name",     response.User.Username);
            settings.Set("user_id",       response.User.Id.ToString());
            if (response.Tenant is not null)
                settings.Set("tenant_name", response.Tenant.Name);

            // Record this tenant as the owner of the local business data.
            settings.Set("data_tenant", newTenant);

            // Bug M3: a fresh login re-arms the session-expiry debounce.
            api.ResetSessionExpiry();
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

    // Bug C2: logout ONLY clears auth/session keys. It NEVER deletes business data
    // and NEVER removes tenant identity (kept for login prefill) or tenant-scoped
    // defaults (they belong to the data, which stays). With this, the auto session-
    // expiry logout in MainWindow can no longer cause revenue loss.
    public void Logout()
    {
        foreach (var key in new[] { "auth_token", "refresh_token", "user_name", "user_id" })
            settings.Remove(key);
        // Headers are per-request (Bug H1): clearing auth_token is enough — the next
        // request automatically omits the Bearer token. Nothing to re-apply.
    }

    // Bug C2: wipe the previous tenant's business data and tenant-scoped defaults
    // when a DIFFERENT tenant logs in. Tenant identity (tenant_subdomain/name) is
    // overwritten by the new login; machine-level settings (api_base_url, tablet_mode,
    // ui_scale) are untouched.
    private void WipeTenantData()
    {
        using var db = dbFactory.CreateDbContext();
        db.Database.ExecuteSqlRaw("DELETE FROM SaleItems");
        db.Database.ExecuteSqlRaw("DELETE FROM Sales");
        db.Database.ExecuteSqlRaw("DELETE FROM Products");
        db.Database.ExecuteSqlRaw("DELETE FROM Customers");
        db.Database.ExecuteSqlRaw("DELETE FROM PriceLists");
        db.Database.ExecuteSqlRaw("DELETE FROM ProductTypes");

        foreach (var key in new[]
        {
            "default_branch_uuid",  "default_cashbox_uuid",
            "default_currency_id",  "default_price_list_id",
            "last_sync_at"
        })
            settings.Remove(key);
    }
}
