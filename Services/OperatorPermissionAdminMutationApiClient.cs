using System;
using System.Threading;
using System.Threading.Tasks;

using PosSystem.Core.DTOs;

namespace PosSystem.Services;

// ── Phase 10.20I — Wrapper around the Phase 10.20H mutation
// endpoints. Mirrors the Phase 10.20G read-only wrapper but for the
// four `POST` mutation paths.
//
// Behaviour:
//   • cancellation rethrown
//   • all other exceptions (HTTP transport failure, JSON failure)
//     collapsed into a synthetic failure envelope so the UI renders
//     a clean error string instead of crashing
//   • never logs the request body (the underlying handler treats
//     `reason` / `approvalTicketId` as opaque)
//   • never logs tokens / Authorization headers
//
// The wrapper is registered in DI as a singleton; the
// ViewModel calls it only when the local feature flag
// `operator_permission_admin_mutation_ui_enabled` is "1".
public sealed class OperatorPermissionAdminMutationApiClient
{
    private readonly ApiClient _api;

    public OperatorPermissionAdminMutationApiClient(ApiClient api)
    {
        _api = api;
    }

    public Task<OperatorPermissionAdminMutationResponseDto<OperatorUserPermissionOverrideAdminDto>?>
        CreateUserOverrideAsync(OperatorPermissionUserOverrideCreateRequestDto request, CancellationToken ct = default)
        => SafeCallAsync(() => _api.CreateOperatorUserOverrideAsync(request, ct));

    public Task<OperatorPermissionAdminMutationResponseDto<OperatorUserPermissionOverrideAdminDto>?>
        RevokeUserOverrideAsync(long id, OperatorPermissionUserOverrideRevokeRequestDto request, CancellationToken ct = default)
        => SafeCallAsync(() => _api.RevokeOperatorUserOverrideAsync(id, request, ct));

    public Task<OperatorPermissionAdminMutationResponseDto<OperatorRolePermissionGrantAdminDto>?>
        CreateRoleGrantAsync(OperatorPermissionRoleGrantCreateRequestDto request, CancellationToken ct = default)
        => SafeCallAsync(() => _api.CreateOperatorRoleGrantAsync(request, ct));

    public Task<OperatorPermissionAdminMutationResponseDto<OperatorRolePermissionGrantAdminDto>?>
        RevokeRoleGrantAsync(long id, OperatorPermissionRoleGrantRevokeRequestDto request, CancellationToken ct = default)
        => SafeCallAsync(() => _api.RevokeOperatorRoleGrantAsync(id, request, ct));

    private static async Task<OperatorPermissionAdminMutationResponseDto<T>?> SafeCallAsync<T>(
        Func<Task<OperatorPermissionAdminMutationResponseDto<T>?>> call)
        where T : class
    {
        try
        {
            return await call();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new OperatorPermissionAdminMutationResponseDto<T>
            {
                Success           = false,
                Message           = "Backend unreachable or returned an unexpected error: " + ScrubExceptionMessage(ex.Message),
                AuditSource       = null,
                Item              = null,
                BackendStatusCode = 0,
                BackendErrorCode  = null,
            };
        }
    }

    private static string ScrubExceptionMessage(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        // Defence-in-depth — never let a JWT, password, or Authorization
        // value leak into the UI from a transport-failure message.
        var (sanitised, _) = OperatorPermissionAdminRedaction.ScrubAndTruncate(raw);
        return sanitised;
    }
}
