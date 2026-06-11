namespace PosSystem.ViewModels;

// ── Phase 10.19D — Backend permission preflight result ──────────────────────
//
// Internal value type returned by
// MigrationOperationsViewModel.ValidateBackendPermissionForDangerousOperationAsync.
// Carries enough context for the calling Execute*CoreAsync to either
// continue to the guarded wrapper (Allowed=true) or block before the
// wrapper (Allowed=false) without affecting any existing local guard.
//
// Status values used:
//   "Skipped"          — enforcement flag is OFF; preflight not required
//   "Allowed"          — enforcement ON, backend allowed, metadata matches
//   "Denied"           — enforcement ON, backend allowed=false
//   "Unavailable"      — enforcement ON, backend offline / 401 / 5xx / null
//   "MetadataMismatch" — enforcement ON, allowed but metadata booleans wrong
//
// This type is internal because it is purely a transport between the
// helper and the four Execute*CoreAsync methods inside the same
// ViewModel; no other file needs to construct or inspect it.
internal sealed class BackendPermissionPreflightResult
{
    public bool   EnforcementEnabled         { get; init; }
    public bool   Allowed                    { get; init; }
    public string Status                     { get; init; } = "";
    public string Reason                     { get; init; } = "";
    public string PermissionKey              { get; init; } = "";
    public bool   RequiresLocalFlag          { get; init; }
    public bool   RequiresConfirmationPhrase { get; init; }
    public bool   RequiresGuardedWrapper     { get; init; }
}
