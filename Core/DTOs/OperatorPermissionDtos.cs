using System.Text.Json.Serialization;

namespace PosSystem.Core.DTOs;

// ── Phase 10.19C — Desktop DTOs for the backend operator permission API ───
//
// These DTOs mirror the backend response shapes from
// Ham-Pos/src/main/java/com/example/hampos/security/dto/operator/*.java
// (Phase 10.19B). The desktop only displays this information in the
// Migration Operations dashboard — it does NOT enforce backend permissions
// in Phase 10.19C. Local flags, OperatorAccessService role checks, and
// the guarded wrapper services remain the authoritative gates.
//
// None of these DTOs carry an access token, refresh token, password, or
// confirmation phrase. The backend never returns those over these
// endpoints; the desktop never sends a phrase to the backend either.

public sealed class OperatorIdentityDto
{
    [JsonPropertyName("userId")]
    public long? UserId { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("tenantId")]
    public string? TenantId { get; set; }

    [JsonPropertyName("tenantName")]
    public string? TenantName { get; set; }

    [JsonPropertyName("storeId")]
    public string? StoreId { get; set; }

    [JsonPropertyName("storeName")]
    public string? StoreName { get; set; }

    [JsonPropertyName("authenticated")]
    public bool Authenticated { get; set; }

    [JsonPropertyName("permissionsSource")]
    public string? PermissionsSource { get; set; }

    [JsonPropertyName("generatedAt")]
    public DateTime? GeneratedAt { get; set; }
}

public sealed class OperatorPermissionsDto
{
    [JsonPropertyName("userId")]
    public long? UserId { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("tenantId")]
    public string? TenantId { get; set; }

    [JsonPropertyName("storeId")]
    public string? StoreId { get; set; }

    [JsonPropertyName("permissions")]
    public List<string> Permissions { get; set; } = new();

    [JsonPropertyName("dangerousPermissions")]
    public List<string> DangerousPermissions { get; set; } = new();

    [JsonPropertyName("readOnlyPermissions")]
    public List<string> ReadOnlyPermissions { get; set; } = new();

    [JsonPropertyName("expiresAt")]
    public DateTime? ExpiresAt { get; set; }

    [JsonPropertyName("generatedAt")]
    public DateTime? GeneratedAt { get; set; }
}

public sealed class OperatorPermissionValidateRequestDto
{
    [JsonPropertyName("permissionKey")]
    public string PermissionKey { get; set; } = "";

    [JsonPropertyName("tenantId")]
    public string? TenantId { get; set; }

    [JsonPropertyName("storeId")]
    public string? StoreId { get; set; }

    [JsonPropertyName("operationName")]
    public string? OperationName { get; set; }

    [JsonPropertyName("approvalTicketId")]
    public string? ApprovalTicketId { get; set; }

    // Intentionally NO ConfirmationPhrase field. The desktop never sends a
    // confirmation phrase to the backend; phrases are local-only and are
    // consumed only by the guarded wrapper services.
}

public sealed class OperatorPermissionValidateResultDto
{
    [JsonPropertyName("allowed")]
    public bool Allowed { get; set; }

    [JsonPropertyName("permissionKey")]
    public string? PermissionKey { get; set; }

    [JsonPropertyName("operationName")]
    public string? OperationName { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("tenantId")]
    public string? TenantId { get; set; }

    [JsonPropertyName("storeId")]
    public string? StoreId { get; set; }

    [JsonPropertyName("requiresLocalFlag")]
    public bool RequiresLocalFlag { get; set; }

    [JsonPropertyName("requiresConfirmationPhrase")]
    public bool RequiresConfirmationPhrase { get; set; }

    [JsonPropertyName("requiresGuardedWrapper")]
    public bool RequiresGuardedWrapper { get; set; }

    [JsonPropertyName("validatedAt")]
    public DateTime? ValidatedAt { get; set; }
}
