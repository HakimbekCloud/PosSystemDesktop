using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PosSystem.Core.DTOs;

// ── Phase 10.19G — Backend operator audit-intent and evidence-registration DTOs
//
// Matches the backend Phase 10.19F record DTOs at
//   Ham-Pos/src/main/java/com/example/hampos/security/dto/operator/
//     OperatorAuditIntentRequest.java
//     OperatorAuditIntentResponse.java
//     OperatorEvidenceRegisterRequest.java
//     OperatorEvidenceRegisterResponse.java
//
// Security contract (identical to the backend Phase 10.19F DTOs):
//   • No ConfirmationPhrase / Phrase field. Phrases are local-only.
//   • No AccessToken / RefreshToken / Authorization / Password / JWT field.
//   • No raw evidence content. Evidence registration is metadata-only —
//     no ZIP bytes, no JSON file content, no raw audit log content,
//     no DB content, no backups.
//   • IncludedFiles carries bare file names only. The desktop strips
//     anything that looks like a path component before sending.
//   • ClientMachineNameHash carries a SHA-256 hex digest of the desktop's
//     Environment.MachineName, never the raw machine name.
//
// Style mirrors OperatorPermissionDtos.cs: sealed classes, [JsonPropertyName]
// camelCase wire names, nullable reference types.

public sealed class OperatorAuditIntentRequestDto
{
    [JsonPropertyName("operationName")]
    public string OperationName { get; set; } = "";

    [JsonPropertyName("permissionKey")]
    public string PermissionKey { get; set; } = "";

    [JsonPropertyName("tenantId")]
    public string? TenantId { get; set; }

    [JsonPropertyName("storeId")]
    public string? StoreId { get; set; }

    [JsonPropertyName("approvalTicketId")]
    public string? ApprovalTicketId { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("clientRequestId")]
    public string? ClientRequestId { get; set; }

    [JsonPropertyName("clientGeneratedAt")]
    public string? ClientGeneratedAt { get; set; }
}

public sealed class OperatorAuditIntentResultDto
{
    [JsonPropertyName("accepted")]
    public bool Accepted { get; set; }

    [JsonPropertyName("intentId")]
    public string? IntentId { get; set; }

    [JsonPropertyName("operationName")]
    public string? OperationName { get; set; }

    [JsonPropertyName("permissionKey")]
    public string? PermissionKey { get; set; }

    [JsonPropertyName("permissionAllowed")]
    public bool PermissionAllowed { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("tenantId")]
    public string? TenantId { get; set; }

    [JsonPropertyName("storeId")]
    public string? StoreId { get; set; }

    [JsonPropertyName("approvalTicketId")]
    public string? ApprovalTicketId { get; set; }

    [JsonPropertyName("recordedAt")]
    public DateTime? RecordedAt { get; set; }

    [JsonPropertyName("auditSource")]
    public string? AuditSource { get; set; }
}

public sealed class OperatorEvidenceRegisterRequestDto
{
    [JsonPropertyName("evidenceBundleId")]
    public string EvidenceBundleId { get; set; } = "";

    [JsonPropertyName("tenantId")]
    public string? TenantId { get; set; }

    [JsonPropertyName("storeId")]
    public string? StoreId { get; set; }

    [JsonPropertyName("pilotId")]
    public string? PilotId { get; set; }

    [JsonPropertyName("approvalTicketId")]
    public string? ApprovalTicketId { get; set; }

    [JsonPropertyName("bundleGeneratedAt")]
    public string? BundleGeneratedAt { get; set; }

    [JsonPropertyName("readinessOverallStatus")]
    public string? ReadinessOverallStatus { get; set; }

    [JsonPropertyName("backendPermissionEnforcementEnabled")]
    public bool? BackendPermissionEnforcementEnabled { get; set; }

    [JsonPropertyName("backendPermissionSummaryStatus")]
    public string? BackendPermissionSummaryStatus { get; set; }

    [JsonPropertyName("includedFiles")]
    public List<string> IncludedFiles { get; set; } = new();

    [JsonPropertyName("fileCount")]
    public int? FileCount { get; set; }

    [JsonPropertyName("totalBytes")]
    public long? TotalBytes { get; set; }

    [JsonPropertyName("manifestSha256")]
    public string? ManifestSha256 { get; set; }

    [JsonPropertyName("bundleSha256")]
    public string? BundleSha256 { get; set; }

    [JsonPropertyName("clientMachineNameHash")]
    public string? ClientMachineNameHash { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

public sealed class OperatorEvidenceRegisterResultDto
{
    [JsonPropertyName("accepted")]
    public bool Accepted { get; set; }

    [JsonPropertyName("registrationId")]
    public string? RegistrationId { get; set; }

    [JsonPropertyName("evidenceBundleId")]
    public string? EvidenceBundleId { get; set; }

    [JsonPropertyName("tenantId")]
    public string? TenantId { get; set; }

    [JsonPropertyName("storeId")]
    public string? StoreId { get; set; }

    [JsonPropertyName("fileCount")]
    public int? FileCount { get; set; }

    [JsonPropertyName("totalBytes")]
    public long? TotalBytes { get; set; }

    [JsonPropertyName("recordedAt")]
    public DateTime? RecordedAt { get; set; }

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();

    [JsonPropertyName("auditSource")]
    public string? AuditSource { get; set; }
}
