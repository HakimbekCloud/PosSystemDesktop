using PosSystem.Core.DTOs;

namespace PosSystem.Services;

// ── Phase 10.19C — Fail-closed wrapper around the operator-permission HTTP API
//
// The existing ApiClient holds the configured HttpClient (base URL, auth
// token, tenant header, refresh-on-401 helper). This wrapper consumes the
// three new ApiClient methods and translates exceptions into a fail-closed
// null result so the dashboard's Refresh button never crashes when the
// backend is offline or rejects the call.
//
// Phase 10.19C is DISPLAY-ONLY. The dashboard shows backend identity +
// permissions for visibility; nothing the wrapper returns affects local
// flag gates, OperatorAccessService role checks, or guarded wrapper
// services. Backend permission enforcement is Phase 10.19D's responsibility.
//
// Security contract:
//   • Never logs the operator's auth token, refresh token, or Authorization
//     header (the underlying HttpClient handler is responsible for that;
//     this wrapper neither inspects nor records header values).
//   • Never sends a confirmation phrase. The validate request DTO has no
//     ConfirmationPhrase field by design.
//   • Never includes desktop DB rows or evidence-bundle contents in any
//     backend request body. Validate requests carry only the permission
//     key, the resolved tenant/store identifiers, the operation name, and
//     an optional approval ticket id.
public sealed class OperatorPermissionApiClient
{
    private readonly ApiClient _api;

    public OperatorPermissionApiClient(ApiClient api)
    {
        _api = api;
    }

    public async Task<OperatorIdentityDto?> GetIdentityAsync(System.Threading.CancellationToken ct = default)
    {
        try
        {
            return await _api.GetOperatorIdentityAsync(ct);
        }
        catch (System.OperationCanceledException) { throw; }
        catch
        {
            // Fail-closed for Phase 10.19C: return null on network failure,
            // 401/403, 5xx, deserialization error. The caller surfaces a
            // user-facing message based on null vs non-null.
            return null;
        }
    }

    public async Task<OperatorPermissionsDto?> GetPermissionsAsync(System.Threading.CancellationToken ct = default)
    {
        try
        {
            return await _api.GetOperatorPermissionsAsync(ct);
        }
        catch (System.OperationCanceledException) { throw; }
        catch
        {
            return null;
        }
    }

    public async Task<OperatorPermissionValidateResultDto?> ValidateAsync(
        OperatorPermissionValidateRequestDto request,
        System.Threading.CancellationToken ct = default)
    {
        if (request is null) throw new System.ArgumentNullException(nameof(request));
        try
        {
            return await _api.ValidateOperatorPermissionAsync(request, ct);
        }
        catch (System.OperationCanceledException) { throw; }
        catch
        {
            return null;
        }
    }
}
