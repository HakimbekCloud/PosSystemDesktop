using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PosSystem.Core.DTOs;

// ── Phase 10.19J — Operator audit/evidence review DTOs (desktop side).
//
// Matches the backend Phase 10.19I records at
//   Ham-Pos/src/main/java/com/example/hampos/security/dto/operator/
//     OperatorAuditEventSummaryResponse.java
//     OperatorAuditEventDetailResponse.java
//     OperatorAuditEventPageResponse.java
//
// Security contract:
//   • Read-only projections of sanitized audit_logs rows. The desktop
//     applies an additional redaction layer (see ApplyDesktopRedaction
//     inside the review wrapper) before rendering, so accidental
//     upstream leaks cannot reach the screen.
//   • No tokens, no passwords, no confirmation phrases, no raw file
//     content, no DB content, no stack traces.

public sealed class OperatorAuditEventSummaryDto
{
    [JsonPropertyName("eventId")]
    public long? EventId { get; set; }

    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("entityType")]
    public string? EntityType { get; set; }

    [JsonPropertyName("entityId")]
    public string? EntityId { get; set; }

    [JsonPropertyName("tenantId")]
    public string? TenantId { get; set; }

    [JsonPropertyName("userId")]
    public long? UserId { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime? CreatedAt { get; set; }

    [JsonPropertyName("operationName")]
    public string? OperationName { get; set; }

    [JsonPropertyName("permissionKey")]
    public string? PermissionKey { get; set; }

    [JsonPropertyName("accepted")]
    public bool? Accepted { get; set; }

    [JsonPropertyName("auditSource")]
    public string? AuditSource { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }
}

public sealed class OperatorAuditEventDetailDto
{
    [JsonPropertyName("eventId")]
    public long? EventId { get; set; }

    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("entityType")]
    public string? EntityType { get; set; }

    [JsonPropertyName("entityId")]
    public string? EntityId { get; set; }

    [JsonPropertyName("tenantId")]
    public string? TenantId { get; set; }

    [JsonPropertyName("userId")]
    public long? UserId { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime? CreatedAt { get; set; }

    [JsonPropertyName("operationName")]
    public string? OperationName { get; set; }

    [JsonPropertyName("permissionKey")]
    public string? PermissionKey { get; set; }

    [JsonPropertyName("accepted")]
    public bool? Accepted { get; set; }

    [JsonPropertyName("auditSource")]
    public string? AuditSource { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    // Sanitized key/value map produced by the backend review service.
    // Values may be primitives, lists, or nested maps — the desktop
    // walks the tree and re-redacts before rendering.
    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }

    [JsonPropertyName("redacted")]
    public bool Redacted { get; set; }

    [JsonPropertyName("reviewSource")]
    public string? ReviewSource { get; set; }
}

public sealed class OperatorAuditEventPageDto
{
    [JsonPropertyName("items")]
    public List<OperatorAuditEventSummaryDto> Items { get; set; } = new();

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("size")]
    public int Size { get; set; }

    [JsonPropertyName("totalElements")]
    public long TotalElements { get; set; }

    [JsonPropertyName("hasNext")]
    public bool HasNext { get; set; }
}

// Pure client-side filter bundle used by the wrapper service. Not sent
// to the wire as a JSON body — the wrapper composes it into the URL
// query string with EscapeDataString.
public sealed class OperatorAuditReviewQuery
{
    public string? TenantId      { get; set; }
    public string? EntityType    { get; set; }
    public string? Action        { get; set; }
    public string? OperationName { get; set; }
    public string? PermissionKey { get; set; }
    public bool?   Accepted      { get; set; }
    public DateTime? From        { get; set; }
    public DateTime? To          { get; set; }
    public int Page              { get; set; }
    public int Size              { get; set; } = 50;
}
