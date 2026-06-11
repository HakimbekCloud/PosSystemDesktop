using System.Net.Http;
using System.Net.Http.Headers;
using PosSystem.Data.Repositories;

namespace PosSystem.Services;

// Per-request header stamping (merge port of Bug H1).
//
// Before: ApiClient mutated HttpClient.DefaultRequestHeaders via
// ApplyAuthToken/ApplyTenantHeader. That coupled correctness to the *ordering*
// of those mutating calls and could leak a stale Bearer/tenant across logout,
// token refresh and tenant switches.
//
// Now: every outgoing request is stamped here, reading the live values from
// the settings store on each send, so there is no shared mutable header state:
//
//   • X-Tenant-ID  — stamped from the current tenant_subdomain UNLESS the
//                    request already set one on purpose (the logout revocation
//                    stamps the tenant captured BEFORE the local clear).
//   • Authorization — Bearer auth_token (DPAPI-decrypted) is added ONLY when the
//                    request does not already carry an Authorization header.
//                    This preserves requests that set their own token on purpose:
//                    the refresh call (Bearer = refresh token) and the logout
//                    call (explicit access token read BEFORE local clear), so
//                    their ordering guarantees are honoured. The login call sets
//                    no token and, since auth_token is cleared at that point,
//                    correctly goes out with the tenant header and no Bearer.
public sealed class TenantAuthHeaderHandler(SettingsRepository settings)
    : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var hasExplicitTenant = request.Headers.Contains("X-Tenant-ID");
        var hasExplicitAuth   = request.Headers.Authorization is not null;

        // L3: a tenant-DB switch is in progress. The path provider may be
        // half-flipped and the settings store is being re-pointed, so any request
        // that READS tenant/auth here could pick up an inconsistent identity and
        // race the switch. A fully self-contained request (explicit tenant AND
        // auth already stamped by the caller — e.g. the logout revocation) reads
        // nothing from settings and may proceed. Background sync is already
        // drained + gated by TenantScopeService; this fails the remaining
        // settings-dependent requests (shift probe, operator clients) fast and
        // clearly instead of letting them fly. The window is sub-second.
        if (TenantScopeService.IsSwitchInProgress &&
            !(hasExplicitTenant && hasExplicitAuth))
            throw new InvalidOperationException(
                "Tenant almashtirilmoqda — so'rov bekor qilindi. Birozdan so'ng qayta urinib ko'ring.");

        // Tenant header: stamp fresh from settings unless the caller set it.
        if (!hasExplicitTenant)
        {
            var tenant = settings.Get("tenant_subdomain");
            if (!string.IsNullOrEmpty(tenant))
                request.Headers.TryAddWithoutValidation("X-Tenant-ID", tenant);
        }

        // Auth header: only add if the request did not already set one.
        if (!hasExplicitAuth)
        {
            var token = settings.GetDecrypted("auth_token");
            if (!string.IsNullOrEmpty(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, ct);
    }
}
