namespace PosSystem.Services.EvidenceBundleExport;

// Phase 10.22E redaction-scan finding. Carries only safe metadata —
// the raw matched value NEVER appears here, in logs, in audit
// metadata, in the manifest, or in any UI string. The Preview field
// is always the literal "[REDACTED]" token; callers display
// `FilePath + ":" + LineNumber + " — " + Type + " " + Preview` only.
public sealed record EvidenceBundleRedactionFinding(
    string FilePath,
    EvidenceBundleRedactionFindingType Type,
    int LineNumber,
    string Preview = "[REDACTED]");
