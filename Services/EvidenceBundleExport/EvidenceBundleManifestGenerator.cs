using System;
using System.Collections.Generic;
using System.Globalization;
using PosSystem.Core.DTOs;

namespace PosSystem.Services.EvidenceBundleExport;

// Phase 10.22E — builds the canonical `manifest.json` payload for the
// desktop-side ZIP bundle.
//
// Schema is identical to backend Phase 10.22D
// EvidenceBundleManifestValidator (`operator-evidence-bundle-v1`).
// Critical rules:
//   • files[] EXCLUDES manifest.json itself.
//   • Each file entry carries the bare relative path (forward-slash),
//     SHA-256 hex, byte count, and redacted=true (the orchestrator
//     only adds files that passed the scanner + MIME checks).
//   • redactionChecklist values are all true ONLY if every text-like
//     file passed the local scanner and no MIME mismatch was recorded.
//   • generatedAt is ISO-8601 UTC.
//   • createdBy is the supplied operator label (already scrubbed).
//   • tenantId / storeId / waveNumber are optional.
//   • Signoff is intentionally null in Phase 10.22E; Phase 10.22G
//     will introduce the review/sign-off endpoint.
public sealed class EvidenceBundleManifestGenerator
{
    public EvidenceBundleManifestDto Build(
        string phase,
        string evidenceType,
        string environment,
        string? tenantId,
        string? storeId,
        int? waveNumber,
        string createdBy,
        IReadOnlyList<EvidenceBundleExportFileItem> includedFiles,
        bool redactionPassedForAllText)
    {
        ArgumentNullException.ThrowIfNull(phase);
        ArgumentNullException.ThrowIfNull(evidenceType);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(createdBy);
        ArgumentNullException.ThrowIfNull(includedFiles);

        var files = new List<EvidenceBundleManifestFileDto>(includedFiles.Count);
        foreach (var f in includedFiles)
        {
            // Defence in depth: never emit manifest.json itself into files[].
            if (string.Equals(f.RelativePath, "manifest.json", StringComparison.OrdinalIgnoreCase))
                continue;

            files.Add(new EvidenceBundleManifestFileDto
            {
                Path      = f.RelativePath,
                Sha256    = f.Sha256Hex,
                SizeBytes = f.SizeBytes,
                Redacted  = true,
            });
        }

        var checklist = new EvidenceBundleRedactionChecklistDto
        {
            AuthorizationHeadersRemoved = redactionPassedForAllText,
            BearerTokensRemoved         = redactionPassedForAllText,
            JwtBodiesRemoved            = redactionPassedForAllText,
            PasswordsRemoved            = redactionPassedForAllText,
            TokensRemoved               = redactionPassedForAllText,
            ConfirmationPhrasesRemoved  = redactionPassedForAllText,
        };

        return new EvidenceBundleManifestDto
        {
            SchemaVersion       = "operator-evidence-bundle-v1",
            Phase               = phase,
            EvidenceType        = evidenceType,
            Environment         = environment,
            TenantId            = string.IsNullOrWhiteSpace(tenantId) ? null : tenantId,
            StoreId             = string.IsNullOrWhiteSpace(storeId) ? null : storeId,
            WaveNumber          = waveNumber,
            GeneratedAt         = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            CreatedBy           = createdBy,
            Files               = files,
            RedactionChecklist  = checklist,
            // Phase 10.22G will add review/signoff. Until then the
            // signoff section is intentionally absent so a desktop
            // operator never accidentally signs off their own bundle.
            Signoff             = null,
        };
    }
}
