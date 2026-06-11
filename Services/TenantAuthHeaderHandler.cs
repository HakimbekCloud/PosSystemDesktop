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
//   • X-Tenant-ID  — always replaced with the current tenant_subdomain
//                    (removed entirely when no tenant is set, e.g. logged out).
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
        // Tenant header: stamp fresh from settings every time.
        request.Headers.Remove("X-Tenant-ID");
        var tenant = settings.Get("tenant_subdomain");
        if (!string.IsNullOrEmpty(tenant))
            request.Headers.TryAddWithoutValidation("X-Tenant-ID", tenant);

        // Auth header: only add if the request did not already set one.
        if (request.Headers.Authorization is null)
        {
            var token = settings.GetDecrypted("auth_token");
            if (!string.IsNullOrEmpty(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, ct);
    }
}
