namespace PosSystem.Services.EvidenceBundleExport;

// Phase 10.22E desktop mirror of the backend Phase 10.22D
// EvidenceBundleRedactionFindingType. The names match the backend
// exactly so a desktop-generated manifest's redaction report and the
// backend's audit metadata speak the same vocabulary.
public enum EvidenceBundleRedactionFindingType
{
    AuthorizationHeader,
    BearerToken,
    JwtLike,
    AccessToken,
    RefreshToken,
    Password,
    Secret,
    ApiKey,
    DpapiSealedValue,
    PrivateKey,
    /// <summary>
    /// Structural match for a confirmation-phrase-shaped literal — verb
    /// keyword + 2+ ALL-CAPS underscore-separated segments. The scanner
    /// does NOT name the real guarded-flow phrase literals; the source
    /// tree never carries them.
    /// </summary>
    ConfirmationPhrase,
    /// <summary>
    /// Invalid UTF-8 inside a file declared as text-like. Maps to
    /// "unscannable" at the orchestrator layer.
    /// </summary>
    DecodingFailure,
}
