using System.Collections.Generic;

namespace PosSystem.Services.EvidenceBundleExport;

// Phase 10.22E — outcome of one
// <see cref="EvidenceBundleExportService"/> call (validate / scan /
// manifest / ZIP). Carries only safe display fields.
//
// `Outcome` values:
//   • Disabled       — local flag is OFF
//   • ValidationOnly — validation ran successfully; no ZIP yet
//   • Generated      — manifest + ZIP successfully written
//   • Blocked        — validation, scan, or MIME check rejected
//                       the bundle
//   • Failed         — an unexpected I/O / encoding error
public sealed record EvidenceBundleExportResult(
    string Outcome,
    string StatusMessage,
    int FileCount,
    long TotalBytes,
    string? ManifestRelativePath,
    string? ZipRelativePath,
    string? BundleSha256Hex,
    IReadOnlyList<EvidenceBundleExportFileItem> Files,
    IReadOnlyList<EvidenceBundleValidationIssue> ValidationIssues,
    IReadOnlyList<EvidenceBundleRedactionFinding> RedactionFindings,
    IReadOnlyList<string> GeneratedArtifactRelativePaths);
