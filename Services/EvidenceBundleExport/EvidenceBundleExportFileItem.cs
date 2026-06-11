namespace PosSystem.Services.EvidenceBundleExport;

// Phase 10.22E — one accepted file row in the export pipeline output.
// Mirrors what the manifest will record; carries no absolute path.
public sealed record EvidenceBundleExportFileItem(
    string RelativePath,
    long SizeBytes,
    string Sha256Hex,
    bool TextLike);
