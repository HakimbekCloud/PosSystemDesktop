using System;
using System.Text.Json.Serialization;

namespace PosSystem.Core.DTOs;

// ── Phase 10.20I — Desktop request/response DTOs for the backend
// Operator Permission Admin Mutation API (Phase 10.20H).
//
// Read-write but guarded: the desktop only calls these via the
// OperatorPermissionAdminMutationApiClient wrapper, which is itself
// gated by the local feature flag
// `operator_permission_admin_mutation_ui_enabled`.
//
// Security:
//   • No ConfirmationPhrase / Phrase field. Phrases are local-only.
//   • No AccessToken / RefreshToken / Authorization / Password / JWT
//     field on any DTO.
//   • No raw file content, no DB content, no raw logs.

public sealed class OperatorPermissionUserOverrideCreateRequestDto
{
    [JsonPropertyName("userId")]           public long?     UserId { get; set; }
    [JsonPropertyName("tenantId")]         public string?   TenantId { get; set; }
    [JsonPropertyName("storeId")]          public string?   StoreId { get; set; }
    [JsonPropertyName("permissionKey")]    public string?   PermissionKey { get; set; }
    [JsonPropertyName("grantType")]        public string?   GrantType { get; set; }
    [JsonPropertyName("expiresAt")]        public DateTime? ExpiresAt { get; set; }
    [JsonPropertyName("reason")]           public string?   Reason { get; set; }
    [JsonPropertyName("approvalTicketId")] public string?   ApprovalTicketId { get; set; }
    [JsonPropertyName("requestId")]        public string?   RequestId { get; set; }
}

public sealed class OperatorPermissionUserOverrideRevokeRequestDto
{
    [JsonPropertyName("reason")]           public string? Reason { get; set; }
    [JsonPropertyName("approvalTicketId")] public string? ApprovalTicketId { get; set; }
    [JsonPropertyName("requestId")]        public string? RequestId { get; set; }
}

public sealed class OperatorPermissionRoleGrantCreateRequestDto
{
    [JsonPropertyName("role")]              public string? Role { get; set; }
    [JsonPropertyName("permissionKey")]     public string? PermissionKey { get; set; }
    [JsonPropertyName("tenantScopePolicy")] public string? TenantScopePolicy { get; set; }
    [JsonPropertyName("reason")]            public string? Reason { get; set; }
    [JsonPropertyName("approvalTicketId")]  public string? ApprovalTicketId { get; set; }
    [JsonPropertyName("requestId")]         public string? RequestId { get; set; }
}

public sealed class OperatorPermissionRoleGrantRevokeRequestDto
{
    [JsonPropertyName("reason")]           public string? Reason { get; set; }
    [JsonPropertyName("approvalTicketId")] public string? ApprovalTicketId { get; set; }
    [JsonPropertyName("requestId")]        public string? RequestId { get; set; }
}

// Generic envelope. On success ({@code Success=true}) the {@code Item}
// is the same admin DTO returned by the Phase 10.20F read-only API.
// On failure the wrapper sets {@code Success=false} and uses
// {@code Message} to surface the parsed backend error body or a
// transport failure summary.
public sealed class OperatorPermissionAdminMutationResponseDto<T>
{
    [JsonPropertyName("success")]     public bool    Success { get; set; }
    [JsonPropertyName("message")]     public string? Message { get; set; }
    [JsonPropertyName("auditSource")] public string? AuditSource { get; set; }
    [JsonPropertyName("item")]        public T?      Item { get; set; }

    // Desktop-only convenience fields (never sent to the backend).
    [JsonIgnore] public int     BackendStatusCode { get; set; }
    [JsonIgnore] public string? BackendErrorCode  { get; set; }
}

// Parsed shape of the backend's typed error body
// {status, code, message}. Wrapper uses this to populate
// OperatorPermissionAdminMutationResponseDto.Message on failure.
internal sealed class OperatorPermissionAdminMutationErrorBodyDto
{
    [JsonPropertyName("status")]  public int     Status { get; set; }
    [JsonPropertyName("code")]    public string? Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}
