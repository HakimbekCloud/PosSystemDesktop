namespace PosSystem.Services.EvidenceBundleExport;

// Phase 10.22E — one folder-validation issue (extension blocked, path
// traversal, etc.). Carries only safe metadata; never raw file
// content. `Code` is one of EvidenceBundlePathSafety.Outcome.ToString()
// or a small fixed set of orchestrator-level reasons.
public sealed record EvidenceBundleValidationIssue(
    string FilePath,
    string Code,
    string SafeMessage);
