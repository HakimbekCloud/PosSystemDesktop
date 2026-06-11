using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PosSystem.Core.DTOs;

// ── Phase 10.20G — Desktop projections of the Phase 10.20D backend
// effective-shadow records. The desktop Phase 10.20G admin UI consumes
// these via OperatorPermissionAdminApiClient.GetEffectiveAsync(...).
//
// Read-only and diagnostic. The desktop never makes a runtime decision
// based on these values; they are display-only.

public sealed class OperatorEffectivePermissionSourceDto
{
    [JsonPropertyName("sourceType")]        public string?   SourceType { get; set; }
    [JsonPropertyName("role")]              public string?   Role { get; set; }
    [JsonPropertyName("permissionKey")]     public string?   PermissionKey { get; set; }
    [JsonPropertyName("tenantScopePolicy")] public string?   TenantScopePolicy { get; set; }
    [JsonPropertyName("tenantId")]          public string?   TenantId { get; set; }
    [JsonPropertyName("storeId")]           public string?   StoreId { get; set; }
    [JsonPropertyName("grantType")]         public string?   GrantType { get; set; }
    [JsonPropertyName("expiresAt")]         public DateTime? ExpiresAt { get; set; }
    [JsonPropertyName("active")]            public bool?     Active { get; set; }
    [JsonPropertyName("reason")]            public string?   Reason { get; set; }
    [JsonPropertyName("approvalTicketId")]  public string?   ApprovalTicketId { get; set; }
}

public sealed class OperatorEffectivePermissionDecisionDto
{
    [JsonPropertyName("permissionKey")]              public string? PermissionKey { get; set; }
    [JsonPropertyName("allowed")]                    public bool    Allowed { get; set; }
    [JsonPropertyName("dangerous")]                  public bool    Dangerous { get; set; }
    [JsonPropertyName("requiresLocalFlag")]          public bool    RequiresLocalFlag { get; set; }
    [JsonPropertyName("requiresConfirmationPhrase")] public bool    RequiresConfirmationPhrase { get; set; }
    [JsonPropertyName("requiresGuardedWrapper")]     public bool    RequiresGuardedWrapper { get; set; }
    [JsonPropertyName("decisionSource")]             public string? DecisionSource { get; set; }
    [JsonPropertyName("reason")]                     public string? Reason { get; set; }
    [JsonPropertyName("sources")]                    public List<OperatorEffectivePermissionSourceDto> Sources { get; set; } = new();
    [JsonPropertyName("warnings")]                   public List<string> Warnings { get; set; } = new();
}

public sealed class OperatorEffectivePermissionComparisonDto
{
    [JsonPropertyName("codePermissions")]        public List<string> CodePermissions { get; set; } = new();
    [JsonPropertyName("dbEffectivePermissions")] public List<string> DbEffectivePermissions { get; set; } = new();
    [JsonPropertyName("missingInDb")]            public List<string> MissingInDb { get; set; } = new();
    [JsonPropertyName("extraInDb")]              public List<string> ExtraInDb { get; set; } = new();
    [JsonPropertyName("matchesCode")]            public bool         MatchesCode { get; set; }
}

public sealed class OperatorPermissionEffectiveResultDto
{
    [JsonPropertyName("enabled")]              public bool          Enabled { get; set; }
    [JsonPropertyName("healthy")]              public bool          Healthy { get; set; }
    [JsonPropertyName("permissionsSource")]    public string?       PermissionsSource { get; set; }
    [JsonPropertyName("generatedAt")]          public DateTime?     GeneratedAt { get; set; }
    [JsonPropertyName("userId")]               public long?         UserId { get; set; }
    [JsonPropertyName("username")]             public string?       Username { get; set; }
    [JsonPropertyName("role")]                 public string?       Role { get; set; }
    [JsonPropertyName("tenantId")]             public string?       TenantId { get; set; }
    [JsonPropertyName("storeId")]              public string?       StoreId { get; set; }
    [JsonPropertyName("definitionCount")]      public long          DefinitionCount { get; set; }
    [JsonPropertyName("roleGrantCount")]       public long          RoleGrantCount { get; set; }
    [JsonPropertyName("overrideCount")]        public long          OverrideCount { get; set; }
    [JsonPropertyName("effectivePermissions")] public List<string>  EffectivePermissions { get; set; } = new();
    [JsonPropertyName("decisions")]            public List<OperatorEffectivePermissionDecisionDto> Decisions { get; set; } = new();
    [JsonPropertyName("comparison")]           public OperatorEffectivePermissionComparisonDto? Comparison { get; set; }
    [JsonPropertyName("warnings")]             public List<string>  Warnings { get; set; } = new();
    [JsonPropertyName("errors")]               public List<string>  Errors { get; set; } = new();
}
