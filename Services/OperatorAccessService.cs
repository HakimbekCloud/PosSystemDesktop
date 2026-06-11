using PosSystem.Data.Repositories;

namespace PosSystem.Services;

// Phase 10.9B: gate for operator/support windows. Each operator surface
// requires BOTH a per-machine feature flag (from GlobalSettingsRepository)
// AND, when role information is available, an allowed user role. Roles are
// checked case-insensitively against a short whitelist that matches the
// backend's role taxonomy (ADMIN, GLOBAL_ADMIN, SUPER_ADMIN, SUPPORT, OWNER).
//
// Degraded mode — when the current session has no recorded user role:
//   • No session at all (e.g. login screen visible) → role check skipped.
//   • Pre-Phase-10.9B sessions whose user_role was never persisted → role
//     check skipped (grandfathered).
//   Both cases fall back to flag-gated-only access. After the next
//   successful login, the role is captured and enforced.
//
// This service does NOT parse JWT claims manually. The role comes from the
// backend's login response (User.Role) which AuthService.LoginAsync persists
// to Settings.user_role.
public sealed class OperatorAccessService
{
    public const string DiagnosticsFlagKey          = "operator_diagnostics_ui_enabled";
    public const string MigrationDashboardFlagKey   = "operator_migration_dashboard_enabled";
    public const string MissingRoleOverrideFlagKey  = "operator_access_allow_missing_role";

    private static readonly System.Collections.Generic.HashSet<string> AllowedRoles =
        new(System.StringComparer.OrdinalIgnoreCase)
        {
            "ADMIN",
            "GLOBAL_ADMIN",
            "SUPER_ADMIN",
            "SUPPORT",
            "OWNER",
        };

    private readonly GlobalSettingsRepository _global;
    private readonly AuthService              _auth;

    public OperatorAccessService(GlobalSettingsRepository global, AuthService auth)
    {
        _global = global;
        _auth   = auth;
    }

    public bool CanOpenOperatorDiagnostics() =>
        _global.Get(DiagnosticsFlagKey) == "1" && IsAuthorizedByRole();

    public bool CanOpenMigrationOperations() =>
        _global.Get(MigrationDashboardFlagKey) == "1" && IsAuthorizedByRole();

    // Phase 10.9B.1: when role is present, enforce the allow-list strictly.
    // When role is missing, require an explicit per-machine override flag
    // (operator_access_allow_missing_role="1") — being-on-the-login-screen or
    // having a pre-Phase-10.9B grandfathered session is no longer sufficient
    // on its own.
    public bool IsAuthorizedByRole()
    {
        var role = _auth.GetCurrentUserRole();
        if (string.IsNullOrEmpty(role))
            return MissingRoleOverrideEnabled();
        return AllowedRoles.Contains(role);
    }

    // True when global_settings.json[operator_access_allow_missing_role] == "1".
    // Operator UIs (or a future diagnostics warning) can surface this so the
    // operator knows the override is currently active.
    public bool MissingRoleOverrideEnabled() =>
        _global.Get(MissingRoleOverrideFlagKey) == "1";

    // True when a non-empty role is recorded for the current session. When
    // false, IsAuthorizedByRole() falls back to MissingRoleOverrideEnabled().
    public bool RoleGatingActive() =>
        !string.IsNullOrEmpty(_auth.GetCurrentUserRole());

    // Convenience accessor for the current session role. Null when no role
    // is recorded (login screen, or a pre-Phase-10.9B grandfathered session).
    public string? GetCurrentRole()
    {
        var role = _auth.GetCurrentUserRole();
        return string.IsNullOrEmpty(role) ? null : role;
    }
}
