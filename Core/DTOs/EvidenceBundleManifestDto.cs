using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PosSystem.Core.DTOs;

// Phase 10.22E — canonical evidence bundle manifest DTOs. Field names
// and casing match the backend Phase 10.22D EvidenceBundleManifestValidator
// schema (`operator-evidence-bundle-v1`) so a desktop-generated
// manifest.json round-trips through the future Phase 10.22F upload
// without re-shaping.
//
// Safety contract:
//   • No ConfirmationPhrase / Token / Password / RawPath fields.
//   • Operator-supplied strings are scrubbed at the orchestrator layer
//     before they land here.
//   • `Files[]` carries bare relative paths only (forward-slashes),
//     never absolute filesystem paths.
//   • manifest.json itself is intentionally NOT in `Files[]`.
public sealed class EvidenceBundleManifestDto
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = "operator-evidence-bundle-v1";

    [JsonPropertyName("phase")]
    public string Phase { get; init; } = "";

    [JsonPropertyName("evidenceType")]
    public string EvidenceType { get; init; } = "";

    [JsonPropertyName("environment")]
    public string Environment { get; init; } = "";

    [JsonPropertyName("tenantId")]
    public string? TenantId { get; init; }

    [JsonPropertyName("storeId")]
    public string? StoreId { get; init; }

    [JsonPropertyName("waveNumber")]
    public int? WaveNumber { get; init; }

    [JsonPropertyName("generatedAt")]
    public string GeneratedAt { get; init; } = "";

    [JsonPropertyName("createdBy")]
    public string CreatedBy { get; init; } = "";

    [JsonPropertyName("files")]
    public List<EvidenceBundleManifestFileDto> Files { get; init; } = new();

    [JsonPropertyName("redactionChecklist")]
    public EvidenceBundleRedactionChecklistDto RedactionChecklist { get; init; }
        = new();

    [JsonPropertyName("signoff")]
    public EvidenceBundleSignoffDto? Signoff { get; init; }
}

public sealed class EvidenceBundleManifestFileDto
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = "";

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = "";

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; init; }

    [JsonPropertyName("redacted")]
    public bool Redacted { get; init; }
}

// Six required keys, all required true at finalize time on the backend.
// The desktop only sets these to true once the local scan + MIME
// validators have passed every included text/binary file.
public sealed class EvidenceBundleRedactionChecklistDto
{
    [JsonPropertyName("authorizationHeadersRemoved")]
    public bool AuthorizationHeadersRemoved { get; init; }

    [JsonPropertyName("bearerTokensRemoved")]
    public bool BearerTokensRemoved { get; init; }

    [JsonPropertyName("jwtBodiesRemoved")]
    public bool JwtBodiesRemoved { get; init; }

    [JsonPropertyName("passwordsRemoved")]
    public bool PasswordsRemoved { get; init; }

    [JsonPropertyName("tokensRemoved")]
    public bool TokensRemoved { get; init; }

    [JsonPropertyName("confirmationPhrasesRemoved")]
    public bool ConfirmationPhrasesRemoved { get; init; }
}

public sealed class EvidenceBundleSignoffDto
{
    [JsonPropertyName("decision")]
    public string? Decision { get; init; }

    [JsonPropertyName("reviewer")]
    public string? Reviewer { get; init; }

    [JsonPropertyName("signedAt")]
    public string? SignedAt { get; init; }

    [JsonPropertyName("signoffPath")]
    public string? SignoffPath { get; init; }
}
