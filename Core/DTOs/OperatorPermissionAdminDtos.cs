using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PosSystem.Core.DTOs;

// ── Phase 10.20G — Desktop DTOs for the read-only operator permission
// admin API (backend Phase 10.20F).
//
// Read-only. The desktop UI applies an additional redaction layer
// before display so accidental upstream leaks cannot reach the screen.

public sealed class OperatorPermissionDefinitionAdminDto
{
    [JsonPropertyName("id")]                         public long?     Id { get; set; }
    [JsonPropertyName("permissionKey")]              public string?   PermissionKey { get; set; }
    [JsonPropertyName("description")]                public string?   Description { get; set; }
    [JsonPropertyName("category")]                   public string?   Category { get; set; }
    [JsonPropertyName("dangerous")]                  public bool      Dangerous { get; set; }
    [JsonPropertyName("requiresLocalFlag")]          public bool      RequiresLocalFlag { get; set; }
    [JsonPropertyName("requiresConfirmationPhrase")] public bool      RequiresConfirmationPhrase { get; set; }
    [JsonPropertyName("requiresGuardedWrapper")]     public bool      RequiresGuardedWrapper { get; set; }
    [JsonPropertyName("active")]                     public bool      Active { get; set; }
    [JsonPropertyName("createdAt")]                  public DateTime? CreatedAt { get; set; }
    [JsonPropertyName("updatedAt")]                  public DateTime? UpdatedAt { get; set; }
}

public sealed class OperatorRolePermissionGrantAdminDto
{
    [JsonPropertyName("id")]                public long?     Id { get; set; }
    [JsonPropertyName("role")]              public string?   Role { get; set; }
    [JsonPropertyName("permissionKey")]     public string?   PermissionKey { get; set; }
    [JsonPropertyName("tenantScopePolicy")] public string?   TenantScopePolicy { get; set; }
    [JsonPropertyName("active")]            public bool      Active { get; set; }
    [JsonPropertyName("createdBy")]         public long?     CreatedBy { get; set; }
    [JsonPropertyName("createdAt")]         public DateTime? CreatedAt { get; set; }
    [JsonPropertyName("revokedBy")]         public long?     RevokedBy { get; set; }
    [JsonPropertyName("revokedAt")]         public DateTime? RevokedAt { get; set; }
}

public sealed class OperatorUserPermissionOverrideAdminDto
{
    [JsonPropertyName("id")]               public long?     Id { get; set; }
    [JsonPropertyName("userId")]           public long?     UserId { get; set; }
    [JsonPropertyName("tenantId")]         public string?   TenantId { get; set; }
    [JsonPropertyName("storeId")]          public string?   StoreId { get; set; }
    [JsonPropertyName("permissionKey")]    public string?   PermissionKey { get; set; }
    [JsonPropertyName("grantType")]        public string?   GrantType { get; set; }
    [JsonPropertyName("expiresAt")]        public DateTime? ExpiresAt { get; set; }
    [JsonPropertyName("reason")]           public string?   Reason { get; set; }
    [JsonPropertyName("approvalTicketId")] public string?   ApprovalTicketId { get; set; }
    [JsonPropertyName("active")]           public bool      Active { get; set; }
    [JsonPropertyName("createdBy")]        public long?     CreatedBy { get; set; }
    [JsonPropertyName("createdAt")]        public DateTime? CreatedAt { get; set; }
    [JsonPropertyName("revokedBy")]        public long?     RevokedBy { get; set; }
    [JsonPropertyName("revokedAt")]        public DateTime? RevokedAt { get; set; }
}

public sealed class OperatorPermissionAdminPageDto<T>
{
    [JsonPropertyName("items")]         public List<T> Items { get; set; } = new();
    [JsonPropertyName("page")]          public int     Page { get; set; }
    [JsonPropertyName("size")]          public int     Size { get; set; }
    [JsonPropertyName("totalElements")] public long    TotalElements { get; set; }
    [JsonPropertyName("hasNext")]       public bool    HasNext { get; set; }
}

public sealed class OperatorPermissionEffectiveAdminDto
{
    [JsonPropertyName("targetUserId")]    public long?   TargetUserId { get; set; }
    [JsonPropertyName("targetTenantId")]  public string? TargetTenantId { get; set; }
    [JsonPropertyName("targetStoreId")]   public string? TargetStoreId { get; set; }
    [JsonPropertyName("auditSource")]     public string? AuditSource { get; set; }
    [JsonPropertyName("effectiveResult")] public OperatorPermissionEffectiveResultDto? EffectiveResult { get; set; }
}

// ── Client-side filter bundles. Not sent as JSON; serialized into the
// URL query string by ApiClient.

public sealed class OperatorPermissionDefinitionAdminQuery
{
    public bool?   Active { get; set; }
    public string? Category { get; set; }
    public bool?   Dangerous { get; set; }
    public string? PermissionKey { get; set; }
    public int     Page { get; set; }
    public int     Size { get; set; } = 50;
}

public sealed class OperatorRoleGrantAdminQuery
{
    public string? Role { get; set; }
    public string? PermissionKey { get; set; }
    public string? TenantScopePolicy { get; set; }
    public bool?   Active { get; set; }
    public int     Page { get; set; }
    public int     Size { get; set; } = 50;
}

public sealed class OperatorUserOverrideAdminQuery
{
    public long?   UserId { get; set; }
    public string? TenantId { get; set; }
    public string? StoreId { get; set; }
    public string? PermissionKey { get; set; }
    public string? GrantType { get; set; }
    public bool?   Active { get; set; }
    public bool?   Expired { get; set; }
    public int     Page { get; set; }
    public int     Size { get; set; } = 50;
}

public sealed class OperatorPermissionEffectiveAdminQuery
{
    public long?   UserId { get; set; }
    public string? TenantId { get; set; }
    public string? StoreId { get; set; }
}
