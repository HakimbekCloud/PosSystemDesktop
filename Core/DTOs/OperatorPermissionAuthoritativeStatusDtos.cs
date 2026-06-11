using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PosSystem.Core.DTOs;

// ── Phase 10.21G — Desktop DTOs for the read-only operator permission
// authoritative-status endpoint (backend Phase 10.21G).
//
// Read-only. Aggregates the current state of every Phase 10.21
// authoritative-mode flag plus the latest readiness signal from the
// parity gate (Phase 10.21B) and dangerous preflight summary
// (Phase 10.21E). The desktop UI applies an additional redaction
// layer before display so accidental upstream leaks cannot reach
// the screen.
//
// NO field carries a token, password, JWT, Authorization header
// value, or confirmation phrase. The backend scrubs every string
// before it reaches the wire; the desktop redacts again on display.

public sealed class OperatorPermissionAuthoritativeStatusDto
{
    [JsonPropertyName("generatedAt")]                     public DateTime? GeneratedAt { get; set; }
    [JsonPropertyName("permissionsSource")]               public string?   PermissionsSource { get; set; }

    [JsonPropertyName("readOnlyAuthoritativeEnabled")]    public bool      ReadOnlyAuthoritativeEnabled { get; set; }
    [JsonPropertyName("dangerousPreflightEnabled")]       public bool      DangerousPreflightEnabled { get; set; }
    [JsonPropertyName("dangerousAuthoritativeEnabled")]   public bool      DangerousAuthoritativeEnabled { get; set; }
    [JsonPropertyName("failOnMismatch")]                  public bool      FailOnMismatch { get; set; }
    [JsonPropertyName("allowCodeFallbackReadOnly")]       public bool      AllowCodeFallbackReadOnly { get; set; }

    [JsonPropertyName("readyForReadOnlyAuthoritative")]   public bool      ReadyForReadOnlyAuthoritative { get; set; }
    [JsonPropertyName("readyForDangerousAuthoritative")]  public bool      ReadyForDangerousAuthoritative { get; set; }
    [JsonPropertyName("parityHealthy")]                   public bool      ParityHealthy { get; set; }
    [JsonPropertyName("dangerousPreflightHealthy")]       public bool      DangerousPreflightHealthy { get; set; }

    [JsonPropertyName("blockerCount")]                    public long      BlockerCount { get; set; }
    [JsonPropertyName("warningCount")]                    public long      WarningCount { get; set; }
    [JsonPropertyName("infoCount")]                       public long      InfoCount { get; set; }

    [JsonPropertyName("flags")]                           public List<OperatorPermissionAuthoritativeFlagStatusDto>? Flags { get; set; }
    [JsonPropertyName("readiness")]                       public OperatorPermissionAuthoritativeReadinessSummaryDto? Readiness { get; set; }
    [JsonPropertyName("risks")]                           public OperatorPermissionAuthoritativeRiskSummaryDto?      Risks { get; set; }
    [JsonPropertyName("issues")]                          public List<OperatorPermissionAuthoritativeStatusIssueDto>? Issues { get; set; }
    [JsonPropertyName("errors")]                          public List<string>? Errors { get; set; }
}

public sealed class OperatorPermissionAuthoritativeFlagStatusDto
{
    [JsonPropertyName("flagName")]                     public string? FlagName { get; set; }
    [JsonPropertyName("scope")]                        public string? Scope { get; set; }
    [JsonPropertyName("currentValue")]                 public bool?   CurrentValue { get; set; }
    [JsonPropertyName("defaultValue")]                 public bool    DefaultValue { get; set; }
    [JsonPropertyName("owner")]                        public string? Owner { get; set; }
    [JsonPropertyName("risk")]                         public string? Risk { get; set; }
    [JsonPropertyName("recommendedProductionState")]   public string? RecommendedProductionState { get; set; }
    [JsonPropertyName("notes")]                        public string? Notes { get; set; }
}

public sealed class OperatorPermissionAuthoritativeReadinessSummaryDto
{
    [JsonPropertyName("parityHealthy")]                  public bool ParityHealthy { get; set; }
    [JsonPropertyName("readyForReadOnlyAuthoritative")]  public bool ReadyForReadOnlyAuthoritative { get; set; }
    [JsonPropertyName("readyForDangerousAuthoritative")] public bool ReadyForDangerousAuthoritative { get; set; }
    [JsonPropertyName("dangerousPreflightHealthy")]      public bool DangerousPreflightHealthy { get; set; }
    [JsonPropertyName("dangerousPreflightEvaluated")]    public bool DangerousPreflightEvaluated { get; set; }
    [JsonPropertyName("parityEvaluated")]                public bool ParityEvaluated { get; set; }
}

public sealed class OperatorPermissionAuthoritativeRiskSummaryDto
{
    [JsonPropertyName("dangerousAuthoritativeWithoutPreflight")]                public bool DangerousAuthoritativeWithoutPreflight { get; set; }
    [JsonPropertyName("dangerousAuthoritativeWithUnhealthyPreflight")]          public bool DangerousAuthoritativeWithUnhealthyPreflight { get; set; }
    [JsonPropertyName("readOnlyAuthoritativeWithUnhealthyParity")]              public bool ReadOnlyAuthoritativeWithUnhealthyParity { get; set; }
    [JsonPropertyName("codeFallbackReadOnlyWhileAuthoritative")]                public bool CodeFallbackReadOnlyWhileAuthoritative { get; set; }
    [JsonPropertyName("failOnMismatchDisabledWhileAuthoritative")]              public bool FailOnMismatchDisabledWhileAuthoritative { get; set; }
    [JsonPropertyName("dangerousPreflightDisabledWhileReadOnlyAuthoritative")]  public bool DangerousPreflightDisabledWhileReadOnlyAuthoritative { get; set; }
}

public sealed class OperatorPermissionAuthoritativeStatusIssueDto
{
    [JsonPropertyName("severity")]       public string? Severity { get; set; }
    [JsonPropertyName("category")]       public string? Category { get; set; }
    [JsonPropertyName("flagName")]       public string? FlagName { get; set; }
    [JsonPropertyName("permissionKey")]  public string? PermissionKey { get; set; }
    [JsonPropertyName("message")]        public string? Message { get; set; }
    [JsonPropertyName("recommendation")] public string? Recommendation { get; set; }
}
