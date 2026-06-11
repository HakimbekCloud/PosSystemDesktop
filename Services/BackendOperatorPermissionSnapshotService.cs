using PosSystem.Core.DTOs;
using PosSystem.Data.Repositories;

namespace PosSystem.Services;

// ── DTOs (Phase 10.19E) ──────────────────────────────────────────────────────

// Per-permission validation snapshot. Records whether the validate call
// was attempted, the verdict, and the metadata booleans the desktop
// requires (local flag + confirmation phrase + guarded wrapper) for
// every dangerous permission key.
public sealed class BackendDangerousPermissionValidationSnapshot
{
    public string         PermissionKey              { get; init; } = "";
    public string         OperationName              { get; init; } = "";
    public bool           Attempted                  { get; init; }
    public bool           Allowed                    { get; init; }
    public string?        Reason                     { get; init; }
    public bool           RequiresLocalFlag          { get; init; }
    public bool           RequiresConfirmationPhrase { get; init; }
    public bool           RequiresGuardedWrapper     { get; init; }
    public System.DateTime? ValidatedAt              { get; init; }
    // "NotAttempted" | "Allowed" | "Denied" | "Unavailable" | "MetadataMismatch"
    public string         Status                     { get; init; } = "NotAttempted";
}

// Top-level read-only snapshot describing the desktop's view of the
// backend operator permission stack. Used by the Production Pilot
// Readiness Report (as Area I) and the Production Pilot Evidence Bundle
// (as `backend-permission-summary.json`).
public sealed class BackendOperatorPermissionSnapshot
{
    public System.DateTime GeneratedAtUtc      { get; init; } = System.DateTime.UtcNow;

    public bool   EnforcementEnabled           { get; init; }
    public string EnforcementFlagValue         { get; init; } = "";

    public bool   IdentityAvailable            { get; init; }
    public bool   PermissionsAvailable         { get; init; }
    public bool   BackendReachable             { get; init; }

    public long?  UserId                       { get; init; }
    public string? Username                    { get; init; }
    public string? Role                        { get; init; }
    public string? TenantId                    { get; init; }
    public string? StoreId                     { get; init; }
    public string? PermissionsSource           { get; init; }
    public System.DateTime? PermissionsGeneratedAt { get; init; }
    public System.DateTime? PermissionsExpiresAt   { get; init; }

    public int    PermissionCount              { get; init; }
    public int    DangerousPermissionCount     { get; init; }
    public int    ReadOnlyPermissionCount      { get; init; }

    public System.Collections.Generic.List<string> Permissions            { get; init; } = new();
    public System.Collections.Generic.List<string> DangerousPermissions   { get; init; } = new();
    public System.Collections.Generic.List<string> ReadOnlyPermissions    { get; init; } = new();

    public System.Collections.Generic.List<string> Warnings { get; init; } = new();
    public System.Collections.Generic.List<string> Errors   { get; init; } = new();

    public BackendDangerousPermissionValidationSnapshot MigrationExecute        { get; init; } = new();
    public BackendDangerousPermissionValidationSnapshot CutoverExecute          { get; init; } = new();
    public BackendDangerousPermissionValidationSnapshot RollbackExecute         { get; init; } = new();
    public BackendDangerousPermissionValidationSnapshot RetentionCleanupExecute { get; init; } = new();
}

// ── Service ──────────────────────────────────────────────────────────────────

// Strictly read-only aggregator that produces a sanitized snapshot of the
// backend operator permission state. Phase 10.19E.
//
// Consumed by:
//   • ProductionPilotReadinessReportService (Area I — backend permissions)
//   • ProductionPilotEvidenceBundleService (backend-permission-summary.json)
//
// What this service does:
//   • Reads the enforcement flag (operator_backend_permission_enforcement_enabled)
//     via GlobalSettingsRepository.Get — never writes.
//   • Calls OperatorPermissionApiClient for identity + permissions +
//     four dangerous-permission validations. Each call is fail-closed
//     (returns null on backend offline / 401 / 5xx / network failure /
//     invalid JSON).
//   • Composes one BackendOperatorPermissionSnapshot, never throws.
//
// What this service NEVER does:
//   • Invoke a guarded executor (migration / cutover / rollback / cleanup
//     wrappers are not injected).
//   • Invoke the underlying migrator, rollback executor, or
//     TenantScopeService.
//   • Send the operator's confirmation phrase to the backend (the
//     validate request DTO has no phrase field).
//   • Send local DB paths, evidence bundle content, or any other
//     desktop-side sensitive payload.
//   • Log tokens or Authorization headers.
//   • Mutate any setting / DB / file.
//   • Switch the path provider, log out, or restart the app.
public sealed class BackendOperatorPermissionSnapshotService
{
    private const string EnforcementFlagKey = "operator_backend_permission_enforcement_enabled";

    private const string KeyMigrationExecute        = "operator.migration.execute";
    private const string KeyCutoverExecute          = "operator.cutover.execute";
    private const string KeyRollbackExecute         = "operator.rollback.execute";
    private const string KeyRetentionCleanupExecute = "operator.retention.cleanup.execute";

    private const string OpMigrationExecute        = "execute-real-migration";
    private const string OpCutoverExecute          = "execute-runtime-cutover";
    private const string OpRollbackExecute         = "execute-rollback";
    private const string OpRetentionCleanupExecute = "execute-retention-cleanup";

    private readonly OperatorPermissionApiClient _api;
    private readonly GlobalSettingsRepository    _global;

    public BackendOperatorPermissionSnapshotService(
        OperatorPermissionApiClient api,
        GlobalSettingsRepository global)
    {
        _api    = api;
        _global = global;
    }

    public async System.Threading.Tasks.Task<BackendOperatorPermissionSnapshot> GenerateAsync(
        string? tenantSubdomain,
        System.Threading.CancellationToken ct = default)
    {
        var warnings = new System.Collections.Generic.List<string>();
        var errors   = new System.Collections.Generic.List<string>();

        var enforcementFlagValue = _global.Get(EnforcementFlagKey) ?? "";
        var enforcementEnabled   = enforcementFlagValue == "1";

        // Resolve tenant: explicit arg → last-used → null. Read-only.
        var resolvedTenant = string.IsNullOrWhiteSpace(tenantSubdomain)
            ? _global.Get("last_tenant_subdomain")
            : tenantSubdomain.Trim();

        OperatorIdentityDto?    identity    = null;
        OperatorPermissionsDto? permissions = null;

        try { identity = await _api.GetIdentityAsync(ct); }
        catch (System.OperationCanceledException) { throw; }
        catch (System.Exception ex) { errors.Add($"Identity fetch threw: {ex.Message}"); }

        try { permissions = await _api.GetPermissionsAsync(ct); }
        catch (System.OperationCanceledException) { throw; }
        catch (System.Exception ex) { errors.Add($"Permissions fetch threw: {ex.Message}"); }

        var identityAvailable    = identity is not null;
        var permissionsAvailable = permissions is not null;
        var backendReachable     = identityAvailable || permissionsAvailable;

        if (!identityAvailable)
            (enforcementEnabled ? errors : warnings).Add(
                "Backend identity unavailable (offline, unauthorized, or backend unreachable).");
        if (!permissionsAvailable)
            (enforcementEnabled ? errors : warnings).Add(
                "Backend permissions unavailable (offline, unauthorized, or backend unreachable).");

        // Always attempt validation for the four dangerous permissions —
        // visibility is useful even when enforcement is off. Failures
        // become blockers only when enforcement is enabled; the caller
        // applies the severity rule.
        var migration       = await ValidateOneAsync(KeyMigrationExecute,        OpMigrationExecute,        resolvedTenant, ct);
        var cutover         = await ValidateOneAsync(KeyCutoverExecute,          OpCutoverExecute,          resolvedTenant, ct);
        var rollback        = await ValidateOneAsync(KeyRollbackExecute,         OpRollbackExecute,         resolvedTenant, ct);
        var retentionClean  = await ValidateOneAsync(KeyRetentionCleanupExecute, OpRetentionCleanupExecute, resolvedTenant, ct);

        // Permission-expiry warning. The current backend (Phase 10.19B) does
        // not populate ExpiresAt; this branch is forward-compatible.
        if (permissions is { ExpiresAt: { } exp } && exp < System.DateTime.UtcNow)
        {
            (enforcementEnabled ? errors : warnings).Add(
                $"Backend permission claim is expired (expiresAt={exp:o}).");
        }

        // CASHIER role surfaces here for completeness. The dashboard itself
        // refuses CASHIER at OperatorAccessService level; this snapshot
        // documents that the backend agrees.
        if (string.Equals(identity?.Role, "CASHIER", System.StringComparison.OrdinalIgnoreCase))
        {
            (enforcementEnabled ? errors : warnings).Add(
                "Backend reports CASHIER role for this user — no operator-maintenance permissions.");
        }

        return new BackendOperatorPermissionSnapshot
        {
            GeneratedAtUtc            = System.DateTime.UtcNow,
            EnforcementEnabled        = enforcementEnabled,
            EnforcementFlagValue      = enforcementFlagValue,
            IdentityAvailable         = identityAvailable,
            PermissionsAvailable      = permissionsAvailable,
            BackendReachable          = backendReachable,
            UserId                    = identity?.UserId,
            Username                  = identity?.Username,
            Role                      = identity?.Role,
            TenantId                  = identity?.TenantId,
            StoreId                   = identity?.StoreId,
            PermissionsSource         = identity?.PermissionsSource,
            PermissionsGeneratedAt    = identity?.GeneratedAt,
            PermissionsExpiresAt      = permissions?.ExpiresAt,
            PermissionCount           = permissions?.Permissions?.Count          ?? 0,
            DangerousPermissionCount  = permissions?.DangerousPermissions?.Count ?? 0,
            ReadOnlyPermissionCount   = permissions?.ReadOnlyPermissions?.Count  ?? 0,
            Permissions               = new System.Collections.Generic.List<string>(
                                            permissions?.Permissions          ?? new()),
            DangerousPermissions      = new System.Collections.Generic.List<string>(
                                            permissions?.DangerousPermissions ?? new()),
            ReadOnlyPermissions       = new System.Collections.Generic.List<string>(
                                            permissions?.ReadOnlyPermissions  ?? new()),
            Warnings                  = warnings,
            Errors                    = errors,
            MigrationExecute          = migration,
            CutoverExecute            = cutover,
            RollbackExecute           = rollback,
            RetentionCleanupExecute   = retentionClean,
        };
    }

    // Calls validate once for a single permission key. Never sends the
    // operator's confirmation phrase — the request DTO has no phrase field
    // by design. Fail-closed: returns a NotAttempted/Unavailable snapshot
    // when the backend is unreachable or returns malformed data.
    private async System.Threading.Tasks.Task<BackendDangerousPermissionValidationSnapshot> ValidateOneAsync(
        string permissionKey,
        string operationName,
        string? tenantId,
        System.Threading.CancellationToken ct)
    {
        OperatorPermissionValidateResultDto? result;
        try
        {
            result = await _api.ValidateAsync(
                new OperatorPermissionValidateRequestDto
                {
                    PermissionKey    = permissionKey,
                    TenantId         = string.IsNullOrWhiteSpace(tenantId) ? null : tenantId,
                    StoreId          = null,
                    OperationName    = operationName,
                    ApprovalTicketId = null,
                    // No ConfirmationPhrase field exists on the DTO.
                },
                ct);
        }
        catch (System.OperationCanceledException) { throw; }
        catch
        {
            result = null;
        }

        if (result is null)
        {
            return new BackendDangerousPermissionValidationSnapshot
            {
                PermissionKey = permissionKey,
                OperationName = operationName,
                Attempted     = true,
                Allowed       = false,
                Reason        = "Backend validation unavailable.",
                Status        = "Unavailable",
            };
        }

        var status = result.Allowed
            ? (result.RequiresLocalFlag &&
               result.RequiresConfirmationPhrase &&
               result.RequiresGuardedWrapper
                    ? "Allowed"
                    : "MetadataMismatch")
            : "Denied";

        return new BackendDangerousPermissionValidationSnapshot
        {
            PermissionKey              = permissionKey,
            OperationName              = operationName,
            Attempted                  = true,
            Allowed                    = result.Allowed && status == "Allowed",
            Reason                     = result.Reason,
            RequiresLocalFlag          = result.RequiresLocalFlag,
            RequiresConfirmationPhrase = result.RequiresConfirmationPhrase,
            RequiresGuardedWrapper     = result.RequiresGuardedWrapper,
            ValidatedAt                = result.ValidatedAt,
            Status                     = status,
        };
    }
}
