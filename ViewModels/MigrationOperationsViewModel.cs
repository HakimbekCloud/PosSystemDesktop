using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PosSystem.Core.DTOs;
using PosSystem.Data.Repositories;
using PosSystem.Services;
using PosSystem.Services.EvidenceBundleExport;

namespace PosSystem.ViewModels;

// Read-only Migration Operations dashboard ViewModel. Aggregates the same set
// of read-only operator services that the diagnostics screen uses, plus the
// migration auditor and verifier, and synthesizes a high-level summary an
// operator can scan before deciding whether to run the migrator/rollback (via
// debugger / future operator UI — NOT from this dashboard).
//
// Read-only contract — services injected:
//   OperatorDiagnosticsService           (Phase 10.7A)
//   OperatorDiagnosticsExportService     (Phase 10.7B / 10.7C)
//   SharedToTenantMigrationAuditor       (Phase 10.4A)
//   SharedToTenantMigrationVerifier      (Phase 10.4C)
//   TenantCutoverReadinessGate           (Phase 10.5A.1)
//   TenantDbRollbackReadinessChecker     (Phase 10.6A.1)
//   GlobalSettingsRepository
//
// Deliberately NOT injected:
//   SharedToTenantDatabaseMigrator       (would execute migration)
//   TenantDbRollbackExecutor             (would execute rollback)
//   TenantScopeService                   (would switch tenant DB)
public partial class MigrationOperationsViewModel : ObservableObject
{
    private readonly OperatorDiagnosticsService       _diagnostics;
    private readonly OperatorDiagnosticsExportService _exporter;
    private readonly SharedToTenantMigrationAuditor   _auditor;
    private readonly SharedToTenantMigrationVerifier  _verifier;
    private readonly TenantCutoverReadinessGate       _cutoverGate;
    private readonly TenantDbRollbackReadinessChecker _rollbackChecker;
    private readonly GlobalSettingsRepository         _global;
    private readonly MigrationDryRunPreviewService    _dryRunPreview;
    private readonly RollbackDryRunPreviewService     _rollbackPreview;
    private readonly MigrationOperationsPreflightExportService _preflightExport;
    private readonly TenantDatabaseInventoryService   _inventory;
    private readonly TenantDatabaseRetentionPreviewService _retentionPreview;
    private readonly TenantDatabaseInventoryExportService _inventoryExport;
    private readonly RealMigrationExecutionGateService _realMigrationGate;
    private readonly GuardedRealMigrationExecutorService _guardedExecutor;
    private readonly OperatorAccessService _operatorAccess;
    private readonly GuardedRuntimeCutoverExecutorService _guardedRuntimeCutover;
    private readonly GuardedRollbackExecutorService _guardedRollback;
    private readonly GuardedRetentionCleanupExecutorService _guardedRetentionCleanup;
    private readonly ProductionPilotReadinessReportService _pilotReadinessReport;
    private readonly ProductionPilotEvidenceBundleService _pilotEvidenceBundle;
    private readonly OperatorPermissionApiClient _operatorPermissionApi;
    private readonly OperatorAuditEvidenceApiClient _operatorAuditEvidenceApi;
    private readonly OperatorAuditReviewApiClient _operatorAuditReviewApi;
    private readonly OperatorPermissionAdminApiClient _operatorPermissionAdminApi;
    private readonly OperatorPermissionAdminMutationApiClient _operatorPermissionAdminMutationApi;
    private readonly Services.EvidenceBundleExport.EvidenceBundleExportService _evidenceBundleExport;
    private readonly Services.EvidenceBundleUpload.EvidenceBundleUploadService _evidenceBundleUpload;
    private readonly Services.EvidenceBundleReview.EvidenceBundleReviewService _evidenceBundleReview;
    private readonly Services.EvidenceBundleRetention.EvidenceBundleRetentionService _evidenceBundleRetention;
    private readonly Services.EvidenceBundleLifecycleScheduler.EvidenceBundleLifecycleSchedulerStatusService _lifecycleScheduler;

    // Phase 10.19G — default-OFF feature flags for the backend audit-intent
    // and evidence-registration HTTP calls. Missing / "" / "0" ⇒ no call.
    private const string BackendAuditIntentFlagKey            = "operator_backend_audit_intent_enabled";
    private const string BackendEvidenceRegistrationFlagKey   = "operator_backend_evidence_registration_enabled";

    // Phase 10.19J — default-OFF feature flag for the read-only operator
    // audit/evidence review UI. Missing / "" / "0" ⇒ no UI / no HTTP.
    private const string BackendAuditReviewUiFlagKey          = "operator_backend_audit_review_ui_enabled";

    // Phase 10.20G — default-OFF feature flag for the read-only operator
    // permission admin UI. Missing / "" / "0" ⇒ disabled card, no HTTP.
    private const string PermissionAdminReadOnlyUiFlagKey     = "operator_permission_admin_readonly_ui_enabled";

    // Phase 10.20I — default-OFF feature flag for the operator permission
    // admin MUTATION UI. Missing / "" / "0" ⇒ disabled card, no HTTP.
    // The backend has its own flag (operator.permission.admin.mutations.enabled)
    // that gates the actual POST endpoints — both flags must be ON for a
    // mutation to commit.
    private const string PermissionAdminMutationUiFlagKey     = "operator_permission_admin_mutation_ui_enabled";

    // Phase 10.21G — default-OFF feature flag for the read-only operator
    // permission authoritative-status card. Missing / "" / "0" ⇒ disabled
    // card, no HTTP. The backend endpoint
    // (GET /api/v1/admin/operator-permissions/authoritative-status) is
    // read-only and does NOT change any permission decision. This flag
    // only controls whether the desktop displays the status card and
    // calls the backend on Refresh.
    private const string PermissionAuthoritativeStatusUiFlagKey =
            "operator_permission_authoritative_status_ui_enabled";

    // Permission keys treated as dangerous on the desktop side for local
    // validation (dangerous ALLOW requires expiresAt). Mirrors the backend
    // OperatorPermissionKey.DANGEROUS set.
    private static readonly System.Collections.Generic.HashSet<string> DangerousPermissionKeys = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "operator.migration.execute",
        "operator.cutover.execute",
        "operator.rollback.execute",
        "operator.retention.cleanup.execute",
        "operator.flags.change",
    };

    // Phase 10.22P — default-OFF flag for the lifecycle scheduler status UI.
    private const string LifecycleSchedulerStatusUiFlagKey =
        Services.EvidenceBundleLifecycleScheduler.EvidenceBundleLifecycleSchedulerStatusService.LocalFlagKey;
    private const string LifecycleSchedulerManualRunFlagKey =
        Services.EvidenceBundleLifecycleScheduler.EvidenceBundleLifecycleSchedulerStatusService.ManualRunFlagKey;

    private const string RealMigrationUiFlagKey       = "operator_real_migration_ui_enabled";
    private const string RuntimeCutoverUiFlagKey      = "operator_runtime_cutover_ui_enabled";
    private const string RollbackUiFlagKey            = "operator_rollback_ui_enabled";
    private const string RetentionCleanupUiFlagKey    = "operator_retention_cleanup_ui_enabled";

    public MigrationOperationsViewModel(
        OperatorDiagnosticsService diagnostics,
        OperatorDiagnosticsExportService exporter,
        SharedToTenantMigrationAuditor auditor,
        SharedToTenantMigrationVerifier verifier,
        TenantCutoverReadinessGate cutoverGate,
        TenantDbRollbackReadinessChecker rollbackChecker,
        GlobalSettingsRepository global,
        MigrationDryRunPreviewService dryRunPreview,
        RollbackDryRunPreviewService rollbackPreview,
        MigrationOperationsPreflightExportService preflightExport,
        TenantDatabaseInventoryService inventory,
        TenantDatabaseRetentionPreviewService retentionPreview,
        TenantDatabaseInventoryExportService inventoryExport,
        RealMigrationExecutionGateService realMigrationGate,
        GuardedRealMigrationExecutorService guardedExecutor,
        OperatorAccessService operatorAccess,
        GuardedRuntimeCutoverExecutorService guardedRuntimeCutover,
        GuardedRollbackExecutorService guardedRollback,
        GuardedRetentionCleanupExecutorService guardedRetentionCleanup,
        ProductionPilotReadinessReportService pilotReadinessReport,
        ProductionPilotEvidenceBundleService pilotEvidenceBundle,
        OperatorPermissionApiClient operatorPermissionApi,
        OperatorAuditEvidenceApiClient operatorAuditEvidenceApi,
        OperatorAuditReviewApiClient operatorAuditReviewApi,
        OperatorPermissionAdminApiClient operatorPermissionAdminApi,
        OperatorPermissionAdminMutationApiClient operatorPermissionAdminMutationApi,
        Services.EvidenceBundleExport.EvidenceBundleExportService evidenceBundleExport,
        Services.EvidenceBundleUpload.EvidenceBundleUploadService evidenceBundleUpload,
        Services.EvidenceBundleReview.EvidenceBundleReviewService evidenceBundleReview,
        Services.EvidenceBundleRetention.EvidenceBundleRetentionService evidenceBundleRetention,
        Services.EvidenceBundleLifecycleScheduler.EvidenceBundleLifecycleSchedulerStatusService lifecycleScheduler)
    {
        _diagnostics             = diagnostics;
        _exporter                = exporter;
        _auditor                 = auditor;
        _verifier                = verifier;
        _cutoverGate             = cutoverGate;
        _rollbackChecker         = rollbackChecker;
        _global                  = global;
        _dryRunPreview           = dryRunPreview;
        _rollbackPreview         = rollbackPreview;
        _preflightExport         = preflightExport;
        _inventory               = inventory;
        _retentionPreview        = retentionPreview;
        _inventoryExport         = inventoryExport;
        _realMigrationGate       = realMigrationGate;
        _guardedExecutor         = guardedExecutor;
        _operatorAccess          = operatorAccess;
        _guardedRuntimeCutover   = guardedRuntimeCutover;
        _guardedRollback         = guardedRollback;
        _guardedRetentionCleanup = guardedRetentionCleanup;
        _pilotReadinessReport    = pilotReadinessReport;
        _pilotEvidenceBundle     = pilotEvidenceBundle;
        _operatorPermissionApi              = operatorPermissionApi;
        _operatorAuditEvidenceApi           = operatorAuditEvidenceApi;
        _operatorAuditReviewApi             = operatorAuditReviewApi;
        _operatorPermissionAdminApi         = operatorPermissionAdminApi;
        _operatorPermissionAdminMutationApi = operatorPermissionAdminMutationApi;
        _evidenceBundleExport               = evidenceBundleExport;
        _evidenceBundleUpload               = evidenceBundleUpload;
        _evidenceBundleReview               = evidenceBundleReview;
        _evidenceBundleRetention            = evidenceBundleRetention;
        _lifecycleScheduler                 = lifecycleScheduler;

        RecalculateRealMigrationUiEnabled();
        RecalculateRuntimeCutoverUiEnabled();
        RecalculateRollbackUiEnabled();
        RecalculateRetentionCleanupUiEnabled();
        RefreshEvidenceBundleExportFlag();
        RefreshEvidenceBundleUploadFlag();
        RefreshEvidenceBundleReviewFlag();
        RefreshEvidenceBundleRetentionFlag();
        RefreshLifecycleSchedulerFlag();
    }

    // ── Input ────────────────────────────────────────────────────────────────

    [ObservableProperty] private string _tenantSubdomainInput = "";

    // ── Status ───────────────────────────────────────────────────────────────

    [ObservableProperty] private bool   _isLoading;
    [ObservableProperty] private string _statusMessage  = "";
    [ObservableProperty] private System.DateTime? _lastUpdatedAt;
    [ObservableProperty] private string _lastExportPath = "";

    // ── Runtime/migration state ──────────────────────────────────────────────

    [ObservableProperty] private bool   _runtimeTenantDbEnabled;
    [ObservableProperty] private bool   _migrationFeatureEnabled;
    [ObservableProperty] private bool   _sharedToTenantMigrated;
    [ObservableProperty] private string _sharedToTenantMigratedAt = "";
    [ObservableProperty] private string _activeDbPath             = "";
    [ObservableProperty] private bool   _isTenantScoped;
    [ObservableProperty] private string _legacyDbPath             = "";
    [ObservableProperty] private string _lastTenantSubdomain      = "";

    // ── Audit / Verification summaries ───────────────────────────────────────

    [ObservableProperty] private string _migrationAuditSummary = "";
    [ObservableProperty] private string _verificationSummary   = "";
    [ObservableProperty] private string _cutoverStatus         = "";
    [ObservableProperty] private string _rollbackStatus        = "";

    // ── Sales / Cache ────────────────────────────────────────────────────────

    [ObservableProperty] private int  _pendingSalesCount;
    [ObservableProperty] private int  _poisonSalesCount;
    [ObservableProperty] private int  _productsCount;
    [ObservableProperty] private int  _customersCount;

    // ── Findings ─────────────────────────────────────────────────────────────

    public ObservableCollection<string> Warnings { get; } = new();
    public ObservableCollection<string> Errors   { get; } = new();

    // ── Dangerous Operation Lock (Phase 10.15A) ─────────────────────────────
    //
    // Single global lock shared by all dangerous execute commands (real
    // migration, runtime cutover, rollback). When one is running, the other
    // two are disabled via CanExecute, and a second click on the same button
    // is rejected by the helper's early-return.

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRealMigrationCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRuntimeCutoverCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRollbackCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRetentionCleanupCommand))]
    private bool _isDangerousOperationRunning;

    [ObservableProperty] private string _dangerousOperationName          = "";
    [ObservableProperty] private string _dangerousOperationStatusMessage = "";

    private async System.Threading.Tasks.Task RunDangerousOperationAsync(
        string operationName,
        System.Func<System.Threading.CancellationToken, System.Threading.Tasks.Task> operation,
        System.Threading.CancellationToken ct = default)
    {
        // Re-entry / cross-command guard. The first caller wins; subsequent
        // calls (including double-clicks on the same button) silently no-op.
        if (IsDangerousOperationRunning) return;

        IsDangerousOperationRunning      = true;
        DangerousOperationName           = operationName;
        DangerousOperationStatusMessage  = $"{operationName} is currently running. Other dangerous actions are disabled.";

        try
        {
            await operation(ct);
        }
        finally
        {
            IsDangerousOperationRunning     = false;
            DangerousOperationName          = "";
            DangerousOperationStatusMessage = "";
        }
    }

    // ── Migration Dry-Run Preview (Phase 10.10A) ────────────────────────────

    [ObservableProperty] private string _migrationDryRunSummary       = "";
    [ObservableProperty] private string _migrationDryRunOutcome       = "";
    [ObservableProperty] private bool   _migrationDryRunAvailable;
    [ObservableProperty] private int    _migrationDryRunTenantCount;
    [ObservableProperty] private int    _migrationDryRunWarningCount;
    [ObservableProperty] private int    _migrationDryRunErrorCount;
    public ObservableCollection<string> MigrationDryRunDetails { get; } = new();

    // ── Side-effect guard (Phase 10.10A.1) ──────────────────────────────────

    [ObservableProperty] private bool _migrationDryRunSideEffectCheckPassed;
    [ObservableProperty] private int  _migrationDryRunSideEffectDifferenceCount;
    public ObservableCollection<string> MigrationDryRunSideEffectDifferences { get; } = new();

    // ── Rollback Dry-Run Preview (Phase 10.10B) ─────────────────────────────

    [ObservableProperty] private string _rollbackDryRunSummary                  = "";
    [ObservableProperty] private string _rollbackDryRunOutcome                  = "";
    [ObservableProperty] private bool   _rollbackDryRunAvailable;
    [ObservableProperty] private string _rollbackDryRunReadinessStatus          = "";
    [ObservableProperty] private bool   _rollbackDryRunRuntimeTenantDbEnabled;
    [ObservableProperty] private bool   _rollbackDryRunIsProviderTenantScoped;
    [ObservableProperty] private string _rollbackDryRunLegacyDbPath             = "";
    [ObservableProperty] private bool   _rollbackDryRunLegacyDbExists;
    [ObservableProperty] private bool   _rollbackDryRunLegacyDbReadable;
    [ObservableProperty] private string _rollbackDryRunTenantsDirectoryPath     = "";
    [ObservableProperty] private string _rollbackDryRunPlannedArchivePath       = "";
    [ObservableProperty] private bool   _rollbackDryRunWouldDisableRuntimeFlag;
    [ObservableProperty] private bool   _rollbackDryRunWouldArchiveTenantsDir;
    [ObservableProperty] private bool   _rollbackDryRunWouldRestoreLegacyFromBackup;
    [ObservableProperty] private bool   _rollbackDryRunSideEffectCheckPassed;
    [ObservableProperty] private int    _rollbackDryRunSideEffectDifferenceCount;

    public ObservableCollection<string> RollbackDryRunPlannedSteps             { get; } = new();
    public ObservableCollection<string> RollbackDryRunSideEffectDifferences    { get; } = new();

    // ── Preflight Export (Phase 10.10C) ─────────────────────────────────────

    [ObservableProperty] private string _lastPreflightExportPath = "";
    [ObservableProperty] private string _preflightStatusMessage  = "";

    // ── Tenant DB Inventory (Phase 10.11A) ──────────────────────────────────

    [ObservableProperty] private string _inventorySummary               = "";
    [ObservableProperty] private string _inventoryTotalKnownSize        = "";
    [ObservableProperty] private string _inventoryLegacyDbPath          = "";
    [ObservableProperty] private string _inventoryLegacyDbSize          = "";
    [ObservableProperty] private bool   _inventoryLegacyDbExists;
    [ObservableProperty] private int    _inventoryTenantDbCount;
    [ObservableProperty] private int    _inventoryBackupCount;
    [ObservableProperty] private int    _inventoryArchivedTenantDirCount;
    [ObservableProperty] private int    _inventoryBrokenLegacyDbCount;
    [ObservableProperty] private int    _inventoryMigrationLogCount;
    [ObservableProperty] private int    _inventoryRollbackLogCount;
    [ObservableProperty] private int    _inventoryDiagnosticsLogCount;
    [ObservableProperty] private int    _inventoryPreflightLogCount;

    public ObservableCollection<string> InventoryTenantDbLines { get; } = new();
    public ObservableCollection<string> InventoryBackupLines   { get; } = new();
    public ObservableCollection<string> InventoryArchiveLines  { get; } = new();
    public ObservableCollection<string> InventoryLogLines      { get; } = new();

    // ── Retention Preview (Phase 10.11B) ────────────────────────────────────

    [ObservableProperty] private string _retentionPreviewSummary             = "";
    [ObservableProperty] private int    _retentionPreviewCandidateCount;
    [ObservableProperty] private string _retentionPreviewCandidateSize       = "";
    [ObservableProperty] private int    _retentionPreviewProtectedItemCount;
    [ObservableProperty] private string _retentionPreviewProtectedSize       = "";

    public ObservableCollection<string> RetentionPreviewCandidateLines { get; } = new();
    public ObservableCollection<string> RetentionPreviewProtectedLines { get; } = new();

    // ── Inventory Export (Phase 10.11C) ─────────────────────────────────────

    [ObservableProperty] private string _lastInventoryExportPath      = "";
    [ObservableProperty] private string _inventoryExportStatusMessage = "";

    // ── Real Migration Execution Gate (Phase 10.12A) ────────────────────────

    [ObservableProperty] private string _realMigrationGateStatus  = "";
    [ObservableProperty] private bool   _realMigrationCanExecute;
    [ObservableProperty] private string _realMigrationGateSummary = "";

    public ObservableCollection<string> RealMigrationBlockingReasons  { get; } = new();
    public ObservableCollection<string> RealMigrationWarnings         { get; } = new();
    public ObservableCollection<string> RealMigrationRecommendedSteps { get; } = new();

    // ── Guarded Real Migration Execution (Phase 10.12C) ─────────────────────

    // Access flags
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRealMigrationCommand))]
    private bool _realMigrationUiEnabled;

    [ObservableProperty]
    private string _realMigrationUiEnabledStatusText = "Disabled by operator_real_migration_ui_enabled.";

    // Inputs (two-way bindings)
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRealMigrationCommand))]
    private string _realMigrationConfirmationPhraseInput = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRealMigrationCommand))]
    private bool _realMigrationExternalBackupAcknowledged;

    [ObservableProperty] private string _realMigrationExternalBackupNote = "";
    [ObservableProperty] private bool   _realMigrationAllowWarnings;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRealMigrationCommand))]
    private string _realMigrationReviewedPreflightExportPath = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRealMigrationCommand))]
    private string _realMigrationReviewedInventoryExportPath = "";

    // Execution status
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRealMigrationCommand))]
    private bool _realMigrationIsExecuting;

    [ObservableProperty] private string _realMigrationExecutionOutcome       = "";
    [ObservableProperty] private bool   _realMigrationExecuted;
    [ObservableProperty] private string _realMigrationExecutionStatusMessage = "";
    [ObservableProperty] private System.DateTime? _realMigrationExecutionStartedAtUtc;
    [ObservableProperty] private System.DateTime? _realMigrationExecutionCompletedAtUtc;

    public ObservableCollection<string> RealMigrationExecutionSteps           { get; } = new();
    public ObservableCollection<string> RealMigrationExecutionBlockingReasons { get; } = new();
    public ObservableCollection<string> RealMigrationExecutionWarnings        { get; } = new();
    public ObservableCollection<string> RealMigrationExecutionErrors          { get; } = new();

    // ── Guarded Runtime Cutover Execution (Phase 10.13B) ────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRuntimeCutoverCommand))]
    private bool _runtimeCutoverUiEnabled;

    [ObservableProperty]
    private string _runtimeCutoverUiEnabledStatusText = "Disabled by operator_runtime_cutover_ui_enabled.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRuntimeCutoverCommand))]
    private string _runtimeCutoverConfirmationPhraseInput = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRuntimeCutoverCommand))]
    private bool _runtimeCutoverExternalBackupAcknowledged;

    [ObservableProperty] private string _runtimeCutoverExternalBackupNote = "";
    [ObservableProperty] private bool   _runtimeCutoverAllowWarnings;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRuntimeCutoverCommand))]
    private string _runtimeCutoverReviewedPreflightExportPath = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRuntimeCutoverCommand))]
    private string _runtimeCutoverReviewedInventoryExportPath = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRuntimeCutoverCommand))]
    private bool _runtimeCutoverIsExecuting;

    [ObservableProperty] private string _runtimeCutoverExecutionOutcome       = "";
    [ObservableProperty] private bool   _runtimeCutoverFlagChanged;
    [ObservableProperty] private string _runtimeCutoverExecutionStatusMessage = "";
    [ObservableProperty] private System.DateTime? _runtimeCutoverExecutionStartedAtUtc;
    [ObservableProperty] private System.DateTime? _runtimeCutoverExecutionCompletedAtUtc;
    [ObservableProperty] private string _runtimeCutoverFlagBefore = "";
    [ObservableProperty] private string _runtimeCutoverFlagAfter  = "";

    public ObservableCollection<string> RuntimeCutoverExecutionSteps           { get; } = new();
    public ObservableCollection<string> RuntimeCutoverExecutionBlockingReasons { get; } = new();
    public ObservableCollection<string> RuntimeCutoverExecutionWarnings        { get; } = new();
    public ObservableCollection<string> RuntimeCutoverExecutionErrors          { get; } = new();

    // ── Guarded Rollback Execution (Phase 10.14B) ───────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRollbackCommand))]
    private bool _rollbackUiEnabled;

    [ObservableProperty]
    private string _rollbackUiEnabledStatusText = "Disabled by operator_rollback_ui_enabled.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRollbackCommand))]
    private string _rollbackConfirmationPhraseInput = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRollbackCommand))]
    private bool _rollbackExternalBackupAcknowledged;

    [ObservableProperty] private string _rollbackExternalBackupNote = "";
    [ObservableProperty] private bool   _rollbackAllowWarnings;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRollbackCommand))]
    private string _rollbackReviewedPreflightExportPath = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRollbackCommand))]
    private string _rollbackReviewedInventoryExportPath = "";

    // Default-safe checkboxes: archive tenants directory + disable runtime flag
    // are both required-true to enable the Execute button (matches the safe
    // real-rollback profile documented in the runbook). RestoreLegacyFromBackup
    // remains optional with default false.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRollbackCommand))]
    private bool _rollbackArchiveTenantsDirectory = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRollbackCommand))]
    private bool _rollbackDisableRuntimeFlag = true;

    [ObservableProperty] private bool _rollbackRestoreLegacyFromBackupIfMissing;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRollbackCommand))]
    private bool _rollbackIsExecuting;

    [ObservableProperty] private string _rollbackExecutionOutcome       = "";
    [ObservableProperty] private bool   _rollbackExecuted;
    [ObservableProperty] private string _rollbackExecutionStatusMessage = "";
    [ObservableProperty] private System.DateTime? _rollbackExecutionStartedAtUtc;
    [ObservableProperty] private System.DateTime? _rollbackExecutionCompletedAtUtc;
    [ObservableProperty] private bool   _rollbackRuntimeTenantDbEnabledBefore;
    [ObservableProperty] private bool   _rollbackRuntimeTenantDbEnabledAfter;
    [ObservableProperty] private bool   _rollbackIsProviderTenantScopedBefore;
    [ObservableProperty] private string _rollbackReadinessStatus = "";

    public ObservableCollection<string> RollbackExecutionSteps           { get; } = new();
    public ObservableCollection<string> RollbackExecutionBlockingReasons { get; } = new();
    public ObservableCollection<string> RollbackExecutionWarnings        { get; } = new();
    public ObservableCollection<string> RollbackExecutionErrors          { get; } = new();

    // ── Guarded Retention Cleanup Execution (Phase 10.16B) ──────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRetentionCleanupCommand))]
    private bool _retentionCleanupUiEnabled;

    [ObservableProperty]
    private string _retentionCleanupUiEnabledStatusText = "Disabled by operator_retention_cleanup_ui_enabled.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRetentionCleanupCommand))]
    private string _retentionCleanupConfirmationPhraseInput = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRetentionCleanupCommand))]
    private bool _retentionCleanupExternalBackupAcknowledged;

    [ObservableProperty] private string _retentionCleanupExternalBackupNote = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRetentionCleanupCommand))]
    private string _retentionCleanupReviewedInventoryExportPath = "";

    // Category opt-ins — default-true. The CanExecute predicate requires at
    // least one to be true; each is decorated to re-notify the command.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRetentionCleanupCommand))]
    private bool _retentionCleanupDeleteDiagnosticsLogs = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRetentionCleanupCommand))]
    private bool _retentionCleanupDeletePreflightLogs = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRetentionCleanupCommand))]
    private bool _retentionCleanupDeleteMigrationLogs = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRetentionCleanupCommand))]
    private bool _retentionCleanupDeleteRollbackLogs = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRetentionCleanupCommand))]
    private bool _retentionCleanupDeleteOldBackups = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRetentionCleanupCommand))]
    private bool _retentionCleanupDeleteArchivedTenantDirectories = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRetentionCleanupCommand))]
    private bool _retentionCleanupDeleteBrokenLegacyDbFiles = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteRetentionCleanupCommand))]
    private bool _retentionCleanupIsExecuting;

    [ObservableProperty] private string _retentionCleanupExecutionOutcome       = "";
    [ObservableProperty] private bool   _retentionCleanupExecuted;
    [ObservableProperty] private string _retentionCleanupExecutionStatusMessage = "";
    [ObservableProperty] private System.DateTime? _retentionCleanupExecutionStartedAtUtc;
    [ObservableProperty] private System.DateTime? _retentionCleanupExecutionCompletedAtUtc;

    [ObservableProperty] private int    _retentionCleanupCandidateCountBefore;
    [ObservableProperty] private string _retentionCleanupCandidateSizeBefore = "0 B";
    [ObservableProperty] private int    _retentionCleanupDeletedItemCount;
    [ObservableProperty] private string _retentionCleanupDeletedSize         = "0 B";
    [ObservableProperty] private int    _retentionCleanupSkippedItemCount;
    [ObservableProperty] private int    _retentionCleanupFailedItemCount;

    public ObservableCollection<string> RetentionCleanupExecutionSteps           { get; } = new();
    public ObservableCollection<string> RetentionCleanupExecutionBlockingReasons { get; } = new();
    public ObservableCollection<string> RetentionCleanupExecutionWarnings        { get; } = new();
    public ObservableCollection<string> RetentionCleanupExecutionErrors          { get; } = new();
    public ObservableCollection<string> RetentionCleanupItemLines                { get; } = new();

    // ── Production Pilot Readiness (Phase 10.17A) ───────────────────────────

    [ObservableProperty] private bool   _pilotReadinessIsGenerating;
    [ObservableProperty] private string _pilotReadinessOverallStatus  = "";
    [ObservableProperty] private string _pilotReadinessStatusMessage  = "";
    [ObservableProperty] private string _pilotReadinessExportPath     = "";
    [ObservableProperty] private System.DateTime? _pilotReadinessGeneratedAtUtc;

    public ObservableCollection<string> PilotReadinessChecks              { get; } = new();
    public ObservableCollection<string> PilotReadinessBlockingReasons     { get; } = new();
    public ObservableCollection<string> PilotReadinessWarnings            { get; } = new();
    public ObservableCollection<string> PilotReadinessRecommendedNextSteps { get; } = new();

    // ── Production Pilot Evidence Bundle (Phase 10.17B) ─────────────────────

    [ObservableProperty] private bool   _pilotEvidenceIsExporting;
    [ObservableProperty] private string _pilotEvidenceOutcome         = "";
    [ObservableProperty] private string _pilotEvidenceStatusMessage   = "";
    [ObservableProperty] private string _pilotEvidenceBundleDirectory = "";
    [ObservableProperty] private string _pilotEvidenceBundleZipPath   = "";
    [ObservableProperty] private string _pilotEvidenceManifestPath    = "";
    [ObservableProperty] private System.DateTime? _pilotEvidenceStartedAtUtc;
    [ObservableProperty] private System.DateTime? _pilotEvidenceCompletedAtUtc;

    public ObservableCollection<string> PilotEvidenceIncludedFiles { get; } = new();
    public ObservableCollection<string> PilotEvidenceWarnings      { get; } = new();
    public ObservableCollection<string> PilotEvidenceErrors        { get; } = new();
    public ObservableCollection<string> PilotEvidenceSteps         { get; } = new();

    // ── Backend Operator Permissions (Phase 10.19C — display-only) ──────────

    [ObservableProperty] private bool   _backendOperatorPermissionsIsLoading;
    [ObservableProperty] private string _backendOperatorPermissionsStatus      = "Not loaded.";
    [ObservableProperty] private string _backendOperatorPermissionsSource      = "";
    [ObservableProperty] private System.DateTime? _backendOperatorPermissionsGeneratedAt;
    [ObservableProperty] private System.DateTime? _backendOperatorPermissionsExpiresAt;

    [ObservableProperty] private string _backendOperatorUserId   = "";
    [ObservableProperty] private string _backendOperatorUsername = "";
    [ObservableProperty] private string _backendOperatorRole     = "";
    [ObservableProperty] private string _backendOperatorTenantId = "";
    [ObservableProperty] private string _backendOperatorStoreId  = "";
    [ObservableProperty] private bool   _backendOperatorAuthenticated;

    [ObservableProperty] private int _backendOperatorPermissionCount;
    [ObservableProperty] private int _backendOperatorDangerousPermissionCount;
    [ObservableProperty] private int _backendOperatorReadOnlyPermissionCount;

    public ObservableCollection<string> BackendOperatorPermissions             { get; } = new();
    public ObservableCollection<string> BackendOperatorDangerousPermissions    { get; } = new();
    public ObservableCollection<string> BackendOperatorReadOnlyPermissions     { get; } = new();
    public ObservableCollection<string> BackendOperatorPermissionWarnings      { get; } = new();
    public ObservableCollection<string> BackendOperatorPermissionErrors        { get; } = new();

    // ── Backend permission preflight enforcement (Phase 10.19D) ────────────

    private const string BackendEnforcementFlagKey = "operator_backend_permission_enforcement_enabled";

    [ObservableProperty] private bool   _backendPermissionEnforcementEnabled;
    [ObservableProperty] private string _backendPermissionEnforcementStatusText = "Disabled (operator_backend_permission_enforcement_enabled missing or \"0\").";

    [ObservableProperty] private string _backendPermissionLastPreflightOperation = "";
    [ObservableProperty] private string _backendPermissionLastPreflightKey       = "";
    [ObservableProperty] private string _backendPermissionLastPreflightStatus    = "";
    [ObservableProperty] private string _backendPermissionLastPreflightReason    = "";
    [ObservableProperty] private System.DateTime? _backendPermissionLastPreflightAtUtc;

    // ── Backend audit-intent / evidence-registration (Phase 10.19G) ─────────
    //
    // Display-only. Two flags. Default OFF. Non-blocking on failure.
    // No confirmation phrase / token / raw content ever crosses the wire.

    [ObservableProperty] private bool   _backendAuditIntentEnabled;
    [ObservableProperty] private string _backendAuditIntentFlagStatusText             = "Disabled (operator_backend_audit_intent_enabled missing or \"0\").";

    [ObservableProperty] private string _backendAuditIntentLastOperation              = "";
    [ObservableProperty] private string _backendAuditIntentLastPermissionKey          = "";
    [ObservableProperty] private string _backendAuditIntentLastStatus                 = "";
    [ObservableProperty] private string _backendAuditIntentLastIntentId               = "";
    [ObservableProperty] private string _backendAuditIntentLastReason                 = "";
    [ObservableProperty] private System.DateTime? _backendAuditIntentLastAtUtc;

    [ObservableProperty] private bool   _backendEvidenceRegistrationEnabled;
    [ObservableProperty] private string _backendEvidenceRegistrationFlagStatusText    = "Disabled (operator_backend_evidence_registration_enabled missing or \"0\").";

    [ObservableProperty] private string _backendEvidenceRegistrationLastStatus        = "";
    [ObservableProperty] private string _backendEvidenceRegistrationLastRegistrationId = "";
    [ObservableProperty] private string _backendEvidenceRegistrationLastBundleId      = "";
    [ObservableProperty] private string _backendEvidenceRegistrationLastReason        = "";
    [ObservableProperty] private System.DateTime? _backendEvidenceRegistrationLastAtUtc;

    // ── Backend operator audit/evidence review UI (Phase 10.19J) ────────────
    //
    // Display-only, default OFF, read-only. Does NOT mutate audit records,
    // does NOT execute any maintenance operation, does NOT change CanExecute
    // gating for any dangerous command, does NOT take the dangerous-operation
    // lock.

    [ObservableProperty] private bool   _backendAuditReviewUiEnabled;
    [ObservableProperty] private string _backendAuditReviewUiStatusText = "Disabled (operator_backend_audit_review_ui_enabled missing or \"0\").";

    [ObservableProperty] private bool   _backendAuditReviewIsLoading;
    [ObservableProperty] private string _backendAuditReviewStatusMessage = "";

    // Filters bound to the UI inputs.
    [ObservableProperty] private string _backendAuditReviewFilterTenantId       = "";
    [ObservableProperty] private string _backendAuditReviewFilterEntityType     = "";
    [ObservableProperty] private string _backendAuditReviewFilterAction         = "";
    [ObservableProperty] private string _backendAuditReviewFilterOperationName  = "";
    [ObservableProperty] private string _backendAuditReviewFilterPermissionKey  = "";
    [ObservableProperty] private bool?  _backendAuditReviewFilterAccepted;

    // Pagination state.
    [ObservableProperty] private int    _backendAuditReviewPage           = 0;
    [ObservableProperty] private int    _backendAuditReviewSize           = 50;
    [ObservableProperty] private long   _backendAuditReviewTotalElements;
    [ObservableProperty] private bool   _backendAuditReviewHasNext;

    // Single-row lookup inputs.
    [ObservableProperty] private string _backendAuditReviewLookupEventId        = "";
    [ObservableProperty] private string _backendAuditReviewLookupIntentId       = "";
    [ObservableProperty] private string _backendAuditReviewLookupRegistrationId = "";

    // Selected-event display (populated by lookups OR by clicking a list row).
    [ObservableProperty] private string _backendAuditReviewSelectedEventId       = "";
    [ObservableProperty] private string _backendAuditReviewSelectedAction        = "";
    [ObservableProperty] private string _backendAuditReviewSelectedEntityType    = "";
    [ObservableProperty] private string _backendAuditReviewSelectedEntityId      = "";
    [ObservableProperty] private string _backendAuditReviewSelectedTenantId      = "";
    [ObservableProperty] private string _backendAuditReviewSelectedUsername      = "";
    [ObservableProperty] private string _backendAuditReviewSelectedRole          = "";
    [ObservableProperty] private string _backendAuditReviewSelectedOperationName = "";
    [ObservableProperty] private string _backendAuditReviewSelectedPermissionKey = "";
    [ObservableProperty] private string _backendAuditReviewSelectedAccepted      = "";
    [ObservableProperty] private string _backendAuditReviewSelectedAuditSource   = "";
    [ObservableProperty] private string _backendAuditReviewSelectedRedacted      = "";
    [ObservableProperty] private string _backendAuditReviewSelectedReviewSource  = "";
    [ObservableProperty] private string _backendAuditReviewSelectedCreatedAt     = "";

    public ObservableCollection<string> BackendAuditReviewEvents          { get; } = new();
    public ObservableCollection<string> BackendAuditReviewSelectedMetadata { get; } = new();
    public ObservableCollection<string> BackendAuditReviewWarnings        { get; } = new();
    public ObservableCollection<string> BackendAuditReviewErrors          { get; } = new();

    // ── Operator Permission Admin (read-only) UI (Phase 10.20G) ─────────────
    //
    // Display-only, default OFF, read-only. Does NOT mutate any audit /
    // permission row, does NOT call any guarded executor, does NOT take the
    // dangerous-operation lock, does NOT change `CanExecute` of any
    // dangerous command.

    [ObservableProperty] private bool   _permissionAdminReadOnlyUiEnabled;
    [ObservableProperty] private string _permissionAdminReadOnlyUiStatusText = "Disabled (operator_permission_admin_readonly_ui_enabled missing or \"0\").";

    [ObservableProperty] private bool   _permissionAdminIsLoading;
    [ObservableProperty] private string _permissionAdminStatusMessage = "";

    // Definitions filters + state.
    [ObservableProperty] private string _permissionDefinitionFilterKey       = "";
    [ObservableProperty] private string _permissionDefinitionFilterCategory  = "";
    [ObservableProperty] private bool?  _permissionDefinitionFilterActive;
    [ObservableProperty] private bool?  _permissionDefinitionFilterDangerous;
    [ObservableProperty] private int    _permissionDefinitionPage           = 0;
    [ObservableProperty] private int    _permissionDefinitionSize           = 50;
    [ObservableProperty] private long   _permissionDefinitionTotalElements;
    [ObservableProperty] private bool   _permissionDefinitionHasNext;

    // Role grant filters + state.
    [ObservableProperty] private string _roleGrantFilterRole              = "";
    [ObservableProperty] private string _roleGrantFilterPermissionKey     = "";
    [ObservableProperty] private string _roleGrantFilterTenantScopePolicy = "";
    [ObservableProperty] private bool?  _roleGrantFilterActive;
    [ObservableProperty] private int    _roleGrantPage = 0;
    [ObservableProperty] private int    _roleGrantSize = 50;
    [ObservableProperty] private long   _roleGrantTotalElements;
    [ObservableProperty] private bool   _roleGrantHasNext;

    // User override filters + state.
    [ObservableProperty] private string _userOverrideFilterUserId        = "";
    [ObservableProperty] private string _userOverrideFilterTenantId      = "";
    [ObservableProperty] private string _userOverrideFilterStoreId       = "";
    [ObservableProperty] private string _userOverrideFilterPermissionKey = "";
    [ObservableProperty] private string _userOverrideFilterGrantType     = "";
    [ObservableProperty] private bool?  _userOverrideFilterActive;
    [ObservableProperty] private bool?  _userOverrideFilterExpired;
    [ObservableProperty] private int    _userOverridePage = 0;
    [ObservableProperty] private int    _userOverrideSize = 50;
    [ObservableProperty] private long   _userOverrideTotalElements;
    [ObservableProperty] private bool   _userOverrideHasNext;

    // Effective query + state.
    [ObservableProperty] private string _effectiveQueryUserId   = "";
    [ObservableProperty] private string _effectiveQueryTenantId = "";
    [ObservableProperty] private string _effectiveQueryStoreId  = "";
    [ObservableProperty] private string _effectiveAuditSource           = "";
    [ObservableProperty] private string _effectiveEnabled               = "";
    [ObservableProperty] private string _effectiveHealthy               = "";
    [ObservableProperty] private string _effectivePermissionsSource     = "";
    [ObservableProperty] private string _effectiveComparisonMatchesCode = "";

    public ObservableCollection<string> PermissionDefinitions     { get; } = new();
    public ObservableCollection<string> RoleGrants                { get; } = new();
    public ObservableCollection<string> UserOverrides             { get; } = new();
    public ObservableCollection<string> EffectivePermissions      { get; } = new();
    public ObservableCollection<string> EffectiveDecisions        { get; } = new();
    public ObservableCollection<string> PermissionAdminWarnings   { get; } = new();
    public ObservableCollection<string> PermissionAdminErrors     { get; } = new();

    // ── Operator Permission Admin Mutation UI (Phase 10.20I) ────────────────
    //
    // Default OFF locally. Backend has its own default-OFF flag — both
    // must be ON for a mutation to commit. Does NOT touch the dangerous-
    // operation lock; does NOT call any guarded executor; does NOT
    // change CanExecute of any dangerous command.

    [ObservableProperty] private bool   _permissionAdminMutationUiEnabled;
    [ObservableProperty] private string _permissionAdminMutationUiStatusText = "Disabled (operator_permission_admin_mutation_ui_enabled missing or \"0\").";

    [ObservableProperty] private bool   _permissionAdminMutationIsBusy;
    [ObservableProperty] private string _permissionAdminMutationStatusMessage = "";

    // Create user override form.
    [ObservableProperty] private string _createOverrideUserId          = "";
    [ObservableProperty] private string _createOverrideTenantId        = "";
    [ObservableProperty] private string _createOverrideStoreId         = "";
    [ObservableProperty] private string _createOverridePermissionKey   = "";
    [ObservableProperty] private string _createOverrideGrantType       = ""; // ALLOW | DENY
    [ObservableProperty] private string _createOverrideExpiresAt       = ""; // ISO-8601 text (parsed before send)
    [ObservableProperty] private string _createOverrideReason          = "";
    [ObservableProperty] private string _createOverrideApprovalTicketId= "";

    // Revoke user override form.
    [ObservableProperty] private string _revokeOverrideId            = "";
    [ObservableProperty] private string _revokeOverrideReason        = "";
    [ObservableProperty] private string _revokeOverrideApprovalTicketId = "";

    // Create role grant form.
    [ObservableProperty] private string _createRoleGrantRole             = "";
    [ObservableProperty] private string _createRoleGrantPermissionKey    = "";
    [ObservableProperty] private string _createRoleGrantTenantScopePolicy= "";
    [ObservableProperty] private string _createRoleGrantReason           = "";
    [ObservableProperty] private string _createRoleGrantApprovalTicketId = "";

    // Revoke role grant form.
    [ObservableProperty] private string _revokeRoleGrantId             = "";
    [ObservableProperty] private string _revokeRoleGrantReason         = "";
    [ObservableProperty] private string _revokeRoleGrantApprovalTicketId = "";

    // Last result display.
    [ObservableProperty] private string _permissionAdminLastMutationStatus      = "";
    [ObservableProperty] private string _permissionAdminLastMutationAuditSource = "";
    [ObservableProperty] private string _permissionAdminLastMutationMessage     = "";
    [ObservableProperty] private string _permissionAdminLastMutationItemSummary = "";
    [ObservableProperty] private System.DateTime? _permissionAdminLastMutationAtUtc;

    public ObservableCollection<string> PermissionAdminMutationWarnings { get; } = new();
    public ObservableCollection<string> PermissionAdminMutationErrors   { get; } = new();

    // ── Operator Permission Authoritative Status (read-only) UI (Phase 10.21G)
    //
    // Read-only diagnostic card. Default OFF locally. The backend endpoint
    // it consumes is also read-only and does NOT change any permission
    // decision. The card surfaces:
    //   - current backend authoritative-mode flag values
    //   - parity gate health
    //   - dangerous preflight health
    //   - readiness booleans
    //   - blocker / warning / info counters
    //   - issue list + risk list + flag list
    //
    // No mutation. No execution. No dangerous-operation lock. No phrase
    // field. No upload control. The Refresh command short-circuits when
    // the local flag is OFF.

    [ObservableProperty] private bool   _permissionAuthoritativeStatusUiEnabled;
    [ObservableProperty] private string _permissionAuthoritativeStatusUiStatusText =
            "Disabled (operator_permission_authoritative_status_ui_enabled missing or \"0\").";

    [ObservableProperty] private bool   _permissionAuthoritativeStatusIsLoading;
    [ObservableProperty] private string _permissionAuthoritativeStatusMessage = "";

    [ObservableProperty] private string _permissionAuthoritativePermissionsSource            = "";
    [ObservableProperty] private string _permissionAuthoritativeReadOnlyEnabled              = "";
    [ObservableProperty] private string _permissionAuthoritativeDangerousPreflightEnabled    = "";
    [ObservableProperty] private string _permissionAuthoritativeDangerousEnabled             = "";
    [ObservableProperty] private string _permissionAuthoritativeFailOnMismatch               = "";
    [ObservableProperty] private string _permissionAuthoritativeAllowCodeFallbackReadOnly    = "";
    [ObservableProperty] private string _permissionAuthoritativeParityHealthy                = "";
    [ObservableProperty] private string _permissionAuthoritativeDangerousPreflightHealthy    = "";
    [ObservableProperty] private string _permissionAuthoritativeReadyForReadOnly             = "";
    [ObservableProperty] private string _permissionAuthoritativeReadyForDangerous            = "";
    [ObservableProperty] private string _permissionAuthoritativeBlockerCount                 = "";
    [ObservableProperty] private string _permissionAuthoritativeWarningCount                 = "";
    [ObservableProperty] private string _permissionAuthoritativeInfoCount                    = "";
    [ObservableProperty] private System.DateTime? _permissionAuthoritativeGeneratedAt;

    public ObservableCollection<string> PermissionAuthoritativeFlags     { get; } = new();
    public ObservableCollection<string> PermissionAuthoritativeReadiness { get; } = new();
    public ObservableCollection<string> PermissionAuthoritativeRisks     { get; } = new();
    public ObservableCollection<string> PermissionAuthoritativeIssues    { get; } = new();
    public ObservableCollection<string> PermissionAuthoritativeErrors    { get; } = new();
    public ObservableCollection<string> PermissionAuthoritativeWarnings  { get; } = new();

    // ── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private async System.Threading.Tasks.Task RefreshAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusMessage = "Yangilanmoqda...";

        // Phase 10.12C / 10.13B / 10.14B / 10.16B — pick up flag/role changes that happened after open.
        RecalculateRealMigrationUiEnabled();
        RecalculateRuntimeCutoverUiEnabled();
        RecalculateRollbackUiEnabled();
        RecalculateRetentionCleanupUiEnabled();

        // Phase 10.19G — refresh read-only display of the new backend audit /
        // evidence flag states (default OFF). Reads from GlobalSettingsRepository
        // only; never issues HTTP.
        RefreshBackendAuditEvidenceFlagStatusTexts();

        try
        {
            var tenant = string.IsNullOrWhiteSpace(TenantSubdomainInput)
                ? _global.Get("last_tenant_subdomain")
                : TenantSubdomainInput.Trim();

            // 1. Diagnostics report (read-only).
            OperatorDiagnosticsReport? diagReport = null;
            try
            {
                diagReport = await _diagnostics.GetReportAsync(tenant);
            }
            catch (System.Exception ex)
            {
                Errors.Add($"Diagnostics failed: {ex.Message}");
            }

            if (diagReport is not null)
            {
                RuntimeTenantDbEnabled    = diagReport.RuntimeTenantDbEnabled;
                MigrationFeatureEnabled   = diagReport.MigrationFeatureEnabled;
                SharedToTenantMigrated    = diagReport.SharedToTenantMigrated;
                SharedToTenantMigratedAt  = diagReport.SharedToTenantMigratedAt ?? "";
                ActiveDbPath              = diagReport.ActiveDbPath;
                IsTenantScoped            = diagReport.IsTenantScoped;
                LegacyDbPath              = diagReport.LegacyDbPath;
                LastTenantSubdomain       = diagReport.LastTenantSubdomain ?? "";
                PendingSalesCount         = diagReport.Sales.PendingSalesCount;
                PoisonSalesCount          = diagReport.Sales.PoisonSalesCount;
                ProductsCount             = diagReport.Cache.ProductsCount;
                CustomersCount            = diagReport.Cache.CustomersCount;
            }

            // 2. Migration audit (read-only).
            try
            {
                var auditReport = await _auditor.AnalyzeAsync();
                MigrationAuditSummary = ComposeAuditSummary(auditReport);
            }
            catch (System.Exception ex)
            {
                MigrationAuditSummary = $"(audit failed: {ex.Message})";
                Errors.Add($"Audit failed: {ex.Message}");
            }

            // 3. Verifier (read-only).
            try
            {
                var verifyReport = await _verifier.VerifyAsync();
                VerificationSummary = ComposeVerificationSummary(verifyReport);
            }
            catch (System.Exception ex)
            {
                VerificationSummary = $"(verification failed: {ex.Message})";
                Errors.Add($"Verification failed: {ex.Message}");
            }

            // 4. Cutover readiness (read-only, tenant-scoped).
            if (!string.IsNullOrWhiteSpace(tenant))
            {
                try
                {
                    var cutover = await _cutoverGate.CheckAsync(tenant);
                    CutoverStatus = cutover.Status.ToString();
                }
                catch (System.Exception ex)
                {
                    CutoverStatus = "(failed)";
                    Errors.Add($"Cutover check failed: {ex.Message}");
                }
            }
            else
            {
                CutoverStatus = "(no tenant resolved)";
            }

            // 5. Rollback readiness (read-only, tenant-agnostic).
            try
            {
                var rollback = _rollbackChecker.Check();
                RollbackStatus = rollback.Status.ToString();
            }
            catch (System.Exception ex)
            {
                RollbackStatus = "(failed)";
                Errors.Add($"Rollback check failed: {ex.Message}");
            }

            // 6. Pull warnings/errors from the diagnostics report (it already
            //    composes the standard set in Phase 10.7A).
            if (diagReport is not null)
            {
                Warnings.Clear();
                foreach (var w in diagReport.Warnings) Warnings.Add(w);
                foreach (var e in diagReport.Errors)   Errors.Add(e);
            }

            LastUpdatedAt = System.DateTime.UtcNow.ToLocalTime();
            StatusMessage = "Tayyor";
        }
        catch (System.Exception ex)
        {
            Errors.Add($"Refresh failed: {ex.Message}");
            StatusMessage = "Xato";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task ExportDiagnosticsAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusMessage = "Eksport qilinmoqda...";

        try
        {
            var tenant = string.IsNullOrWhiteSpace(TenantSubdomainInput) ? null : TenantSubdomainInput.Trim();
            var result = await _exporter.ExportAsync(new OperatorDiagnosticsExportOptions
            {
                TenantSubdomain    = tenant,
                IncludeMachineInfo = true,
            });

            if (result.Success && result.FilePath is not null)
            {
                LastExportPath = result.FilePath;
                StatusMessage  = "Eksport tayyor";
                foreach (var w in result.Warnings) Warnings.Add(w);
            }
            else
            {
                StatusMessage = "Eksport xatosi";
                foreach (var e in result.Errors)   Errors.Add(e);
                foreach (var w in result.Warnings) Warnings.Add(w);
            }
        }
        catch (System.Exception ex)
        {
            Errors.Add($"Diagnostics export failed: {ex.Message}");
            StatusMessage = "Xato";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Migration dry-run preview command ────────────────────────────────────

    [RelayCommand]
    private async System.Threading.Tasks.Task PreviewMigrationDryRunAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusMessage = "Migration dry-run preview...";

        try
        {
            // The wrapper service is the only caller path here — it hardcodes
            // DryRunOnly=true, Force=false, AllowWhenFeatureDisabled=false,
            // WriteAuditLog=false. ViewModel cannot override these.
            var preview = await _dryRunPreview.PreviewAsync();

            MigrationDryRunOutcome     = preview.Outcome;
            MigrationDryRunAvailable   = preview.IsAvailable;
            MigrationDryRunTenantCount = preview.TenantCount;
            MigrationDryRunWarningCount = preview.WarningCount;
            MigrationDryRunErrorCount  = preview.ErrorCount;

            MigrationDryRunDetails.Clear();
            foreach (var d in preview.Details) MigrationDryRunDetails.Add(d);

            // Side-effect guard (Phase 10.10A.1).
            MigrationDryRunSideEffectCheckPassed     = preview.SideEffectCheckPassed;
            MigrationDryRunSideEffectDifferenceCount = preview.SideEffectDifferenceCount;
            MigrationDryRunSideEffectDifferences.Clear();
            foreach (var diff in preview.SideEffectDifferences)
                MigrationDryRunSideEffectDifferences.Add(diff);

            // Summary line for at-a-glance display.
            var sideEffectLine = preview.SideEffectCheckPassed
                ? "side-effects=Passed"
                : $"side-effects=Failed ({preview.SideEffectDifferenceCount})";
            MigrationDryRunSummary =
                $"Outcome: {preview.Outcome}; tenants={preview.TenantCount}; " +
                $"warnings={preview.WarningCount}; errors={preview.ErrorCount}; " +
                $"{sideEffectLine}";

            foreach (var w in preview.Warnings) Warnings.Add(w);
            foreach (var e in preview.Errors)   Errors.Add(e);

            // Surface side-effect failures separately into the dashboard's
            // Errors collection so they show up red. Do not silently swallow.
            if (!preview.SideEffectCheckPassed)
            {
                foreach (var diff in preview.SideEffectDifferences)
                    Errors.Add($"Dry-run side effect: {diff}");
            }

            StatusMessage = "Tayyor";
        }
        catch (System.Exception ex)
        {
            MigrationDryRunOutcome = "Failed";
            MigrationDryRunSummary = $"Preview failed: {ex.Message}";
            Errors.Add($"Migration dry-run preview failed: {ex.Message}");
            StatusMessage = "Xato";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Rollback dry-run preview command (Phase 10.10B) ─────────────────────

    [RelayCommand]
    private async System.Threading.Tasks.Task PreviewRollbackDryRunAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusMessage = "Rollback dry-run preview...";

        try
        {
            // The wrapper service is the only caller path here — it hardcodes
            // DryRunOnly=true, Force=false, ConfirmationPhrase=null,
            // DisableRuntimeFlag=true, ArchiveTenantsDirectory=true,
            // RestoreLegacyFromBackupIfMissing=false, WriteAuditLog=false.
            // ViewModel cannot override these.
            var preview = await _rollbackPreview.PreviewAsync();

            RollbackDryRunOutcome                      = preview.Outcome;
            RollbackDryRunAvailable                    = preview.IsAvailable;
            RollbackDryRunReadinessStatus              = preview.ReadinessStatus ?? "";
            RollbackDryRunRuntimeTenantDbEnabled       = preview.RuntimeTenantDbEnabled;
            RollbackDryRunIsProviderTenantScoped       = preview.IsProviderTenantScoped;
            RollbackDryRunLegacyDbPath                 = preview.LegacyDbPath ?? "";
            RollbackDryRunLegacyDbExists               = preview.LegacyDbExists;
            RollbackDryRunLegacyDbReadable             = preview.LegacyDbReadable;
            RollbackDryRunTenantsDirectoryPath         = preview.TenantsDirectoryPath ?? "";
            RollbackDryRunPlannedArchivePath           = preview.PlannedTenantsArchivePath ?? "";
            RollbackDryRunWouldDisableRuntimeFlag      = preview.WouldDisableRuntimeFlag;
            RollbackDryRunWouldArchiveTenantsDir       = preview.WouldArchiveTenantsDirectory;
            RollbackDryRunWouldRestoreLegacyFromBackup = preview.WouldRestoreLegacyFromBackup;
            RollbackDryRunSideEffectCheckPassed        = preview.SideEffectCheckPassed;
            RollbackDryRunSideEffectDifferenceCount    = preview.SideEffectDifferenceCount;

            RollbackDryRunPlannedSteps.Clear();
            foreach (var s in preview.PlannedSteps) RollbackDryRunPlannedSteps.Add(s);

            RollbackDryRunSideEffectDifferences.Clear();
            foreach (var diff in preview.SideEffectDifferences)
                RollbackDryRunSideEffectDifferences.Add(diff);

            var sideEffectLine = preview.SideEffectCheckPassed
                ? "side-effects=Passed"
                : $"side-effects=Failed ({preview.SideEffectDifferenceCount})";
            RollbackDryRunSummary =
                $"Outcome: {preview.Outcome}; readiness={preview.ReadinessStatus ?? "n/a"}; " +
                $"runtime={preview.RuntimeTenantDbEnabled}; " +
                $"{sideEffectLine}";

            foreach (var w in preview.Warnings) Warnings.Add(w);
            foreach (var e in preview.Errors)   Errors.Add(e);

            // Surface side-effect failures into the dashboard's Errors collection
            // so they show up red. Do not silently swallow.
            if (!preview.SideEffectCheckPassed)
            {
                foreach (var diff in preview.SideEffectDifferences)
                    Errors.Add($"Rollback dry-run side effect: {diff}");
            }

            StatusMessage = "Tayyor";
        }
        catch (System.Exception ex)
        {
            RollbackDryRunOutcome = "Failed";
            RollbackDryRunSummary = $"Preview failed: {ex.Message}";
            Errors.Add($"Rollback dry-run preview failed: {ex.Message}");
            StatusMessage = "Xato";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Preflight export command (Phase 10.10C) ─────────────────────────────

    [RelayCommand]
    private async System.Threading.Tasks.Task ExportPreflightReportAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusMessage = "Preflight export...";
        PreflightStatusMessage = "Eksport qilinmoqda...";

        try
        {
            var tenant = string.IsNullOrWhiteSpace(TenantSubdomainInput) ? null : TenantSubdomainInput.Trim();
            var result = await _preflightExport.ExportAsync(new MigrationOperationsPreflightExportOptions
            {
                TenantSubdomain    = tenant,
                IncludeMachineInfo = true,
            });

            if (result.Success && result.FilePath is not null)
            {
                LastPreflightExportPath = result.FilePath;
                PreflightStatusMessage  = $"Tayyor — {result.FilePath}";
                StatusMessage           = "Tayyor";
                foreach (var w in result.Warnings) Warnings.Add(w);
                foreach (var e in result.Errors)   Errors.Add(e);
            }
            else
            {
                PreflightStatusMessage = "Eksport xatosi";
                StatusMessage          = "Xato";
                foreach (var w in result.Warnings) Warnings.Add(w);
                foreach (var e in result.Errors)   Errors.Add(e);
            }
        }
        catch (System.Exception ex)
        {
            Errors.Add($"Preflight export failed: {ex.Message}");
            PreflightStatusMessage = "Xato";
            StatusMessage = "Xato";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Inventory refresh command (Phase 10.11A) ────────────────────────────

    [RelayCommand]
    private async System.Threading.Tasks.Task RefreshInventoryAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusMessage = "Inventory yangilanmoqda...";

        try
        {
            var report = await _inventory.GetInventoryAsync();

            InventoryLegacyDbPath           = report.LegacyDbPath;
            InventoryLegacyDbExists         = report.LegacyDb?.Exists ?? false;
            InventoryLegacyDbSize           = report.LegacyDb?.SizeText ?? "n/a";
            InventoryTotalKnownSize         = report.TotalKnownSizeText;
            InventoryTenantDbCount          = report.TenantDatabases.Count;
            InventoryBackupCount            = report.BackupFiles.Count;
            InventoryArchivedTenantDirCount = report.ArchivedTenantDirectories.Count;
            InventoryBrokenLegacyDbCount    = report.BrokenLegacyDbFiles.Count;
            InventoryMigrationLogCount      = report.MigrationLogs.FileCount;
            InventoryRollbackLogCount       = report.RollbackLogs.FileCount;
            InventoryDiagnosticsLogCount    = report.DiagnosticsLogs.FileCount;
            InventoryPreflightLogCount      = report.PreflightLogs.FileCount;

            InventoryTenantDbLines.Clear();
            foreach (var t in report.TenantDatabases)
                InventoryTenantDbLines.Add(
                    $"{t.TenantSubdomain} → {t.DbPath} (exists={t.DbExists}, size={t.SizeText}" +
                    (t.LastWriteTimeUtc is null ? "" : $", mtime={t.LastWriteTimeUtc:o}") + ")");

            InventoryBackupLines.Clear();
            foreach (var b in report.BackupFiles)
                InventoryBackupLines.Add(
                    $"{b.Name} (size={b.SizeText}" +
                    (b.LastWriteTimeUtc is null ? "" : $", mtime={b.LastWriteTimeUtc:o}") + ")");

            InventoryArchiveLines.Clear();
            foreach (var a in report.ArchivedTenantDirectories)
                InventoryArchiveLines.Add(
                    $"{a.Name} (size={a.SizeText}, files={a.FileCount}" +
                    (a.LastWriteTimeUtc is null ? "" : $", mtime={a.LastWriteTimeUtc:o}") + ")");

            InventoryLogLines.Clear();
            InventoryLogLines.Add(FormatLogLine("migrations",  report.MigrationLogs));
            InventoryLogLines.Add(FormatLogLine("rollbacks",   report.RollbackLogs));
            InventoryLogLines.Add(FormatLogLine("diagnostics", report.DiagnosticsLogs));
            InventoryLogLines.Add(FormatLogLine("preflight",   report.PreflightLogs));

            InventorySummary =
                $"Total known size: {report.TotalKnownSizeText}; " +
                $"tenants={report.TenantDatabases.Count}; " +
                $"backups={report.BackupFiles.Count}; " +
                $"archives={report.ArchivedTenantDirectories.Count}; " +
                $"broken-legacy={report.BrokenLegacyDbFiles.Count}; " +
                $"logs=mig:{report.MigrationLogs.FileCount}/" +
                $"rb:{report.RollbackLogs.FileCount}/" +
                $"diag:{report.DiagnosticsLogs.FileCount}/" +
                $"pf:{report.PreflightLogs.FileCount}";

            foreach (var w in report.Warnings) Warnings.Add($"Inventory: {w}");
            foreach (var e in report.Errors)   Errors.Add($"Inventory: {e}");

            StatusMessage = "Tayyor";
        }
        catch (System.Exception ex)
        {
            Errors.Add($"Inventory refresh failed: {ex.Message}");
            StatusMessage = "Xato";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string FormatLogLine(string label, InventoryLogSummary s)
        => $"{label}/ — exists={s.Exists}, files={s.FileCount}, size={s.SizeText}" +
           (s.NewestFileUtc is null ? "" : $", newest={s.NewestFileUtc:o}");

    // ── Retention preview command (Phase 10.11B) ────────────────────────────

    [RelayCommand]
    private async System.Threading.Tasks.Task PreviewRetentionPlanAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusMessage = "Retention preview...";

        try
        {
            var report = await _retentionPreview.PreviewAsync();

            RetentionPreviewSummary             = report.Summary;
            RetentionPreviewCandidateCount      = report.CandidateCount;
            RetentionPreviewCandidateSize       = report.CandidateSizeText;
            RetentionPreviewProtectedItemCount  = report.ProtectedItemCount;
            RetentionPreviewProtectedSize       = report.ProtectedSizeText;

            RetentionPreviewCandidateLines.Clear();
            foreach (var c in report.Candidates)
                RetentionPreviewCandidateLines.Add(FormatRetentionItem(c));

            RetentionPreviewProtectedLines.Clear();
            foreach (var p in report.ProtectedItems)
                RetentionPreviewProtectedLines.Add(FormatRetentionItem(p));

            foreach (var w in report.Warnings) Warnings.Add($"Retention: {w}");
            foreach (var e in report.Errors)   Errors.Add($"Retention: {e}");

            StatusMessage = "Tayyor";
        }
        catch (System.Exception ex)
        {
            Errors.Add($"Retention preview failed: {ex.Message}");
            StatusMessage = "Xato";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string FormatRetentionItem(RetentionPreviewItem i)
        => $"[{i.Category}] {i.Name} (size={i.SizeText}" +
           (i.AgeDays is null ? "" : $", age={i.AgeDays}d") +
           (i.LastWriteTimeUtc is null ? "" : $", mtime={i.LastWriteTimeUtc:o}") +
           $") — {i.Reason}";

    // ── Inventory export command (Phase 10.11C) ─────────────────────────────

    [RelayCommand]
    private async System.Threading.Tasks.Task ExportInventoryReportAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusMessage = "Inventory export...";
        InventoryExportStatusMessage = "Eksport qilinmoqda...";

        try
        {
            var result = await _inventoryExport.ExportAsync(new TenantDatabaseInventoryExportOptions
            {
                IncludeMachineInfo = true,
            });

            if (result.Success && result.FilePath is not null)
            {
                LastInventoryExportPath      = result.FilePath;
                InventoryExportStatusMessage = $"Tayyor — {result.FilePath}";
                StatusMessage                = "Tayyor";
                foreach (var w in result.Warnings) Warnings.Add(w);
                foreach (var e in result.Errors)   Errors.Add(e);
            }
            else
            {
                InventoryExportStatusMessage = "Eksport xatosi";
                StatusMessage                = "Xato";
                foreach (var w in result.Warnings) Warnings.Add(w);
                foreach (var e in result.Errors)   Errors.Add(e);
            }
        }
        catch (System.Exception ex)
        {
            Errors.Add($"Inventory export failed: {ex.Message}");
            InventoryExportStatusMessage = "Xato";
            StatusMessage = "Xato";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Real migration gate check command (Phase 10.12A) ────────────────────

    [RelayCommand]
    private async System.Threading.Tasks.Task CheckRealMigrationGateAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusMessage = "Real migration gate check...";

        try
        {
            var tenant = string.IsNullOrWhiteSpace(TenantSubdomainInput)
                ? null
                : TenantSubdomainInput.Trim();

            var report = await _realMigrationGate.CheckAsync(tenant);

            RealMigrationGateStatus  = report.Status;
            RealMigrationCanExecute  = report.CanExecuteRealMigration;
            RealMigrationGateSummary =
                $"Status: {report.Status}; CanExecute={report.CanExecuteRealMigration}; " +
                $"blockers={report.BlockingReasons.Count}; warnings={report.Warnings.Count}; " +
                $"tenant={report.TenantSubdomain ?? "n/a"}; " +
                $"cutover={report.CutoverStatus ?? "n/a"}; " +
                $"dry-run={report.MigrationDryRunOutcome ?? "n/a"}; " +
                $"pending={report.PendingSalesCount}; poison={report.PoisonSalesCount}";

            RealMigrationBlockingReasons.Clear();
            foreach (var b in report.BlockingReasons) RealMigrationBlockingReasons.Add(b);

            RealMigrationWarnings.Clear();
            foreach (var w in report.Warnings) RealMigrationWarnings.Add(w);

            RealMigrationRecommendedSteps.Clear();
            foreach (var s in report.RecommendedSteps) RealMigrationRecommendedSteps.Add(s);

            // Also surface blocking reasons into the dashboard's red Errors
            // card so they cannot be missed; warnings go to the amber card.
            foreach (var b in report.BlockingReasons) Errors.Add($"Gate blocked: {b}");
            foreach (var w in report.Warnings)        Warnings.Add($"Gate warning: {w}");

            StatusMessage = "Tayyor";
        }
        catch (System.Exception ex)
        {
            RealMigrationGateStatus  = "Blocked";
            RealMigrationCanExecute  = false;
            RealMigrationGateSummary = $"Gate check failed: {ex.Message}";
            Errors.Add($"Real migration gate failed: {ex.Message}");
            StatusMessage = "Xato";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Guarded real migration execution (Phase 10.12C) ─────────────────────

    // Belt-and-suspenders: recompute the section's enabled state from the flag
    // + role check on every refresh and at the moment of execution. The
    // dashboard itself is already flag/role-gated for opening, but the real
    // migration sub-section has its own additional flag.
    private void RecalculateRealMigrationUiEnabled()
    {
        var flagOn = _global.Get(RealMigrationUiFlagKey) == "1";
        var roleOk = _operatorAccess.IsAuthorizedByRole();
        RealMigrationUiEnabled = flagOn && roleOk;
    }

    partial void OnRealMigrationUiEnabledChanged(bool value)
    {
        RealMigrationUiEnabledStatusText = value
            ? "Enabled (operator_real_migration_ui_enabled=1, role authorized)."
            : "Disabled by operator_real_migration_ui_enabled or role check.";
    }

    private bool CanExecuteRealMigration()
        => RealMigrationUiEnabled
           && !RealMigrationIsExecuting
           && !IsDangerousOperationRunning
           && !string.IsNullOrWhiteSpace(RealMigrationReviewedPreflightExportPath)
           && !string.IsNullOrWhiteSpace(RealMigrationReviewedInventoryExportPath)
           && RealMigrationExternalBackupAcknowledged
           && !string.IsNullOrWhiteSpace(RealMigrationConfirmationPhraseInput);

    [RelayCommand(CanExecute = nameof(CanExecuteRealMigration))]
    private System.Threading.Tasks.Task ExecuteRealMigrationAsync()
        => RunDangerousOperationAsync("Real Migration", ExecuteRealMigrationCoreAsync);

    private async System.Threading.Tasks.Task ExecuteRealMigrationCoreAsync(System.Threading.CancellationToken ct)
    {
        if (RealMigrationIsExecuting) return;

        // Re-evaluate gating at the moment of click — flag may have been
        // toggled in global_settings.json since the last refresh, and the
        // role check is cheap.
        RecalculateRealMigrationUiEnabled();
        if (!RealMigrationUiEnabled)
        {
            RealMigrationExecutionStatusMessage = "Real migration UI is disabled.";
            return;
        }

        RealMigrationIsExecuting = true;
        StatusMessage = "Real migration executing...";

        // Reset previous result state.
        RealMigrationExecutionSteps.Clear();
        RealMigrationExecutionBlockingReasons.Clear();
        RealMigrationExecutionWarnings.Clear();
        RealMigrationExecutionErrors.Clear();
        RealMigrationExecutionOutcome        = "";
        RealMigrationExecuted                = false;
        RealMigrationExecutionStartedAtUtc   = null;
        RealMigrationExecutionCompletedAtUtc = null;
        RealMigrationExecutionStatusMessage  = "Executing...";

        // Capture phrase locally, then immediately clear it from the bound
        // property so the textbox is empty for the duration of the call —
        // the wrapper is the only thing that still sees the value.
        var phrase = RealMigrationConfirmationPhraseInput;
        RealMigrationConfirmationPhraseInput = "";

        try
        {
            var tenant = string.IsNullOrWhiteSpace(TenantSubdomainInput)
                ? null
                : TenantSubdomainInput.Trim();

            // Phase 10.19D — backend permission preflight. Defaults OFF; when
            // enabled, fails closed if backend denies / is unavailable.
            var preflight = await ValidateBackendPermissionForDangerousOperationAsync(
                "operator.migration.execute", "execute-real-migration", ct);
            if (!preflight.Allowed)
            {
                phrase = "";
                RealMigrationExecutionOutcome       = GuardedRealMigrationExecutorService.OutcomeRejected;
                RealMigrationExecuted               = false;
                RealMigrationExecutionStatusMessage =
                    $"Rejected by backend permission preflight ({preflight.Status}): {preflight.Reason}";
                RealMigrationExecutionBlockingReasons.Add(
                    $"Backend permission preflight rejected: {preflight.Reason}");
                Errors.Add(
                    $"BackendPermissions: preflight rejected ({preflight.PermissionKey}): {preflight.Reason}");
                StatusMessage = "Tayyor";
                return;
            }

            // Phase 10.19G — non-blocking audit-intent registration. Default
            // OFF. Wrapper execution proceeds regardless of outcome.
            await RecordAuditIntentNonBlockingAsync(
                "operator.migration.execute", "execute-real-migration", tenant, ct);

            var result = await _guardedExecutor.ExecuteAsync(
                new GuardedRealMigrationExecutionOptions
                {
                    TenantSubdomain              = tenant,
                    Force                        = true,
                    ConfirmationPhrase           = phrase,
                    ExternalBackupAcknowledged   = RealMigrationExternalBackupAcknowledged,
                    ExternalBackupNote           = RealMigrationExternalBackupNote,
                    AllowWarnings                = RealMigrationAllowWarnings,
                    ReviewedPreflightExportPath  = RealMigrationReviewedPreflightExportPath,
                    ReviewedInventoryExportPath  = RealMigrationReviewedInventoryExportPath,
                    WriteAuditLog                = true,
                },
                ct);

            // Drop the captured phrase from this local frame as soon as the
            // wrapper returns. It's never serialized or surfaced past this point.
            phrase = "";

            RealMigrationExecutionOutcome        = result.Outcome;
            RealMigrationExecuted                = result.MigrationExecuted;
            RealMigrationExecutionStartedAtUtc   = result.StartedAtUtc;
            RealMigrationExecutionCompletedAtUtc = result.CompletedAtUtc;

            foreach (var s in result.Steps)           RealMigrationExecutionSteps.Add(s);
            foreach (var b in result.BlockingReasons) RealMigrationExecutionBlockingReasons.Add(b);
            foreach (var w in result.Warnings)        RealMigrationExecutionWarnings.Add(w);
            foreach (var e in result.Errors)          RealMigrationExecutionErrors.Add(e);

            // Forward to the dashboard-wide warning/error cards too.
            foreach (var w in result.Warnings) Warnings.Add($"RealMigration: {w}");
            foreach (var e in result.Errors)   Errors.Add($"RealMigration: {e}");

            RealMigrationExecutionStatusMessage = result.Outcome switch
            {
                GuardedRealMigrationExecutorService.OutcomeSuccess =>
                    "Migration completed. Tenant DB runtime mode is still disabled. " +
                    "Review verification and cutover readiness before enabling runtime mode.",
                GuardedRealMigrationExecutorService.OutcomeFailed =>
                    "Migration failed. No automatic rollback was attempted. Use the rollback runbook if recovery is needed.",
                GuardedRealMigrationExecutorService.OutcomeRejected =>
                    $"Rejected by guards ({result.BlockingReasons.Count} blocker(s)). No migration executed.",
                _ => result.Outcome,
            };

            StatusMessage = "Tayyor";
        }
        catch (System.Exception ex)
        {
            // Drop the captured phrase on exception too.
            phrase = "";
            RealMigrationExecutionOutcome        = GuardedRealMigrationExecutorService.OutcomeFailed;
            RealMigrationExecutionStatusMessage  = $"Failed: {ex.Message}";
            RealMigrationExecuted                = false;
            Errors.Add($"RealMigration: {ex.Message}");
            StatusMessage = "Xato";
        }
        finally
        {
            RealMigrationIsExecuting = false;
        }
    }

    // ── Guarded runtime cutover execution (Phase 10.13B) ────────────────────

    // Belt-and-suspenders: recompute the section's enabled state from the
    // flag + role check on every refresh and at the moment of execution.
    private void RecalculateRuntimeCutoverUiEnabled()
    {
        var flagOn = _global.Get(RuntimeCutoverUiFlagKey) == "1";
        var roleOk = _operatorAccess.IsAuthorizedByRole();
        RuntimeCutoverUiEnabled = flagOn && roleOk;
    }

    partial void OnRuntimeCutoverUiEnabledChanged(bool value)
    {
        RuntimeCutoverUiEnabledStatusText = value
            ? "Enabled (operator_runtime_cutover_ui_enabled=1, role authorized)."
            : "Disabled by operator_runtime_cutover_ui_enabled or role check.";
    }

    private bool CanExecuteRuntimeCutover()
        => RuntimeCutoverUiEnabled
           && !RuntimeCutoverIsExecuting
           && !IsDangerousOperationRunning
           && !string.IsNullOrWhiteSpace(RuntimeCutoverReviewedPreflightExportPath)
           && !string.IsNullOrWhiteSpace(RuntimeCutoverReviewedInventoryExportPath)
           && RuntimeCutoverExternalBackupAcknowledged
           && !string.IsNullOrWhiteSpace(RuntimeCutoverConfirmationPhraseInput);

    [RelayCommand(CanExecute = nameof(CanExecuteRuntimeCutover))]
    private System.Threading.Tasks.Task ExecuteRuntimeCutoverAsync()
        => RunDangerousOperationAsync("Runtime Cutover", ExecuteRuntimeCutoverCoreAsync);

    private async System.Threading.Tasks.Task ExecuteRuntimeCutoverCoreAsync(System.Threading.CancellationToken ct)
    {
        if (RuntimeCutoverIsExecuting) return;

        // Re-evaluate gating at click time — flag may have been toggled since
        // the last refresh, and the role check is cheap.
        RecalculateRuntimeCutoverUiEnabled();
        if (!RuntimeCutoverUiEnabled)
        {
            RuntimeCutoverExecutionStatusMessage = "Runtime cutover UI is disabled.";
            return;
        }

        RuntimeCutoverIsExecuting = true;
        StatusMessage = "Runtime cutover executing...";

        // Reset previous result state.
        RuntimeCutoverExecutionSteps.Clear();
        RuntimeCutoverExecutionBlockingReasons.Clear();
        RuntimeCutoverExecutionWarnings.Clear();
        RuntimeCutoverExecutionErrors.Clear();
        RuntimeCutoverExecutionOutcome        = "";
        RuntimeCutoverFlagChanged             = false;
        RuntimeCutoverFlagBefore              = "";
        RuntimeCutoverFlagAfter               = "";
        RuntimeCutoverExecutionStartedAtUtc   = null;
        RuntimeCutoverExecutionCompletedAtUtc = null;
        RuntimeCutoverExecutionStatusMessage  = "Executing...";

        // Capture phrase locally and immediately clear the bound property so
        // the textbox is empty for the duration of the call.
        var phrase = RuntimeCutoverConfirmationPhraseInput;
        RuntimeCutoverConfirmationPhraseInput = "";

        try
        {
            var tenant = string.IsNullOrWhiteSpace(TenantSubdomainInput)
                ? null
                : TenantSubdomainInput.Trim();

            // Phase 10.19D — backend permission preflight. Defaults OFF.
            var preflight = await ValidateBackendPermissionForDangerousOperationAsync(
                "operator.cutover.execute", "execute-runtime-cutover", ct);
            if (!preflight.Allowed)
            {
                phrase = "";
                RuntimeCutoverExecutionOutcome       = GuardedRuntimeCutoverExecutorService.OutcomeRejected;
                RuntimeCutoverFlagChanged            = false;
                RuntimeCutoverExecutionStatusMessage =
                    $"Rejected by backend permission preflight ({preflight.Status}): {preflight.Reason}";
                RuntimeCutoverExecutionBlockingReasons.Add(
                    $"Backend permission preflight rejected: {preflight.Reason}");
                Errors.Add(
                    $"BackendPermissions: preflight rejected ({preflight.PermissionKey}): {preflight.Reason}");
                StatusMessage = "Tayyor";
                return;
            }

            // Phase 10.19G — non-blocking audit-intent registration.
            await RecordAuditIntentNonBlockingAsync(
                "operator.cutover.execute", "execute-runtime-cutover", tenant, ct);

            var result = await _guardedRuntimeCutover.ExecuteAsync(
                new GuardedRuntimeCutoverExecutionOptions
                {
                    TenantSubdomain              = tenant,
                    Force                        = true,
                    ConfirmationPhrase           = phrase,
                    ExternalBackupAcknowledged   = RuntimeCutoverExternalBackupAcknowledged,
                    ExternalBackupNote           = RuntimeCutoverExternalBackupNote,
                    AllowWarnings                = RuntimeCutoverAllowWarnings,
                    ReviewedPreflightExportPath  = RuntimeCutoverReviewedPreflightExportPath,
                    ReviewedInventoryExportPath  = RuntimeCutoverReviewedInventoryExportPath,
                    WriteAuditLog                = true,
                },
                ct);

            // Drop the captured phrase from the local frame as soon as the
            // wrapper returns. The bound property was already cleared above.
            phrase = "";

            RuntimeCutoverExecutionOutcome        = result.Outcome;
            RuntimeCutoverFlagChanged             = result.RuntimeFlagChanged;
            RuntimeCutoverFlagBefore              = result.RuntimeFlagBefore ?? "";
            RuntimeCutoverFlagAfter               = result.RuntimeFlagAfter  ?? "";
            RuntimeCutoverExecutionStartedAtUtc   = result.StartedAtUtc;
            RuntimeCutoverExecutionCompletedAtUtc = result.CompletedAtUtc;

            foreach (var s in result.Steps)           RuntimeCutoverExecutionSteps.Add(s);
            foreach (var b in result.BlockingReasons) RuntimeCutoverExecutionBlockingReasons.Add(b);
            foreach (var w in result.Warnings)        RuntimeCutoverExecutionWarnings.Add(w);
            foreach (var e in result.Errors)          RuntimeCutoverExecutionErrors.Add(e);

            // Forward to the dashboard-wide warning/error cards too.
            foreach (var w in result.Warnings) Warnings.Add($"RuntimeCutover: {w}");
            foreach (var e in result.Errors)   Errors.Add($"RuntimeCutover: {e}");

            RuntimeCutoverExecutionStatusMessage = result.Outcome switch
            {
                GuardedRuntimeCutoverExecutorService.OutcomeSuccess =>
                    "Runtime cutover completed. tenant_db_runtime_enabled is now 1. " +
                    "No DB switch was performed. Restart/re-login is required.",
                GuardedRuntimeCutoverExecutorService.OutcomeFailed =>
                    "Runtime cutover failed. No automatic rollback was attempted.",
                GuardedRuntimeCutoverExecutorService.OutcomeRejected =>
                    $"Rejected by guards ({result.BlockingReasons.Count} blocker(s)). Runtime flag unchanged.",
                _ => result.Outcome,
            };

            StatusMessage = "Tayyor";
        }
        catch (System.Exception ex)
        {
            // Drop the captured phrase on exception too.
            phrase = "";
            RuntimeCutoverExecutionOutcome       = GuardedRuntimeCutoverExecutorService.OutcomeFailed;
            RuntimeCutoverExecutionStatusMessage = $"Failed: {ex.Message}";
            RuntimeCutoverFlagChanged            = false;
            Errors.Add($"RuntimeCutover: {ex.Message}");
            StatusMessage = "Xato";
        }
        finally
        {
            RuntimeCutoverIsExecuting = false;
        }
    }

    // ── Guarded rollback execution (Phase 10.14B) ───────────────────────────

    private void RecalculateRollbackUiEnabled()
    {
        var flagOn = _global.Get(RollbackUiFlagKey) == "1";
        var roleOk = _operatorAccess.IsAuthorizedByRole();
        RollbackUiEnabled = flagOn && roleOk;
    }

    partial void OnRollbackUiEnabledChanged(bool value)
    {
        RollbackUiEnabledStatusText = value
            ? "Enabled (operator_rollback_ui_enabled=1, role authorized)."
            : "Disabled by operator_rollback_ui_enabled or role check.";
    }

    private bool CanExecuteRollback()
        => RollbackUiEnabled
           && !RollbackIsExecuting
           && !IsDangerousOperationRunning
           && !string.IsNullOrWhiteSpace(RollbackReviewedPreflightExportPath)
           && !string.IsNullOrWhiteSpace(RollbackReviewedInventoryExportPath)
           && RollbackExternalBackupAcknowledged
           && !string.IsNullOrWhiteSpace(RollbackConfirmationPhraseInput)
           && RollbackArchiveTenantsDirectory
           && RollbackDisableRuntimeFlag;

    [RelayCommand(CanExecute = nameof(CanExecuteRollback))]
    private System.Threading.Tasks.Task ExecuteRollbackAsync()
        => RunDangerousOperationAsync("Rollback", ExecuteRollbackCoreAsync);

    private async System.Threading.Tasks.Task ExecuteRollbackCoreAsync(System.Threading.CancellationToken ct)
    {
        if (RollbackIsExecuting) return;

        // Re-evaluate gating at click time — flag/role may have changed since
        // the last refresh.
        RecalculateRollbackUiEnabled();
        if (!RollbackUiEnabled)
        {
            RollbackExecutionStatusMessage = "Rollback UI is disabled.";
            return;
        }

        RollbackIsExecuting = true;
        StatusMessage = "Rollback executing...";

        // Reset previous result state.
        RollbackExecutionSteps.Clear();
        RollbackExecutionBlockingReasons.Clear();
        RollbackExecutionWarnings.Clear();
        RollbackExecutionErrors.Clear();
        RollbackExecutionOutcome              = "";
        RollbackExecuted                      = false;
        RollbackReadinessStatus               = "";
        RollbackRuntimeTenantDbEnabledBefore  = false;
        RollbackRuntimeTenantDbEnabledAfter   = false;
        RollbackIsProviderTenantScopedBefore  = false;
        RollbackExecutionStartedAtUtc         = null;
        RollbackExecutionCompletedAtUtc       = null;
        RollbackExecutionStatusMessage        = "Executing...";

        // Capture phrase locally; immediately clear the bound property so the
        // textbox is empty for the duration of the call.
        var phrase = RollbackConfirmationPhraseInput;
        RollbackConfirmationPhraseInput = "";

        try
        {
            // Phase 10.19D — backend permission preflight. Defaults OFF.
            var preflight = await ValidateBackendPermissionForDangerousOperationAsync(
                "operator.rollback.execute", "execute-rollback", ct);
            if (!preflight.Allowed)
            {
                phrase = "";
                RollbackExecutionOutcome       = GuardedRollbackExecutorService.OutcomeRejected;
                RollbackExecuted               = false;
                RollbackExecutionStatusMessage =
                    $"Rejected by backend permission preflight ({preflight.Status}): {preflight.Reason}";
                RollbackExecutionBlockingReasons.Add(
                    $"Backend permission preflight rejected: {preflight.Reason}");
                Errors.Add(
                    $"BackendPermissions: preflight rejected ({preflight.PermissionKey}): {preflight.Reason}");
                StatusMessage = "Tayyor";
                return;
            }

            // Phase 10.19G — non-blocking audit-intent registration.
            {
                var auditTenant = string.IsNullOrWhiteSpace(TenantSubdomainInput)
                    ? null : TenantSubdomainInput.Trim();
                await RecordAuditIntentNonBlockingAsync(
                    "operator.rollback.execute", "execute-rollback", auditTenant, ct);
            }

            var result = await _guardedRollback.ExecuteAsync(
                new GuardedRollbackExecutionOptions
                {
                    Force                            = true,
                    ConfirmationPhrase               = phrase,
                    ExternalBackupAcknowledged       = RollbackExternalBackupAcknowledged,
                    ExternalBackupNote               = RollbackExternalBackupNote,
                    AllowWarnings                    = RollbackAllowWarnings,
                    ReviewedPreflightExportPath      = RollbackReviewedPreflightExportPath,
                    ReviewedInventoryExportPath      = RollbackReviewedInventoryExportPath,
                    ArchiveTenantsDirectory          = RollbackArchiveTenantsDirectory,
                    DisableRuntimeFlag               = RollbackDisableRuntimeFlag,
                    RestoreLegacyFromBackupIfMissing = RollbackRestoreLegacyFromBackupIfMissing,
                    WriteAuditLog                    = true,
                },
                ct);

            phrase = "";

            RollbackExecutionOutcome             = result.Outcome;
            RollbackExecuted                     = result.RollbackExecuted;
            RollbackReadinessStatus              = result.RollbackReadinessStatus ?? "";
            RollbackRuntimeTenantDbEnabledBefore = result.RuntimeTenantDbEnabledBefore;
            RollbackRuntimeTenantDbEnabledAfter  = result.RuntimeTenantDbEnabledAfter;
            RollbackIsProviderTenantScopedBefore = result.IsProviderTenantScopedBefore;
            RollbackExecutionStartedAtUtc        = result.StartedAtUtc;
            RollbackExecutionCompletedAtUtc      = result.CompletedAtUtc;

            foreach (var s in result.Steps)           RollbackExecutionSteps.Add(s);
            foreach (var b in result.BlockingReasons) RollbackExecutionBlockingReasons.Add(b);
            foreach (var w in result.Warnings)        RollbackExecutionWarnings.Add(w);
            foreach (var e in result.Errors)          RollbackExecutionErrors.Add(e);

            // Forward to the dashboard-wide warning/error cards.
            foreach (var w in result.Warnings) Warnings.Add($"Rollback: {w}");
            foreach (var e in result.Errors)   Errors.Add($"Rollback: {e}");

            RollbackExecutionStatusMessage = result.Outcome switch
            {
                GuardedRollbackExecutorService.OutcomeSuccess =>
                    "Rollback completed. No DB switch was performed. Restart/re-login is required " +
                    "before post-rollback runtime state is fully applied.",
                GuardedRollbackExecutorService.OutcomeFailed =>
                    "Rollback failed. No automatic migration was attempted.",
                GuardedRollbackExecutorService.OutcomeRejected =>
                    $"Rejected by guards ({result.BlockingReasons.Count} blocker(s)). No rollback executed.",
                _ => result.Outcome,
            };

            StatusMessage = "Tayyor";
        }
        catch (System.Exception ex)
        {
            phrase = "";
            RollbackExecutionOutcome       = GuardedRollbackExecutorService.OutcomeFailed;
            RollbackExecutionStatusMessage = $"Failed: {ex.Message}";
            RollbackExecuted               = false;
            Errors.Add($"Rollback: {ex.Message}");
            StatusMessage = "Xato";
        }
        finally
        {
            RollbackIsExecuting = false;
        }
    }

    // ── Guarded retention cleanup execution (Phase 10.16B) ──────────────────

    private void RecalculateRetentionCleanupUiEnabled()
    {
        var flagOn = _global.Get(RetentionCleanupUiFlagKey) == "1";
        var roleOk = _operatorAccess.IsAuthorizedByRole();
        RetentionCleanupUiEnabled = flagOn && roleOk;
    }

    partial void OnRetentionCleanupUiEnabledChanged(bool value)
    {
        RetentionCleanupUiEnabledStatusText = value
            ? "Enabled (operator_retention_cleanup_ui_enabled=1, role authorized)."
            : "Disabled by operator_retention_cleanup_ui_enabled or role check.";
    }

    private bool AnyRetentionCategoryEnabled
        => RetentionCleanupDeleteDiagnosticsLogs
        || RetentionCleanupDeletePreflightLogs
        || RetentionCleanupDeleteMigrationLogs
        || RetentionCleanupDeleteRollbackLogs
        || RetentionCleanupDeleteOldBackups
        || RetentionCleanupDeleteArchivedTenantDirectories
        || RetentionCleanupDeleteBrokenLegacyDbFiles;

    private bool CanExecuteRetentionCleanup()
        => RetentionCleanupUiEnabled
           && !IsDangerousOperationRunning
           && !RetentionCleanupIsExecuting
           && !string.IsNullOrWhiteSpace(RetentionCleanupReviewedInventoryExportPath)
           && RetentionCleanupExternalBackupAcknowledged
           && !string.IsNullOrWhiteSpace(RetentionCleanupConfirmationPhraseInput)
           && AnyRetentionCategoryEnabled;

    [RelayCommand(CanExecute = nameof(CanExecuteRetentionCleanup))]
    private System.Threading.Tasks.Task ExecuteRetentionCleanupAsync()
        => RunDangerousOperationAsync("Retention Cleanup", ExecuteRetentionCleanupCoreAsync);

    private async System.Threading.Tasks.Task ExecuteRetentionCleanupCoreAsync(System.Threading.CancellationToken ct)
    {
        if (RetentionCleanupIsExecuting) return;

        // Re-evaluate gating at click time.
        RecalculateRetentionCleanupUiEnabled();
        if (!RetentionCleanupUiEnabled)
        {
            RetentionCleanupExecutionStatusMessage = "Retention cleanup UI is disabled.";
            return;
        }

        RetentionCleanupIsExecuting = true;
        StatusMessage = "Retention cleanup executing...";

        // Reset previous result state.
        RetentionCleanupExecutionSteps.Clear();
        RetentionCleanupExecutionBlockingReasons.Clear();
        RetentionCleanupExecutionWarnings.Clear();
        RetentionCleanupExecutionErrors.Clear();
        RetentionCleanupItemLines.Clear();
        RetentionCleanupExecutionOutcome        = "";
        RetentionCleanupExecuted                = false;
        RetentionCleanupCandidateCountBefore    = 0;
        RetentionCleanupCandidateSizeBefore     = "0 B";
        RetentionCleanupDeletedItemCount        = 0;
        RetentionCleanupDeletedSize             = "0 B";
        RetentionCleanupSkippedItemCount        = 0;
        RetentionCleanupFailedItemCount         = 0;
        RetentionCleanupExecutionStartedAtUtc   = null;
        RetentionCleanupExecutionCompletedAtUtc = null;
        RetentionCleanupExecutionStatusMessage  = "Executing...";

        var phrase = RetentionCleanupConfirmationPhraseInput;
        RetentionCleanupConfirmationPhraseInput = "";

        try
        {
            // Phase 10.19D — backend permission preflight. Defaults OFF.
            var preflight = await ValidateBackendPermissionForDangerousOperationAsync(
                "operator.retention.cleanup.execute", "execute-retention-cleanup", ct);
            if (!preflight.Allowed)
            {
                phrase = "";
                RetentionCleanupExecutionOutcome       = GuardedRetentionCleanupExecutorService.OutcomeRejected;
                RetentionCleanupExecuted               = false;
                RetentionCleanupExecutionStatusMessage =
                    $"Rejected by backend permission preflight ({preflight.Status}): {preflight.Reason}";
                RetentionCleanupExecutionBlockingReasons.Add(
                    $"Backend permission preflight rejected: {preflight.Reason}");
                Errors.Add(
                    $"BackendPermissions: preflight rejected ({preflight.PermissionKey}): {preflight.Reason}");
                StatusMessage = "Tayyor";
                return;
            }

            // Phase 10.19G — non-blocking audit-intent registration.
            {
                var auditTenant = string.IsNullOrWhiteSpace(TenantSubdomainInput)
                    ? null : TenantSubdomainInput.Trim();
                await RecordAuditIntentNonBlockingAsync(
                    "operator.retention.cleanup.execute", "execute-retention-cleanup", auditTenant, ct);
            }

            var result = await _guardedRetentionCleanup.ExecuteAsync(
                new GuardedRetentionCleanupExecutionOptions
                {
                    Force                            = true,
                    ConfirmationPhrase               = phrase,
                    ExternalBackupAcknowledged       = RetentionCleanupExternalBackupAcknowledged,
                    ExternalBackupNote               = RetentionCleanupExternalBackupNote,
                    ReviewedInventoryExportPath      = RetentionCleanupReviewedInventoryExportPath,
                    DeleteDiagnosticsLogs            = RetentionCleanupDeleteDiagnosticsLogs,
                    DeletePreflightLogs              = RetentionCleanupDeletePreflightLogs,
                    DeleteMigrationLogs              = RetentionCleanupDeleteMigrationLogs,
                    DeleteRollbackLogs               = RetentionCleanupDeleteRollbackLogs,
                    DeleteOldBackups                 = RetentionCleanupDeleteOldBackups,
                    DeleteArchivedTenantDirectories  = RetentionCleanupDeleteArchivedTenantDirectories,
                    DeleteBrokenLegacyDbFiles        = RetentionCleanupDeleteBrokenLegacyDbFiles,
                    WriteAuditLog                    = true,
                },
                ct);

            phrase = "";

            RetentionCleanupExecutionOutcome        = result.Outcome;
            RetentionCleanupExecuted                = result.CleanupExecuted;
            RetentionCleanupCandidateCountBefore    = result.CandidateCountBefore;
            RetentionCleanupCandidateSizeBefore     = result.CandidateSizeTextBefore;
            RetentionCleanupDeletedItemCount        = result.DeletedItemCount;
            RetentionCleanupDeletedSize             = result.DeletedSizeText;
            RetentionCleanupSkippedItemCount        = result.SkippedItemCount;
            RetentionCleanupFailedItemCount         = result.FailedItemCount;
            RetentionCleanupExecutionStartedAtUtc   = result.StartedAtUtc;
            RetentionCleanupExecutionCompletedAtUtc = result.CompletedAtUtc;

            foreach (var s in result.Steps)           RetentionCleanupExecutionSteps.Add(s);
            foreach (var b in result.BlockingReasons) RetentionCleanupExecutionBlockingReasons.Add(b);
            foreach (var w in result.Warnings)        RetentionCleanupExecutionWarnings.Add(w);
            foreach (var e in result.Errors)          RetentionCleanupExecutionErrors.Add(e);

            foreach (var item in result.Items)
                RetentionCleanupItemLines.Add(FormatRetentionCleanupItem(item));

            // Forward to dashboard-wide warning/error cards.
            foreach (var w in result.Warnings) Warnings.Add($"RetentionCleanup: {w}");
            foreach (var e in result.Errors)   Errors.Add($"RetentionCleanup: {e}");

            RetentionCleanupExecutionStatusMessage = result.Outcome switch
            {
                GuardedRetentionCleanupExecutorService.OutcomeSuccess =>
                    "Retention cleanup completed. Only guarded retention candidates were deleted. " +
                    "No DB switch, logout, restart, migration, rollback, or cutover was performed.",
                GuardedRetentionCleanupExecutorService.OutcomeNoOp =>
                    "Retention cleanup completed with no operation. No eligible candidates were deleted.",
                GuardedRetentionCleanupExecutorService.OutcomeFailed =>
                    "Retention cleanup failed. No automatic restore was attempted.",
                GuardedRetentionCleanupExecutorService.OutcomeRejected =>
                    $"Rejected by guards ({result.BlockingReasons.Count} blocker(s)). No deletion attempted.",
                _ => result.Outcome,
            };

            StatusMessage = "Tayyor";
        }
        catch (System.Exception ex)
        {
            phrase = "";
            RetentionCleanupExecutionOutcome       = GuardedRetentionCleanupExecutorService.OutcomeFailed;
            RetentionCleanupExecutionStatusMessage = $"Failed: {ex.Message}";
            RetentionCleanupExecuted               = false;
            Errors.Add($"RetentionCleanup: {ex.Message}");
            StatusMessage = "Xato";
        }
        finally
        {
            RetentionCleanupIsExecuting = false;
        }
    }

    private static string FormatRetentionCleanupItem(RetentionCleanupItemResult i)
        => $"[{i.Category}] {i.Name} (size={FormatBytesShort(i.SizeBytes)}) — {i.Action}: {i.Reason}" +
           (string.IsNullOrEmpty(i.Error) ? "" : $" :: {i.Error}");

    private static string FormatBytesShort(long bytes)
    {
        if (bytes < 1024)               return $"{bytes} B";
        if (bytes < 1024L * 1024)       return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    // ── Production pilot readiness command (Phase 10.17A) ───────────────────

    [RelayCommand]
    private async System.Threading.Tasks.Task GeneratePilotReadinessReportAsync()
    {
        if (PilotReadinessIsGenerating) return;
        if (IsLoading) return;

        PilotReadinessIsGenerating = true;
        IsLoading = true;
        StatusMessage              = "Pilot readiness report...";
        PilotReadinessStatusMessage = "Generating...";

        // Reset previous state.
        PilotReadinessChecks.Clear();
        PilotReadinessBlockingReasons.Clear();
        PilotReadinessWarnings.Clear();
        PilotReadinessRecommendedNextSteps.Clear();
        PilotReadinessOverallStatus  = "";
        PilotReadinessGeneratedAtUtc = null;
        PilotReadinessExportPath     = "";

        try
        {
            var tenant = string.IsNullOrWhiteSpace(TenantSubdomainInput)
                ? null
                : TenantSubdomainInput.Trim();

            var report = await _pilotReadinessReport.GenerateAsync(tenant);

            PilotReadinessOverallStatus  = report.OverallStatus;
            PilotReadinessGeneratedAtUtc = report.GeneratedAtUtc;
            PilotReadinessExportPath     = report.ExportPath ?? "";

            foreach (var c in report.Checks)
                PilotReadinessChecks.Add($"[{c.Area}/{c.Name}] {c.Status}: {c.Message}");

            foreach (var b in report.BlockingReasons)
                PilotReadinessBlockingReasons.Add(b);
            foreach (var w in report.Warnings)
                PilotReadinessWarnings.Add(w);
            foreach (var s in report.RecommendedNextSteps)
                PilotReadinessRecommendedNextSteps.Add(s);

            PilotReadinessStatusMessage = report.OverallStatus switch
            {
                "Ready" =>
                    "Pilot readiness: Ready. Follow docs/operator-tenant-db-migration-runbook.md before any destructive operation.",
                "ReadyWithWarnings" =>
                    "Pilot readiness: ReadyWithWarnings. Review warnings and recommended next steps before proceeding.",
                "Blocked" =>
                    $"Pilot readiness: Blocked. {report.BlockingReasons.Count} blocker(s). Do not run migration/cutover/rollback/cleanup until resolved.",
                _ => $"Pilot readiness: {report.OverallStatus}.",
            };

            // Surface report errors (e.g. failed export) into the dashboard's
            // red Errors card so they don't get lost.
            if (string.IsNullOrEmpty(report.ExportPath))
                Errors.Add("Pilot readiness export failed (no file written under logs\\pilot-readiness).");

            StatusMessage = "Tayyor";
        }
        catch (System.Exception ex)
        {
            PilotReadinessOverallStatus  = "Unknown";
            PilotReadinessStatusMessage  = $"Failed: {ex.Message}";
            Errors.Add($"PilotReadiness: {ex.Message}");
            StatusMessage = "Xato";
        }
        finally
        {
            PilotReadinessIsGenerating = false;
            IsLoading = false;
        }
    }

    // ── Production pilot evidence bundle command (Phase 10.17B) ─────────────

    [RelayCommand]
    private async System.Threading.Tasks.Task ExportPilotEvidenceBundleAsync()
    {
        if (PilotEvidenceIsExporting) return;
        if (IsLoading) return;

        PilotEvidenceIsExporting = true;
        IsLoading = true;
        StatusMessage              = "Pilot evidence bundle export...";
        PilotEvidenceStatusMessage = "Exporting...";

        // Reset previous state.
        PilotEvidenceIncludedFiles.Clear();
        PilotEvidenceWarnings.Clear();
        PilotEvidenceErrors.Clear();
        PilotEvidenceSteps.Clear();
        PilotEvidenceOutcome         = "";
        PilotEvidenceBundleDirectory = "";
        PilotEvidenceBundleZipPath   = "";
        PilotEvidenceManifestPath    = "";
        PilotEvidenceStartedAtUtc    = null;
        PilotEvidenceCompletedAtUtc  = null;

        try
        {
            var tenant = string.IsNullOrWhiteSpace(TenantSubdomainInput)
                ? null
                : TenantSubdomainInput.Trim();

            var result = await _pilotEvidenceBundle.ExportAsync(tenant);

            PilotEvidenceOutcome         = result.Outcome;
            PilotEvidenceBundleDirectory = result.BundleDirectory ?? "";
            PilotEvidenceBundleZipPath   = result.BundleZipPath   ?? "";
            PilotEvidenceManifestPath    = result.ManifestPath    ?? "";
            PilotEvidenceStartedAtUtc    = result.StartedAtUtc;
            PilotEvidenceCompletedAtUtc  = result.CompletedAtUtc;

            foreach (var f in result.IncludedFiles) PilotEvidenceIncludedFiles.Add(f);
            foreach (var w in result.Warnings)      PilotEvidenceWarnings.Add(w);
            foreach (var e in result.Errors)        PilotEvidenceErrors.Add(e);
            foreach (var s in result.Steps)         PilotEvidenceSteps.Add(s);

            // Forward to dashboard-wide cards too.
            foreach (var w in result.Warnings) Warnings.Add($"PilotEvidence: {w}");
            foreach (var e in result.Errors)   Errors.Add($"PilotEvidence: {e}");

            PilotEvidenceStatusMessage = result.Outcome == "Success"
                ? $"Pilot evidence bundle written ({result.FileCount} file(s), {result.TotalSizeText})." +
                  (string.IsNullOrEmpty(result.BundleZipPath)
                      ? " ZIP not created."
                      : " ZIP created.")
                : $"Pilot evidence bundle export failed ({result.Errors.Count} error(s)).";

            // Phase 10.19G — non-blocking metadata-only evidence registration.
            // Runs only after a successful local export. Backend rejection /
            // unavailability NEVER invalidates the local bundle on disk.
            if (result.Outcome == "Success")
            {
                await TryRegisterEvidenceMetadataAsync(result, tenant);
            }

            StatusMessage = "Tayyor";
        }
        catch (System.Exception ex)
        {
            PilotEvidenceOutcome       = "Failed";
            PilotEvidenceStatusMessage = $"Failed: {ex.Message}";
            Errors.Add($"PilotEvidence: {ex.Message}");
            StatusMessage = "Xato";
        }
        finally
        {
            PilotEvidenceIsExporting = false;
            IsLoading = false;
        }
    }

    // ── Phase 10.19G — Metadata-only evidence registration ──────────────────
    //
    // Called only after a successful Pilot Evidence Bundle export. Default
    // OFF. When ON, sends sanitized metadata (bundle id, file names, hashes,
    // counts) to the backend. NEVER sends:
    //   • bundle ZIP bytes
    //   • raw JSON file contents
    //   • raw audit log content
    //   • DB content, backups, or full local paths
    //   • the raw machine name (only a SHA-256 hex digest)
    //   • confirmation phrases or tokens
    //
    // Backend rejection / unavailability surfaces a BackendEvidence: warning
    // but the local bundle on disk remains the operational source of truth.
    private async System.Threading.Tasks.Task TryRegisterEvidenceMetadataAsync(
        ProductionPilotEvidenceBundleResult result, string? tenant)
    {
        RefreshBackendAuditEvidenceFlagStatusTexts();
        if (!BackendEvidenceRegistrationEnabled) return;

        // Hash off the UI thread so a multi-MB ZIP does not stall the
        // dispatcher.
        string? manifestSha = null;
        string? bundleSha   = null;
        try
        {
            if (!string.IsNullOrEmpty(result.ManifestPath) && System.IO.File.Exists(result.ManifestPath))
                manifestSha = await System.Threading.Tasks.Task.Run(
                    () => OperatorAuditEvidenceHashing.Sha256HexOfFile(result.ManifestPath!));
            else if (!string.IsNullOrEmpty(result.ManifestPath))
                Warnings.Add("BackendEvidence: manifest file missing on disk; manifest hash omitted.");

            if (!string.IsNullOrEmpty(result.BundleZipPath) && System.IO.File.Exists(result.BundleZipPath))
                bundleSha = await System.Threading.Tasks.Task.Run(
                    () => OperatorAuditEvidenceHashing.Sha256HexOfFile(result.BundleZipPath!));
        }
        catch (System.Exception ex)
        {
            Warnings.Add($"BackendEvidence: hash failed: {ex.Message}");
        }

        string machineHash;
        try { machineHash = OperatorAuditEvidenceHashing.Sha256HexOfString(System.Environment.MachineName ?? ""); }
        catch { machineHash = ""; }

        var bundleId = !string.IsNullOrEmpty(result.BundleDirectory)
            ? System.IO.Path.GetFileName(result.BundleDirectory!) ?? ""
            : "";

        var includedFiles = result.IncludedFiles
            .Where(s => !string.IsNullOrWhiteSpace(s))
            // Defence-in-depth: even though ProductionPilotEvidenceBundleService
            // already records bare names, strip path components here so a future
            // service change cannot leak a directory.
            .Select(System.IO.Path.GetFileName)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .ToList();

        var req = new OperatorEvidenceRegisterRequestDto
        {
            EvidenceBundleId                     = bundleId,
            TenantId                             = string.IsNullOrWhiteSpace(tenant) ? null : tenant,
            StoreId                              = null,
            PilotId                              = null,
            ApprovalTicketId                     = null,
            BundleGeneratedAt                    = result.CompletedAtUtc.ToString("O"),
            ReadinessOverallStatus               = null,
            BackendPermissionEnforcementEnabled  = _global.Get(BackendEnforcementFlagKey) == "1",
            BackendPermissionSummaryStatus       = BackendPermissionEnforcementStatusText,
            IncludedFiles                        = includedFiles,
            FileCount                            = result.FileCount,
            TotalBytes                           = result.TotalBytes,
            ManifestSha256                       = manifestSha,
            BundleSha256                         = bundleSha,
            ClientMachineNameHash                = machineHash,
            Notes                                = "Desktop Phase 10.19G metadata registration.",
        };

        OperatorEvidenceRegisterResultDto? reg = null;
        try
        {
            reg = await _operatorAuditEvidenceApi.RegisterEvidenceAsync(req);
        }
        catch (System.OperationCanceledException) { throw; }
        catch (System.Exception ex)
        {
            Warnings.Add($"BackendEvidence: {ex.Message}");
        }

        BackendEvidenceRegistrationLastBundleId = bundleId;
        BackendEvidenceRegistrationLastAtUtc    = System.DateTime.UtcNow;

        if (reg is { Accepted: true })
        {
            BackendEvidenceRegistrationLastStatus         = "accepted";
            BackendEvidenceRegistrationLastRegistrationId = reg.RegistrationId ?? "";
            BackendEvidenceRegistrationLastReason         = "Accepted by backend.";
        }
        else if (reg is null)
        {
            BackendEvidenceRegistrationLastStatus         = "unavailable";
            BackendEvidenceRegistrationLastRegistrationId = "";
            BackendEvidenceRegistrationLastReason         = "Backend unreachable or returned an error.";
            Warnings.Add("BackendEvidence: registration unavailable.");
        }
        else
        {
            BackendEvidenceRegistrationLastStatus         = "rejected";
            BackendEvidenceRegistrationLastRegistrationId = "";
            var rejectReason = reg.Warnings is { Count: > 0 }
                ? string.Join("; ", reg.Warnings)
                : "Backend rejected the evidence registration.";
            BackendEvidenceRegistrationLastReason         = rejectReason;
            Warnings.Add($"BackendEvidence: registration rejected: {rejectReason}");
        }
    }

    // ── Backend operator permissions refresh command (Phase 10.19C) ────────
    //
    // Display-only. Read-only HTTP fetches against the Phase 10.19B backend.
    // No CanExecute gating is changed. No guarded wrapper is invoked. No
    // dangerous-operation lock is taken.

    private const string PermDashboardOpen = "operator.dashboard.open";

    [RelayCommand]
    private async System.Threading.Tasks.Task RefreshBackendOperatorPermissionsAsync()
    {
        if (BackendOperatorPermissionsIsLoading) return;
        if (IsLoading) return;

        BackendOperatorPermissionsIsLoading = true;
        IsLoading = true;
        StatusMessage = "Backend operator permissions...";
        BackendOperatorPermissionsStatus = "Fetching...";

        // Phase 10.19G — refresh read-only display of the new backend audit /
        // evidence flag states alongside the existing permission refresh.
        RefreshBackendAuditEvidenceFlagStatusTexts();

        // Reset previous state.
        BackendOperatorPermissions.Clear();
        BackendOperatorDangerousPermissions.Clear();
        BackendOperatorReadOnlyPermissions.Clear();
        BackendOperatorPermissionWarnings.Clear();
        BackendOperatorPermissionErrors.Clear();
        BackendOperatorPermissionsSource         = "";
        BackendOperatorPermissionsGeneratedAt    = null;
        BackendOperatorPermissionsExpiresAt      = null;
        BackendOperatorUserId                    = "";
        BackendOperatorUsername                  = "";
        BackendOperatorRole                      = "";
        BackendOperatorTenantId                  = "";
        BackendOperatorStoreId                   = "";
        BackendOperatorAuthenticated             = false;
        BackendOperatorPermissionCount           = 0;
        BackendOperatorDangerousPermissionCount  = 0;
        BackendOperatorReadOnlyPermissionCount   = 0;

        try
        {
            // 1. Identity. Fail-closed → null when offline / 401 / 5xx.
            var identity = await _operatorPermissionApi.GetIdentityAsync();
            if (identity is null)
            {
                BackendOperatorPermissionsStatus = "Backend identity request failed (offline, unauthorized, or backend unreachable).";
                BackendOperatorPermissionErrors.Add("Identity request returned null. Local flag/role/wrapper gates remain in effect.");
                Errors.Add("BackendPermissions: identity request failed.");
            }
            else
            {
                BackendOperatorUserId             = identity.UserId?.ToString() ?? "";
                BackendOperatorUsername           = identity.Username ?? "";
                BackendOperatorRole               = identity.Role ?? "";
                BackendOperatorTenantId           = identity.TenantId ?? "";
                BackendOperatorStoreId            = identity.StoreId ?? "";
                BackendOperatorAuthenticated      = identity.Authenticated;
                BackendOperatorPermissionsSource  = identity.PermissionsSource ?? "";
                BackendOperatorPermissionsGeneratedAt = identity.GeneratedAt;
            }

            // 2. Permissions. Fail-closed → null on failure.
            var permissions = await _operatorPermissionApi.GetPermissionsAsync();
            if (permissions is null)
            {
                BackendOperatorPermissionsStatus = "Backend permissions request failed (offline, unauthorized, or backend unreachable).";
                BackendOperatorPermissionErrors.Add("Permissions request returned null. Local flag/role/wrapper gates remain in effect.");
                Errors.Add("BackendPermissions: permissions request failed.");
            }
            else
            {
                BackendOperatorPermissionsExpiresAt = permissions.ExpiresAt;
                BackendOperatorPermissionCount        = permissions.Permissions?.Count ?? 0;
                BackendOperatorDangerousPermissionCount = permissions.DangerousPermissions?.Count ?? 0;
                BackendOperatorReadOnlyPermissionCount  = permissions.ReadOnlyPermissions?.Count ?? 0;
                if (permissions.Permissions is not null)
                    foreach (var p in permissions.Permissions) BackendOperatorPermissions.Add(p);
                if (permissions.DangerousPermissions is not null)
                    foreach (var p in permissions.DangerousPermissions) BackendOperatorDangerousPermissions.Add(p);
                if (permissions.ReadOnlyPermissions is not null)
                    foreach (var p in permissions.ReadOnlyPermissions) BackendOperatorReadOnlyPermissions.Add(p);

                // Comparison warnings — display-only signals for the operator.
                // None of these change any CanExecute predicate.
                ComposeBackendPermissionWarnings(identity, permissions);

                if (BackendOperatorPermissionErrors.Count == 0)
                    BackendOperatorPermissionsStatus = "Loaded (display-only — does not gate desktop buttons in this phase).";
            }

            StatusMessage = "Tayyor";
        }
        catch (System.Exception ex)
        {
            // The wrapper catches normal HTTP failures and returns null. Any
            // exception that bubbles up here is unexpected — surface it but
            // do not crash the dashboard.
            BackendOperatorPermissionsStatus = $"Unexpected failure: {ex.Message}";
            BackendOperatorPermissionErrors.Add($"Unexpected failure: {ex.Message}");
            Errors.Add($"BackendPermissions: unexpected failure: {ex.Message}");
            StatusMessage = "Xato";
        }
        finally
        {
            BackendOperatorPermissionsIsLoading = false;
            IsLoading = false;
        }
    }

    private void ComposeBackendPermissionWarnings(
        OperatorIdentityDto? identity,
        OperatorPermissionsDto permissions)
    {
        // 1. CASHIER role from backend while the dashboard is open.
        if (string.Equals(identity?.Role, "CASHIER", System.StringComparison.OrdinalIgnoreCase))
        {
            BackendOperatorPermissionWarnings.Add(
                "Backend role is CASHIER but the dashboard is open. Phase 10.19C does not enforce backend permissions yet — local flags + OperatorAccessService still gate UI access.");
        }

        // 2. Backend permission list empty.
        if (permissions.Permissions is null || permissions.Permissions.Count == 0)
        {
            BackendOperatorPermissionWarnings.Add(
                "Backend returned zero operator permissions for this user.");
        }

        // 3. Backend permissions expired.
        if (permissions.ExpiresAt is { } exp && exp < System.DateTime.UtcNow)
        {
            BackendOperatorPermissionWarnings.Add(
                $"Backend permission claim is expired (expiresAt={exp:o}).");
        }

        // 4. operator.dashboard.open missing while the dashboard is open.
        var hasDashboardOpen = permissions.Permissions is not null &&
                               permissions.Permissions.Contains(PermDashboardOpen);
        if (!hasDashboardOpen)
        {
            BackendOperatorPermissionWarnings.Add(
                $"Backend permission '{PermDashboardOpen}' is missing while the dashboard is open. Phase 10.19C is display-only; local flag + role gate is still authoritative.");
        }

        // 5. Backend dangerous permissions present AND destructive local UI
        //    flags are currently enabled outside an operation window. These
        //    are exactly the four flags whose ON state is normally restricted
        //    to the brief window of the relevant operation.
        if (permissions.DangerousPermissions is not null && permissions.DangerousPermissions.Count > 0)
        {
            CheckDangerousFlagAndWarn(RealMigrationUiFlagKey,    "operator_real_migration_ui_enabled");
            CheckDangerousFlagAndWarn(RuntimeCutoverUiFlagKey,   "operator_runtime_cutover_ui_enabled");
            CheckDangerousFlagAndWarn(RollbackUiFlagKey,         "operator_rollback_ui_enabled");
            CheckDangerousFlagAndWarn(RetentionCleanupUiFlagKey, "operator_retention_cleanup_ui_enabled");
        }

        // 6. Backend role differs from locally-stored user_role, when both
        //    are available. (The local role is read by OperatorAccessService
        //    from the AuthService; we re-derive it here from settings to
        //    avoid a new dependency on AuthService just for a warning.)
        var localRole = _global.Get("user_role"); // best-effort; may be empty.
        if (!string.IsNullOrWhiteSpace(localRole) &&
            !string.IsNullOrWhiteSpace(identity?.Role) &&
            !string.Equals(localRole, identity!.Role, System.StringComparison.OrdinalIgnoreCase))
        {
            BackendOperatorPermissionWarnings.Add(
                $"Backend role '{identity.Role}' differs from local cached role '{localRole}'. Consider logging out and back in.");
        }
    }

    private void CheckDangerousFlagAndWarn(string flagKey, string flagDisplayName)
    {
        if (_global.Get(flagKey) == "1")
        {
            BackendOperatorPermissionWarnings.Add(
                $"Backend grants dangerous permissions AND local flag {flagDisplayName}=\"1\". Confirm this is intentional for the current operation window.");
        }
    }

    // ── Backend permission preflight helper (Phase 10.19D) ──────────────────
    //
    // Called from each Execute*CoreAsync after phrase capture+clear but
    // before the guarded wrapper. When the enforcement flag is OFF, returns
    // Allowed=true without contacting the backend (preserves Phase 10.19C
    // behaviour exactly). When ON, calls validate and fails closed on
    // null/denied/metadata-mismatch.
    //
    // Security: never sends the confirmation phrase; never sends local DB
    // paths or evidence-bundle content; never logs tokens.

    private async System.Threading.Tasks.Task<BackendPermissionPreflightResult>
        ValidateBackendPermissionForDangerousOperationAsync(
            string permissionKey,
            string operationName,
            System.Threading.CancellationToken ct)
    {
        var enabled = _global.Get(BackendEnforcementFlagKey) == "1";

        var tenant = string.IsNullOrWhiteSpace(TenantSubdomainInput)
            ? _global.Get("last_tenant_subdomain")
            : TenantSubdomainInput.Trim();

        if (!enabled)
        {
            var skipped = new BackendPermissionPreflightResult
            {
                EnforcementEnabled = false,
                Allowed            = true,
                Status             = "Skipped",
                Reason             = "Backend permission enforcement is disabled by local flag.",
                PermissionKey      = permissionKey,
            };
            UpdateLastPreflightDisplay(skipped, operationName);
            return skipped;
        }

        OperatorPermissionValidateResultDto? result;
        try
        {
            result = await _operatorPermissionApi.ValidateAsync(
                new OperatorPermissionValidateRequestDto
                {
                    PermissionKey    = permissionKey,
                    TenantId         = string.IsNullOrWhiteSpace(tenant) ? null : tenant,
                    StoreId          = null,
                    OperationName    = operationName,
                    ApprovalTicketId = null,
                    // No ConfirmationPhrase field exists on the DTO by design.
                },
                ct);
        }
        catch (System.OperationCanceledException) { throw; }
        catch
        {
            result = null;
        }

        if (result is null)
        {
            var unavailable = new BackendPermissionPreflightResult
            {
                EnforcementEnabled = true,
                Allowed            = false,
                Status             = "Unavailable",
                Reason             = "Backend permission validation unavailable. Dangerous operation blocked because enforcement is enabled.",
                PermissionKey      = permissionKey,
            };
            UpdateLastPreflightDisplay(unavailable, operationName);
            Errors.Add($"BackendPermissions: validate unavailable for {permissionKey}.");
            return unavailable;
        }

        if (!result.Allowed)
        {
            var denied = new BackendPermissionPreflightResult
            {
                EnforcementEnabled         = true,
                Allowed                    = false,
                Status                     = "Denied",
                Reason                     = string.IsNullOrWhiteSpace(result.Reason) ? "Denied by backend." : result.Reason,
                PermissionKey              = permissionKey,
                RequiresLocalFlag          = result.RequiresLocalFlag,
                RequiresConfirmationPhrase = result.RequiresConfirmationPhrase,
                RequiresGuardedWrapper     = result.RequiresGuardedWrapper,
            };
            UpdateLastPreflightDisplay(denied, operationName);
            return denied;
        }

        // Metadata sanity-check: a dangerous permission must require all
        // three desktop-side guards. Disagreement is a protocol violation
        // that fails closed.
        if (!result.RequiresLocalFlag || !result.RequiresConfirmationPhrase || !result.RequiresGuardedWrapper)
        {
            var mismatch = new BackendPermissionPreflightResult
            {
                EnforcementEnabled         = true,
                Allowed                    = false,
                Status                     = "MetadataMismatch",
                Reason                     = "Backend validation metadata did not confirm local flag + confirmation phrase + guarded wrapper requirements.",
                PermissionKey              = permissionKey,
                RequiresLocalFlag          = result.RequiresLocalFlag,
                RequiresConfirmationPhrase = result.RequiresConfirmationPhrase,
                RequiresGuardedWrapper     = result.RequiresGuardedWrapper,
            };
            UpdateLastPreflightDisplay(mismatch, operationName);
            Errors.Add($"BackendPermissions: metadata mismatch for {permissionKey}.");
            return mismatch;
        }

        var allowed = new BackendPermissionPreflightResult
        {
            EnforcementEnabled         = true,
            Allowed                    = true,
            Status                     = "Allowed",
            Reason                     = string.IsNullOrWhiteSpace(result.Reason) ? "Permitted by backend." : result.Reason,
            PermissionKey              = permissionKey,
            RequiresLocalFlag          = result.RequiresLocalFlag,
            RequiresConfirmationPhrase = result.RequiresConfirmationPhrase,
            RequiresGuardedWrapper     = result.RequiresGuardedWrapper,
        };
        UpdateLastPreflightDisplay(allowed, operationName);
        return allowed;
    }

    private void UpdateLastPreflightDisplay(
        BackendPermissionPreflightResult result, string operationName)
    {
        BackendPermissionEnforcementEnabled    = result.EnforcementEnabled;
        BackendPermissionEnforcementStatusText = result.EnforcementEnabled
            ? "Enabled (operator_backend_permission_enforcement_enabled=\"1\")."
            : "Disabled (operator_backend_permission_enforcement_enabled missing or \"0\").";

        BackendPermissionLastPreflightOperation = operationName ?? "";
        BackendPermissionLastPreflightKey       = result.PermissionKey ?? "";
        BackendPermissionLastPreflightStatus    = result.Status ?? "";
        BackendPermissionLastPreflightReason    = result.Reason ?? "";
        BackendPermissionLastPreflightAtUtc     = System.DateTime.UtcNow;
    }

    // ── Phase 10.19G — Backend audit-intent non-blocking call ───────────────
    //
    // Called from each Execute*CoreAsync AFTER the backend permission
    // preflight returns Allowed and BEFORE the guarded wrapper executes.
    // Default OFF: when the flag is missing/empty/"0" the method returns
    // immediately and makes NO HTTP call. When ON, the result is recorded
    // in the display state and any non-accepted/null result becomes a
    // BackendAudit: warning — it does NOT block the wrapper.
    //
    // Security: never sends a confirmation phrase; never sends tokens; the
    // request DTO has no such fields by design. Cancellation propagates.

    private async System.Threading.Tasks.Task RecordAuditIntentNonBlockingAsync(
        string permissionKey,
        string operationName,
        string? tenant,
        System.Threading.CancellationToken ct)
    {
        RefreshBackendAuditEvidenceFlagStatusTexts();
        if (!BackendAuditIntentEnabled) return;

        OperatorAuditIntentResultDto? intent = null;
        try
        {
            var req = new OperatorAuditIntentRequestDto
            {
                OperationName     = operationName,
                PermissionKey     = permissionKey,
                TenantId          = string.IsNullOrWhiteSpace(tenant) ? null : tenant,
                StoreId           = null,
                ApprovalTicketId  = null,
                Reason            = "Desktop guarded operation intent before local wrapper execution.",
                ClientRequestId   = System.Guid.NewGuid().ToString("N"),
                ClientGeneratedAt = System.DateTime.UtcNow.ToString("O"),
            };
            intent = await _operatorAuditEvidenceApi.RegisterIntentAsync(req, ct);
        }
        catch (System.OperationCanceledException) { throw; }
        catch (System.Exception ex)
        {
            Warnings.Add($"BackendAudit: {ex.Message}");
        }

        BackendAuditIntentLastOperation     = operationName;
        BackendAuditIntentLastPermissionKey = permissionKey;
        BackendAuditIntentLastAtUtc         = System.DateTime.UtcNow;

        if (intent is { Accepted: true })
        {
            BackendAuditIntentLastStatus   = "accepted";
            BackendAuditIntentLastIntentId = intent.IntentId ?? "";
            BackendAuditIntentLastReason   = intent.Reason ?? "";
        }
        else if (intent is null)
        {
            BackendAuditIntentLastStatus   = "unavailable";
            BackendAuditIntentLastIntentId = "";
            BackendAuditIntentLastReason   = "Backend unreachable or returned an error.";
            Warnings.Add($"BackendAudit: intent registration unavailable for {operationName}.");
        }
        else
        {
            BackendAuditIntentLastStatus   = "rejected";
            BackendAuditIntentLastIntentId = "";
            BackendAuditIntentLastReason   = intent.Reason ?? "Backend rejected the audit intent.";
            Warnings.Add($"BackendAudit: intent rejected for {operationName}: {BackendAuditIntentLastReason}");
        }
    }

    private void RefreshBackendAuditEvidenceFlagStatusTexts()
    {
        BackendAuditIntentEnabled = _global.Get(BackendAuditIntentFlagKey) == "1";
        BackendAuditIntentFlagStatusText = BackendAuditIntentEnabled
            ? "Enabled (operator_backend_audit_intent_enabled=\"1\")."
            : "Disabled (operator_backend_audit_intent_enabled missing or \"0\").";

        BackendEvidenceRegistrationEnabled = _global.Get(BackendEvidenceRegistrationFlagKey) == "1";
        BackendEvidenceRegistrationFlagStatusText = BackendEvidenceRegistrationEnabled
            ? "Enabled (operator_backend_evidence_registration_enabled=\"1\")."
            : "Disabled (operator_backend_evidence_registration_enabled missing or \"0\").";

        // Phase 10.19J — review UI flag state. Reads only; no HTTP.
        BackendAuditReviewUiEnabled = _global.Get(BackendAuditReviewUiFlagKey) == "1";
        BackendAuditReviewUiStatusText = BackendAuditReviewUiEnabled
            ? "Enabled (operator_backend_audit_review_ui_enabled=\"1\")."
            : "Disabled (operator_backend_audit_review_ui_enabled missing or \"0\").";

        // Phase 10.20G — permission admin read-only UI flag.
        PermissionAdminReadOnlyUiEnabled = _global.Get(PermissionAdminReadOnlyUiFlagKey) == "1";
        PermissionAdminReadOnlyUiStatusText = PermissionAdminReadOnlyUiEnabled
            ? "Enabled (operator_permission_admin_readonly_ui_enabled=\"1\")."
            : "Disabled (operator_permission_admin_readonly_ui_enabled missing or \"0\").";

        // Phase 10.20I — permission admin MUTATION UI flag. Default OFF locally;
        // backend has its own separate default-OFF flag.
        PermissionAdminMutationUiEnabled = _global.Get(PermissionAdminMutationUiFlagKey) == "1";
        PermissionAdminMutationUiStatusText = PermissionAdminMutationUiEnabled
            ? "Enabled locally (operator_permission_admin_mutation_ui_enabled=\"1\"). " +
              "Backend must also have operator.permission.admin.mutations.enabled=true for mutations to commit."
            : "Disabled (operator_permission_admin_mutation_ui_enabled missing or \"0\").";

        // Phase 10.21G — read-only authoritative-status card flag.
        PermissionAuthoritativeStatusUiEnabled =
                _global.Get(PermissionAuthoritativeStatusUiFlagKey) == "1";
        PermissionAuthoritativeStatusUiStatusText = PermissionAuthoritativeStatusUiEnabled
            ? "Enabled (operator_permission_authoritative_status_ui_enabled=\"1\"). " +
              "Read-only diagnostic card; does not change runtime permission decisions."
            : "Disabled (operator_permission_authoritative_status_ui_enabled missing or \"0\").";
    }

    // ── Phase 10.20G — Permission admin read-only commands ──────────────────
    //
    // All commands are read-only. None of them takes the dangerous-operation
    // lock, calls a guarded executor, or affects CanExecute of any dangerous
    // command. Each command short-circuits when the feature flag is OFF.

    [RelayCommand]
    private async System.Threading.Tasks.Task RefreshPermissionDefinitionsAsync()
    {
        if (PermissionAdminIsLoading) return;
        RefreshBackendAuditEvidenceFlagStatusTexts();
        if (!PermissionAdminReadOnlyUiEnabled)
        {
            PermissionAdminStatusMessage = "Disabled by local flag — no backend call made.";
            return;
        }

        PermissionAdminIsLoading = true;
        PermissionAdminStatusMessage = "Refreshing permission definitions...";
        PermissionDefinitions.Clear();

        var safeSize = NormalisePermissionAdminSize(PermissionDefinitionSize);
        PermissionDefinitionSize = safeSize;
        var safePage = System.Math.Max(0, PermissionDefinitionPage);
        PermissionDefinitionPage = safePage;

        var query = new OperatorPermissionDefinitionAdminQuery
        {
            PermissionKey = NullIfBlank(PermissionDefinitionFilterKey),
            Category      = NullIfBlank(PermissionDefinitionFilterCategory),
            Active        = PermissionDefinitionFilterActive,
            Dangerous     = PermissionDefinitionFilterDangerous,
            Page          = safePage,
            Size          = safeSize,
        };

        OperatorPermissionAdminPageDto<OperatorPermissionDefinitionAdminDto>? page = null;
        try
        {
            page = await _operatorPermissionAdminApi.GetDefinitionsAsync(query);
        }
        catch (System.OperationCanceledException) { /* swallow */ }
        catch (System.Exception ex)
        {
            PermissionAdminErrors.Add($"PermissionAdmin: {ex.Message}");
            Warnings.Add($"PermissionAdmin: {ex.Message}");
        }
        finally
        {
            PermissionAdminIsLoading = false;
        }

        if (page is null)
        {
            PermissionAdminStatusMessage = "Backend unreachable or rejected the request.";
            PermissionDefinitionHasNext = false;
            PermissionDefinitionTotalElements = 0;
            return;
        }

        PermissionDefinitionTotalElements = page.TotalElements;
        PermissionDefinitionHasNext       = page.HasNext;
        PermissionDefinitionPage          = page.Page;
        PermissionDefinitionSize          = page.Size;

        foreach (var item in page.Items)
        {
            PermissionDefinitions.Add(FormatDefinitionRow(item));
        }
        PermissionAdminStatusMessage =
            $"Definitions: {page.Items.Count} of {page.TotalElements} (page {page.Page}, size {page.Size}).";
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task PermissionDefinitionsNextPageAsync()
    {
        RefreshBackendAuditEvidenceFlagStatusTexts();
        if (!PermissionAdminReadOnlyUiEnabled || !PermissionDefinitionHasNext) return;
        PermissionDefinitionPage = System.Math.Max(0, PermissionDefinitionPage) + 1;
        await RefreshPermissionDefinitionsAsync();
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task PermissionDefinitionsPreviousPageAsync()
    {
        RefreshBackendAuditEvidenceFlagStatusTexts();
        if (!PermissionAdminReadOnlyUiEnabled || PermissionDefinitionPage <= 0) return;
        PermissionDefinitionPage = PermissionDefinitionPage - 1;
        await RefreshPermissionDefinitionsAsync();
    }

    [RelayCommand]
    private void ClearPermissionDefinitionFilters()
    {
        PermissionDefinitionFilterKey       = "";
        PermissionDefinitionFilterCategory  = "";
        PermissionDefinitionFilterActive    = null;
        PermissionDefinitionFilterDangerous = null;
        PermissionDefinitionPage            = 0;
        PermissionAdminStatusMessage        = "Definition filters cleared.";
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task RefreshRoleGrantsAsync()
    {
        if (PermissionAdminIsLoading) return;
        RefreshBackendAuditEvidenceFlagStatusTexts();
        if (!PermissionAdminReadOnlyUiEnabled)
        {
            PermissionAdminStatusMessage = "Disabled by local flag — no backend call made.";
            return;
        }

        PermissionAdminIsLoading = true;
        PermissionAdminStatusMessage = "Refreshing role grants...";
        RoleGrants.Clear();

        var safeSize = NormalisePermissionAdminSize(RoleGrantSize);
        RoleGrantSize = safeSize;
        var safePage = System.Math.Max(0, RoleGrantPage);
        RoleGrantPage = safePage;

        var query = new OperatorRoleGrantAdminQuery
        {
            Role              = NullIfBlank(RoleGrantFilterRole),
            PermissionKey     = NullIfBlank(RoleGrantFilterPermissionKey),
            TenantScopePolicy = NullIfBlank(RoleGrantFilterTenantScopePolicy),
            Active            = RoleGrantFilterActive,
            Page              = safePage,
            Size              = safeSize,
        };

        OperatorPermissionAdminPageDto<OperatorRolePermissionGrantAdminDto>? page = null;
        try { page = await _operatorPermissionAdminApi.GetRoleGrantsAsync(query); }
        catch (System.OperationCanceledException) { /* swallow */ }
        catch (System.Exception ex)
        {
            PermissionAdminErrors.Add($"PermissionAdmin: {ex.Message}");
            Warnings.Add($"PermissionAdmin: {ex.Message}");
        }
        finally { PermissionAdminIsLoading = false; }

        if (page is null)
        {
            PermissionAdminStatusMessage = "Backend unreachable or rejected the request.";
            RoleGrantHasNext = false;
            RoleGrantTotalElements = 0;
            return;
        }

        RoleGrantTotalElements = page.TotalElements;
        RoleGrantHasNext       = page.HasNext;
        RoleGrantPage          = page.Page;
        RoleGrantSize          = page.Size;
        foreach (var item in page.Items) RoleGrants.Add(FormatRoleGrantRow(item));
        PermissionAdminStatusMessage =
            $"Role grants: {page.Items.Count} of {page.TotalElements} (page {page.Page}, size {page.Size}).";
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task RoleGrantsNextPageAsync()
    {
        RefreshBackendAuditEvidenceFlagStatusTexts();
        if (!PermissionAdminReadOnlyUiEnabled || !RoleGrantHasNext) return;
        RoleGrantPage = System.Math.Max(0, RoleGrantPage) + 1;
        await RefreshRoleGrantsAsync();
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task RoleGrantsPreviousPageAsync()
    {
        RefreshBackendAuditEvidenceFlagStatusTexts();
        if (!PermissionAdminReadOnlyUiEnabled || RoleGrantPage <= 0) return;
        RoleGrantPage = RoleGrantPage - 1;
        await RefreshRoleGrantsAsync();
    }

    [RelayCommand]
    private void ClearRoleGrantFilters()
    {
        RoleGrantFilterRole              = "";
        RoleGrantFilterPermissionKey     = "";
        RoleGrantFilterTenantScopePolicy = "";
        RoleGrantFilterActive            = null;
        RoleGrantPage                    = 0;
        PermissionAdminStatusMessage     = "Role grant filters cleared.";
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task RefreshUserOverridesAsync()
    {
        if (PermissionAdminIsLoading) return;
        RefreshBackendAuditEvidenceFlagStatusTexts();
        if (!PermissionAdminReadOnlyUiEnabled)
        {
            PermissionAdminStatusMessage = "Disabled by local flag — no backend call made.";
            return;
        }

        PermissionAdminIsLoading = true;
        PermissionAdminStatusMessage = "Refreshing user overrides...";
        UserOverrides.Clear();

        var safeSize = NormalisePermissionAdminSize(UserOverrideSize);
        UserOverrideSize = safeSize;
        var safePage = System.Math.Max(0, UserOverridePage);
        UserOverridePage = safePage;

        var query = new OperatorUserOverrideAdminQuery
        {
            UserId        = TryParseLong(UserOverrideFilterUserId),
            TenantId      = NullIfBlank(UserOverrideFilterTenantId),
            StoreId       = NullIfBlank(UserOverrideFilterStoreId),
            PermissionKey = NullIfBlank(UserOverrideFilterPermissionKey),
            GrantType     = NullIfBlank(UserOverrideFilterGrantType),
            Active        = UserOverrideFilterActive,
            Expired       = UserOverrideFilterExpired,
            Page          = safePage,
            Size          = safeSize,
        };

        OperatorPermissionAdminPageDto<OperatorUserPermissionOverrideAdminDto>? page = null;
        try { page = await _operatorPermissionAdminApi.GetUserOverridesAsync(query); }
        catch (System.OperationCanceledException) { /* swallow */ }
        catch (System.Exception ex)
        {
            PermissionAdminErrors.Add($"PermissionAdmin: {ex.Message}");
            Warnings.Add($"PermissionAdmin: {ex.Message}");
        }
        finally { PermissionAdminIsLoading = false; }

        if (page is null)
        {
            PermissionAdminStatusMessage = "Backend unreachable or rejected the request.";
            UserOverrideHasNext = false;
            UserOverrideTotalElements = 0;
            return;
        }

        UserOverrideTotalElements = page.TotalElements;
        UserOverrideHasNext       = page.HasNext;
        UserOverridePage          = page.Page;
        UserOverrideSize          = page.Size;
        foreach (var item in page.Items) UserOverrides.Add(FormatUserOverrideRow(item));
        PermissionAdminStatusMessage =
            $"User overrides: {page.Items.Count} of {page.TotalElements} (page {page.Page}, size {page.Size}).";
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task UserOverridesNextPageAsync()
    {
        RefreshBackendAuditEvidenceFlagStatusTexts();
        if (!PermissionAdminReadOnlyUiEnabled || !UserOverrideHasNext) return;
        UserOverridePage = System.Math.Max(0, UserOverridePage) + 1;
        await RefreshUserOverridesAsync();
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task UserOverridesPreviousPageAsync()
    {
        RefreshBackendAuditEvidenceFlagStatusTexts();
        if (!PermissionAdminReadOnlyUiEnabled || UserOverridePage <= 0) return;
        UserOverridePage = UserOverridePage - 1;
        await RefreshUserOverridesAsync();
    }

    [RelayCommand]
    private void ClearUserOverrideFilters()
    {
        UserOverrideFilterUserId        = "";
        UserOverrideFilterTenantId      = "";
        UserOverrideFilterStoreId       = "";
        UserOverrideFilterPermissionKey = "";
        UserOverrideFilterGrantType     = "";
        UserOverrideFilterActive        = null;
        UserOverrideFilterExpired       = null;
        UserOverridePage                = 0;
        PermissionAdminStatusMessage    = "User override filters cleared.";
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task ResolveEffectivePermissionsAsync()
    {
        if (PermissionAdminIsLoading) return;
        RefreshBackendAuditEvidenceFlagStatusTexts();
        if (!PermissionAdminReadOnlyUiEnabled)
        {
            PermissionAdminStatusMessage = "Disabled by local flag — no backend call made.";
            return;
        }

        PermissionAdminIsLoading = true;
        PermissionAdminStatusMessage = "Resolving effective DB-shadow permissions...";
        EffectivePermissions.Clear();
        EffectiveDecisions.Clear();
        EffectiveAuditSource = "";
        EffectiveEnabled = "";
        EffectiveHealthy = "";
        EffectivePermissionsSource = "";
        EffectiveComparisonMatchesCode = "";

        var query = new OperatorPermissionEffectiveAdminQuery
        {
            UserId   = TryParseLong(EffectiveQueryUserId),
            TenantId = NullIfBlank(EffectiveQueryTenantId),
            StoreId  = NullIfBlank(EffectiveQueryStoreId),
        };

        OperatorPermissionEffectiveAdminDto? result = null;
        try { result = await _operatorPermissionAdminApi.GetEffectiveAsync(query); }
        catch (System.OperationCanceledException) { /* swallow */ }
        catch (System.Exception ex)
        {
            PermissionAdminErrors.Add($"PermissionAdmin: {ex.Message}");
            Warnings.Add($"PermissionAdmin: {ex.Message}");
        }
        finally { PermissionAdminIsLoading = false; }

        if (result is null)
        {
            PermissionAdminStatusMessage = "Backend unreachable or rejected the request.";
            return;
        }

        EffectiveAuditSource = result.AuditSource ?? "";
        var eff = result.EffectiveResult;
        if (eff is not null)
        {
            EffectiveEnabled = eff.Enabled ? "true" : "false";
            EffectiveHealthy = eff.Healthy ? "true" : "false";
            EffectivePermissionsSource = eff.PermissionsSource ?? "";
            EffectiveComparisonMatchesCode = eff.Comparison?.MatchesCode == true ? "true"
                : eff.Comparison is null ? "" : "false";
            foreach (var key in eff.EffectivePermissions)
            {
                var (sanitised, _) = OperatorPermissionAdminRedaction.ScrubAndTruncate(key);
                EffectivePermissions.Add(sanitised);
            }
            bool anyDecisionRedacted = false;
            foreach (var d in eff.Decisions)
            {
                EffectiveDecisions.Add(FormatEffectiveDecisionRow(d, ref anyDecisionRedacted));
            }
            if (anyDecisionRedacted)
            {
                PermissionAdminWarnings.Add("PermissionAdmin: desktop redaction applied.");
                Warnings.Add("PermissionAdmin: desktop redaction applied.");
            }
        }
        PermissionAdminStatusMessage = $"Effective view: auditSource={EffectiveAuditSource}, enabled={EffectiveEnabled}.";
    }

    [RelayCommand]
    private void ClearEffectivePermissionQuery()
    {
        EffectiveQueryUserId   = "";
        EffectiveQueryTenantId = "";
        EffectiveQueryStoreId  = "";
        EffectivePermissions.Clear();
        EffectiveDecisions.Clear();
        EffectiveAuditSource = "";
        EffectiveEnabled = "";
        EffectiveHealthy = "";
        EffectivePermissionsSource = "";
        EffectiveComparisonMatchesCode = "";
        PermissionAdminStatusMessage = "Effective query cleared.";
    }

    // ── Phase 10.20G — Display formatters and small helpers ─────────────────

    private string FormatDefinitionRow(OperatorPermissionDefinitionAdminDto d)
    {
        string activeMark = d.Active ? "[ACTIVE]" : "[INACTIVE]";
        var (key, _) = OperatorPermissionAdminRedaction.ScrubAndTruncate(d.PermissionKey ?? "");
        var (category, _) = OperatorPermissionAdminRedaction.ScrubAndTruncate(d.Category ?? "");
        return $"{activeMark} {key} | {category} | dangerous={d.Dangerous} | localFlag={d.RequiresLocalFlag} | phrase={d.RequiresConfirmationPhrase} | wrapper={d.RequiresGuardedWrapper}";
    }

    private string FormatRoleGrantRow(OperatorRolePermissionGrantAdminDto g)
    {
        string activeMark = g.Active ? "[ACTIVE]" : "[REVOKED]";
        var (role, _) = OperatorPermissionAdminRedaction.ScrubAndTruncate(g.Role ?? "");
        var (key,  _) = OperatorPermissionAdminRedaction.ScrubAndTruncate(g.PermissionKey ?? "");
        var (policy, _) = OperatorPermissionAdminRedaction.ScrubAndTruncate(g.TenantScopePolicy ?? "");
        return $"{activeMark} {role} -> {key} | {policy}";
    }

    private string FormatUserOverrideRow(OperatorUserPermissionOverrideAdminDto o)
    {
        string activeMark = o.Active ? "[ACTIVE]" : "[REVOKED]";
        var (tenant, _)   = OperatorPermissionAdminRedaction.ScrubAndTruncate(o.TenantId ?? "-");
        var (store,  _)   = OperatorPermissionAdminRedaction.ScrubAndTruncate(o.StoreId ?? "-");
        var (key,    _)   = OperatorPermissionAdminRedaction.ScrubAndTruncate(o.PermissionKey ?? "");
        var (grant,  _)   = OperatorPermissionAdminRedaction.ScrubAndTruncate(o.GrantType ?? "");
        var (reason, redacted) = OperatorPermissionAdminRedaction.ScrubAndTruncate(o.Reason ?? "");
        if (redacted)
        {
            PermissionAdminWarnings.Add("PermissionAdmin: desktop redaction applied.");
        }
        var (ticket, _) = OperatorPermissionAdminRedaction.ScrubAndTruncate(o.ApprovalTicketId ?? "");
        var expires = o.ExpiresAt.HasValue ? o.ExpiresAt.Value.ToString("u") : "-";
        return $"{activeMark} user={o.UserId} tenant={tenant} store={store} permission={key} grant={grant} expires={expires} reason=\"{reason}\" ticket={ticket}";
    }

    private string FormatEffectiveDecisionRow(OperatorEffectivePermissionDecisionDto d, ref bool redactedFlag)
    {
        var (key,    _)        = OperatorPermissionAdminRedaction.ScrubAndTruncate(d.PermissionKey ?? "");
        var (source, _)        = OperatorPermissionAdminRedaction.ScrubAndTruncate(d.DecisionSource ?? "");
        var (reason, redacted) = OperatorPermissionAdminRedaction.ScrubAndTruncate(d.Reason ?? "");
        if (redacted) redactedFlag = true;
        return $"{key} | allowed={d.Allowed} | source={source} | reason={reason}";
    }

    private static int NormalisePermissionAdminSize(int requested)
    {
        if (requested <= 0)  return 50;
        if (requested > 200) return 200;
        return requested;
    }

    private static long? TryParseLong(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (long.TryParse(raw.Trim(), out long v)) return v;
        return null;
    }

    // ── Phase 10.20I — Permission admin MUTATION commands ──────────────────
    //
    // Every command checks the local flag, runs local validation, asks the
    // operator to confirm via a Yes/No dialog, and only then calls the
    // backend wrapper. Backend may still reject if its own mutation flag
    // is off; the response's Success flag drives the UI feedback.
    //
    // None of these commands take the dangerous-operation lock, call any
    // guarded executor, change `CanExecute` of any dangerous command, or
    // touch desktop local flags / runtime DB / DB-authoritative state.

    [RelayCommand]
    private async System.Threading.Tasks.Task CreateUserOverrideAsync()
    {
        if (PermissionAdminMutationIsBusy) return;
        RefreshBackendAuditEvidenceFlagStatusTexts();
        if (!PermissionAdminMutationUiEnabled)
        {
            PermissionAdminMutationStatusMessage = "Disabled by local flag — no backend call made.";
            return;
        }

        // Local validation.
        var userId = TryParseLong(CreateOverrideUserId);
        if (userId is null)
        {
            ReportMutationValidationError("userId is required and must be numeric.");
            return;
        }
        if (string.IsNullOrWhiteSpace(CreateOverridePermissionKey))
        {
            ReportMutationValidationError("permissionKey is required.");
            return;
        }
        string grantType = (CreateOverrideGrantType ?? "").Trim().ToUpperInvariant();
        if (grantType != "ALLOW" && grantType != "DENY")
        {
            ReportMutationValidationError("grantType must be ALLOW or DENY.");
            return;
        }
        if (string.IsNullOrWhiteSpace(CreateOverrideReason))
        {
            ReportMutationValidationError("reason is required.");
            return;
        }
        if (string.IsNullOrWhiteSpace(CreateOverrideApprovalTicketId))
        {
            ReportMutationValidationError("approvalTicketId is required.");
            return;
        }
        System.DateTime? expiresAt = null;
        if (!string.IsNullOrWhiteSpace(CreateOverrideExpiresAt))
        {
            if (!System.DateTime.TryParse(CreateOverrideExpiresAt.Trim(), out var parsed))
            {
                ReportMutationValidationError("expiresAt must be a parseable ISO-8601 timestamp.");
                return;
            }
            expiresAt = System.DateTime.SpecifyKind(parsed, System.DateTimeKind.Unspecified);
        }

        bool isDangerous = DangerousPermissionKeys.Contains(CreateOverridePermissionKey.Trim());
        if (isDangerous && grantType == "ALLOW" && expiresAt is null)
        {
            ReportMutationValidationError(
                "Dangerous ALLOW overrides require expiresAt. Backend enforces a maximum of 24 hours from now.");
            return;
        }

        // Confirmation dialog.
        string warning = isDangerous && grantType == "ALLOW"
            ? "\n\nDangerous ALLOW grants must be temporary and audited. This does not bypass " +
              "desktop local flags, confirmation phrase, or guarded wrappers.\n"
            : "";
        if (!ConfirmMutation(
                $"Create operator user override?\n\n" +
                $"userId        = {userId}\n" +
                $"permissionKey = {CreateOverridePermissionKey.Trim()}\n" +
                $"grantType     = {grantType}\n" +
                $"tenantId      = {(string.IsNullOrWhiteSpace(CreateOverrideTenantId) ? "(target user's tenant)" : CreateOverrideTenantId.Trim())}\n" +
                $"storeId       = {(string.IsNullOrWhiteSpace(CreateOverrideStoreId) ? "(none)" : CreateOverrideStoreId.Trim())}\n" +
                $"expiresAt     = {(expiresAt?.ToString("u") ?? "(none)")}\n" +
                warning +
                "\nThis changes DB permission admin tables only. DB permissions are still not " +
                "authoritative; runtime permission decisions are unchanged. The backend audits " +
                "this mutation and may reject it if its own server-side flag is OFF."))
        {
            PermissionAdminMutationStatusMessage = "Cancelled by operator.";
            return;
        }

        var req = new OperatorPermissionUserOverrideCreateRequestDto
        {
            UserId           = userId,
            TenantId         = NullIfBlank(CreateOverrideTenantId),
            StoreId          = NullIfBlank(CreateOverrideStoreId),
            PermissionKey    = CreateOverridePermissionKey.Trim(),
            GrantType        = grantType,
            ExpiresAt        = expiresAt,
            Reason           = CreateOverrideReason.Trim(),
            ApprovalTicketId = CreateOverrideApprovalTicketId.Trim(),
            RequestId        = System.Guid.NewGuid().ToString("N"),
        };
        await RunMutationAsync(
            "Create user override",
            () => _operatorPermissionAdminMutationApi.CreateUserOverrideAsync(req),
            item => FormatUserOverrideItemSummary(item),
            refreshList: RefreshUserOverridesAsync);
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task RevokeUserOverrideAsync()
    {
        if (PermissionAdminMutationIsBusy) return;
        RefreshBackendAuditEvidenceFlagStatusTexts();
        if (!PermissionAdminMutationUiEnabled)
        {
            PermissionAdminMutationStatusMessage = "Disabled by local flag — no backend call made.";
            return;
        }

        var id = TryParseLong(RevokeOverrideId);
        if (id is null)
        {
            ReportMutationValidationError("Override id is required and must be numeric.");
            return;
        }
        if (string.IsNullOrWhiteSpace(RevokeOverrideReason))
        {
            ReportMutationValidationError("reason is required.");
            return;
        }
        if (string.IsNullOrWhiteSpace(RevokeOverrideApprovalTicketId))
        {
            ReportMutationValidationError("approvalTicketId is required.");
            return;
        }
        if (!ConfirmMutation(
                $"Revoke operator user override id={id}?\n\n" +
                "Soft-revoke only — the row remains for audit history. The backend audits " +
                "this mutation and may reject it if its own server-side flag is OFF."))
        {
            PermissionAdminMutationStatusMessage = "Cancelled by operator.";
            return;
        }
        var req = new OperatorPermissionUserOverrideRevokeRequestDto
        {
            Reason           = RevokeOverrideReason.Trim(),
            ApprovalTicketId = RevokeOverrideApprovalTicketId.Trim(),
            RequestId        = System.Guid.NewGuid().ToString("N"),
        };
        await RunMutationAsync(
            "Revoke user override",
            () => _operatorPermissionAdminMutationApi.RevokeUserOverrideAsync(id.Value, req),
            item => FormatUserOverrideItemSummary(item),
            refreshList: RefreshUserOverridesAsync);
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task CreateRoleGrantAsync()
    {
        if (PermissionAdminMutationIsBusy) return;
        RefreshBackendAuditEvidenceFlagStatusTexts();
        if (!PermissionAdminMutationUiEnabled)
        {
            PermissionAdminMutationStatusMessage = "Disabled by local flag — no backend call made.";
            return;
        }
        if (string.IsNullOrWhiteSpace(CreateRoleGrantRole))
        {
            ReportMutationValidationError("role is required.");
            return;
        }
        if (string.IsNullOrWhiteSpace(CreateRoleGrantPermissionKey))
        {
            ReportMutationValidationError("permissionKey is required.");
            return;
        }
        if (string.IsNullOrWhiteSpace(CreateRoleGrantTenantScopePolicy))
        {
            ReportMutationValidationError("tenantScopePolicy is required.");
            return;
        }
        if (string.IsNullOrWhiteSpace(CreateRoleGrantReason))
        {
            ReportMutationValidationError("reason is required.");
            return;
        }
        if (string.IsNullOrWhiteSpace(CreateRoleGrantApprovalTicketId))
        {
            ReportMutationValidationError("approvalTicketId is required.");
            return;
        }
        if (!ConfirmMutation(
                $"Create role grant?\n\n" +
                $"role              = {CreateRoleGrantRole.Trim().ToUpperInvariant()}\n" +
                $"permissionKey     = {CreateRoleGrantPermissionKey.Trim()}\n" +
                $"tenantScopePolicy = {CreateRoleGrantTenantScopePolicy.Trim().ToUpperInvariant()}\n\n" +
                "The backend allows only GLOBAL_ADMIN to create role grants in this phase. " +
                "DB permissions are still not authoritative; runtime decisions are unchanged. " +
                "Audited by backend."))
        {
            PermissionAdminMutationStatusMessage = "Cancelled by operator.";
            return;
        }
        var req = new OperatorPermissionRoleGrantCreateRequestDto
        {
            Role              = CreateRoleGrantRole.Trim().ToUpperInvariant(),
            PermissionKey     = CreateRoleGrantPermissionKey.Trim(),
            TenantScopePolicy = CreateRoleGrantTenantScopePolicy.Trim().ToUpperInvariant(),
            Reason            = CreateRoleGrantReason.Trim(),
            ApprovalTicketId  = CreateRoleGrantApprovalTicketId.Trim(),
            RequestId         = System.Guid.NewGuid().ToString("N"),
        };
        await RunMutationAsync(
            "Create role grant",
            () => _operatorPermissionAdminMutationApi.CreateRoleGrantAsync(req),
            item => FormatRoleGrantItemSummary(item),
            refreshList: RefreshRoleGrantsAsync);
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task RevokeRoleGrantAsync()
    {
        if (PermissionAdminMutationIsBusy) return;
        RefreshBackendAuditEvidenceFlagStatusTexts();
        if (!PermissionAdminMutationUiEnabled)
        {
            PermissionAdminMutationStatusMessage = "Disabled by local flag — no backend call made.";
            return;
        }
        var id = TryParseLong(RevokeRoleGrantId);
        if (id is null)
        {
            ReportMutationValidationError("Role grant id is required and must be numeric.");
            return;
        }
        if (string.IsNullOrWhiteSpace(RevokeRoleGrantReason))
        {
            ReportMutationValidationError("reason is required.");
            return;
        }
        if (string.IsNullOrWhiteSpace(RevokeRoleGrantApprovalTicketId))
        {
            ReportMutationValidationError("approvalTicketId is required.");
            return;
        }
        if (!ConfirmMutation(
                $"Revoke role grant id={id}?\n\n" +
                "Soft-revoke only — the row remains for audit history. Backend allows only " +
                "GLOBAL_ADMIN to revoke role grants."))
        {
            PermissionAdminMutationStatusMessage = "Cancelled by operator.";
            return;
        }
        var req = new OperatorPermissionRoleGrantRevokeRequestDto
        {
            Reason           = RevokeRoleGrantReason.Trim(),
            ApprovalTicketId = RevokeRoleGrantApprovalTicketId.Trim(),
            RequestId        = System.Guid.NewGuid().ToString("N"),
        };
        await RunMutationAsync(
            "Revoke role grant",
            () => _operatorPermissionAdminMutationApi.RevokeRoleGrantAsync(id.Value, req),
            item => FormatRoleGrantItemSummary(item),
            refreshList: RefreshRoleGrantsAsync);
    }

    [RelayCommand]
    private void ClearUserOverrideCreateForm()
    {
        CreateOverrideUserId          = "";
        CreateOverrideTenantId        = "";
        CreateOverrideStoreId         = "";
        CreateOverridePermissionKey   = "";
        CreateOverrideGrantType       = "";
        CreateOverrideExpiresAt       = "";
        CreateOverrideReason          = "";
        CreateOverrideApprovalTicketId= "";
        PermissionAdminMutationStatusMessage = "Create-override form cleared.";
    }

    [RelayCommand]
    private void ClearUserOverrideRevokeForm()
    {
        RevokeOverrideId               = "";
        RevokeOverrideReason           = "";
        RevokeOverrideApprovalTicketId = "";
        PermissionAdminMutationStatusMessage = "Revoke-override form cleared.";
    }

    [RelayCommand]
    private void ClearRoleGrantCreateForm()
    {
        CreateRoleGrantRole              = "";
        CreateRoleGrantPermissionKey     = "";
        CreateRoleGrantTenantScopePolicy = "";
        CreateRoleGrantReason            = "";
        CreateRoleGrantApprovalTicketId  = "";
        PermissionAdminMutationStatusMessage = "Create-role-grant form cleared.";
    }

    [RelayCommand]
    private void ClearRoleGrantRevokeForm()
    {
        RevokeRoleGrantId               = "";
        RevokeRoleGrantReason           = "";
        RevokeRoleGrantApprovalTicketId = "";
        PermissionAdminMutationStatusMessage = "Revoke-role-grant form cleared.";
    }

    // ── Phase 10.20I — Helpers ──────────────────────────────────────────────

    private async System.Threading.Tasks.Task RunMutationAsync<TItem>(
        string operationLabel,
        System.Func<System.Threading.Tasks.Task<OperatorPermissionAdminMutationResponseDto<TItem>?>> call,
        System.Func<TItem, string> summarise,
        System.Func<System.Threading.Tasks.Task>? refreshList)
        where TItem : class
    {
        PermissionAdminMutationIsBusy = true;
        PermissionAdminMutationStatusMessage = $"{operationLabel}: contacting backend...";

        OperatorPermissionAdminMutationResponseDto<TItem>? resp = null;
        try
        {
            resp = await call();
        }
        catch (System.OperationCanceledException) { /* swallow */ }
        catch (System.Exception ex)
        {
            ReportMutationError($"{operationLabel}: {ex.Message}");
        }
        finally
        {
            PermissionAdminMutationIsBusy = false;
        }

        if (resp is null)
        {
            PermissionAdminMutationStatusMessage = $"{operationLabel}: backend unreachable.";
            ReportMutationError($"{operationLabel}: backend unreachable.");
            UpdateLastMutationDisplay(false, null, "Backend unreachable.", "", null);
            return;
        }

        if (resp.Success)
        {
            string itemSummary = resp.Item is null ? "" : summarise(resp.Item);
            var (sanitisedSummary, _) = OperatorPermissionAdminRedaction.ScrubAndTruncate(itemSummary);
            UpdateLastMutationDisplay(true, resp.AuditSource, resp.Message ?? "Mutation succeeded.",
                sanitisedSummary, System.DateTime.UtcNow);
            PermissionAdminMutationStatusMessage = $"{operationLabel}: success.";
            if (refreshList is not null)
            {
                try { await refreshList(); }
                catch (System.OperationCanceledException) { }
                catch (System.Exception ex)
                {
                    ReportMutationError($"List refresh after {operationLabel} failed: {ex.Message}");
                }
            }
        }
        else
        {
            var (sanitisedMsg, _) = OperatorPermissionAdminRedaction.ScrubAndTruncate(resp.Message ?? "");
            string codeStr = string.IsNullOrEmpty(resp.BackendErrorCode) ? "" : $" ({resp.BackendErrorCode})";
            string statusStr = resp.BackendStatusCode > 0 ? $"HTTP {resp.BackendStatusCode}" : "";
            string composed = string.Join(" ", new[] { statusStr, codeStr, sanitisedMsg }).Trim();
            ReportMutationError($"{operationLabel}: {composed}");
            UpdateLastMutationDisplay(false, resp.AuditSource, sanitisedMsg, "", System.DateTime.UtcNow);
            PermissionAdminMutationStatusMessage = $"{operationLabel}: backend rejected the request.";
        }
    }

    private void UpdateLastMutationDisplay(bool success, string? auditSource, string message,
                                           string itemSummary, System.DateTime? atUtc)
    {
        PermissionAdminLastMutationStatus      = success ? "success" : "failed";
        PermissionAdminLastMutationAuditSource = auditSource ?? "";
        PermissionAdminLastMutationMessage     = message ?? "";
        PermissionAdminLastMutationItemSummary = itemSummary ?? "";
        PermissionAdminLastMutationAtUtc       = atUtc;
    }

    private void ReportMutationValidationError(string message)
    {
        PermissionAdminMutationStatusMessage = message;
        PermissionAdminMutationErrors.Add($"PermissionAdminMutation: {message}");
        Errors.Add($"PermissionAdminMutation: {message}");
    }

    private void ReportMutationError(string message)
    {
        PermissionAdminMutationErrors.Add($"PermissionAdminMutation: {message}");
        Errors.Add($"PermissionAdminMutation: {message}");
    }

    /// <summary>
    /// Yes/No confirmation dialog. Returns true when the operator clicked
    /// Yes. The MessageBox runs on the calling (UI) thread; the command
    /// methods are invoked from the dispatcher so MessageBox is safe.
    /// </summary>
    private static bool ConfirmMutation(string message)
    {
        var result = System.Windows.MessageBox.Show(
            message,
            "Confirm operator permission mutation",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        return result == System.Windows.MessageBoxResult.Yes;
    }

    private static string FormatUserOverrideItemSummary(OperatorUserPermissionOverrideAdminDto item)
    {
        string activeMark = item.Active ? "[ACTIVE]" : "[REVOKED]";
        return $"{activeMark} id={item.Id} user={item.UserId} tenant={item.TenantId ?? "-"} "
             + $"permission={item.PermissionKey} grant={item.GrantType} "
             + $"expires={(item.ExpiresAt.HasValue ? item.ExpiresAt.Value.ToString("u") : "-")}";
    }

    private static string FormatRoleGrantItemSummary(OperatorRolePermissionGrantAdminDto item)
    {
        string activeMark = item.Active ? "[ACTIVE]" : "[REVOKED]";
        return $"{activeMark} id={item.Id} {item.Role} -> {item.PermissionKey} ({item.TenantScopePolicy})";
    }

    // ── Phase 10.19J — Backend operator audit/evidence review commands ─────
    //
    // Read-only. Default OFF by feature flag. Every command short-circuits
    // when the flag is OFF. Failures NEVER bypass local guards, never
    // mutate audit records, never touch any guarded wrapper, never take
    // the dangerous-operation lock.

    [RelayCommand]
    private async System.Threading.Tasks.Task RefreshBackendAuditReviewEventsAsync()
    {
        if (BackendAuditReviewIsLoading) return;
        RefreshBackendAuditEvidenceFlagStatusTexts();
        if (!BackendAuditReviewUiEnabled)
        {
            BackendAuditReviewStatusMessage = "Disabled by local flag — no backend call made.";
            return;
        }

        BackendAuditReviewIsLoading = true;
        BackendAuditReviewStatusMessage = "Refreshing audit events...";
        BackendAuditReviewEvents.Clear();

        int safeSize = NormaliseAuditReviewSize(BackendAuditReviewSize);
        BackendAuditReviewSize = safeSize;
        int safePage = System.Math.Max(0, BackendAuditReviewPage);
        BackendAuditReviewPage = safePage;

        var query = new OperatorAuditReviewQuery
        {
            TenantId      = NullIfBlank(BackendAuditReviewFilterTenantId),
            EntityType    = NullIfBlank(BackendAuditReviewFilterEntityType),
            Action        = NullIfBlank(BackendAuditReviewFilterAction),
            OperationName = NullIfBlank(BackendAuditReviewFilterOperationName),
            PermissionKey = NullIfBlank(BackendAuditReviewFilterPermissionKey),
            Accepted      = BackendAuditReviewFilterAccepted,
            Page          = safePage,
            Size          = safeSize,
        };

        OperatorAuditEventPageDto? page = null;
        try
        {
            page = await _operatorAuditReviewApi.GetEventsAsync(query);
        }
        catch (System.OperationCanceledException) { /* swallow — read-only path */ }
        catch (System.Exception ex)
        {
            BackendAuditReviewErrors.Add($"BackendAuditReview: {ex.Message}");
            Warnings.Add($"BackendAuditReview: {ex.Message}");
        }
        finally
        {
            BackendAuditReviewIsLoading = false;
        }

        if (page is null)
        {
            BackendAuditReviewStatusMessage = "Backend unreachable or rejected the request.";
            BackendAuditReviewHasNext       = false;
            BackendAuditReviewTotalElements = 0;
            return;
        }

        BackendAuditReviewTotalElements = page.TotalElements;
        BackendAuditReviewHasNext       = page.HasNext;
        BackendAuditReviewPage          = page.Page;
        BackendAuditReviewSize          = page.Size;

        foreach (var item in page.Items)
        {
            BackendAuditReviewEvents.Add(FormatSummary(item));
        }
        BackendAuditReviewStatusMessage =
            $"Fetched {page.Items.Count} of {page.TotalElements} event(s), page {page.Page} (size {page.Size}).";
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task BackendAuditReviewNextPageAsync()
    {
        RefreshBackendAuditEvidenceFlagStatusTexts();
        if (!BackendAuditReviewUiEnabled)
        {
            BackendAuditReviewStatusMessage = "Disabled by local flag — no backend call made.";
            return;
        }
        if (!BackendAuditReviewHasNext) return;
        BackendAuditReviewPage = System.Math.Max(0, BackendAuditReviewPage) + 1;
        await RefreshBackendAuditReviewEventsAsync();
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task BackendAuditReviewPreviousPageAsync()
    {
        RefreshBackendAuditEvidenceFlagStatusTexts();
        if (!BackendAuditReviewUiEnabled)
        {
            BackendAuditReviewStatusMessage = "Disabled by local flag — no backend call made.";
            return;
        }
        if (BackendAuditReviewPage <= 0) return;
        BackendAuditReviewPage = BackendAuditReviewPage - 1;
        await RefreshBackendAuditReviewEventsAsync();
    }

    [RelayCommand]
    private void ClearBackendAuditReviewFilters()
    {
        BackendAuditReviewFilterTenantId       = "";
        BackendAuditReviewFilterEntityType     = "";
        BackendAuditReviewFilterAction         = "";
        BackendAuditReviewFilterOperationName  = "";
        BackendAuditReviewFilterPermissionKey  = "";
        BackendAuditReviewFilterAccepted       = null;
        BackendAuditReviewPage                 = 0;
        BackendAuditReviewStatusMessage        = "Filters cleared.";
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task LookupBackendAuditEventAsync()
    {
        await LookupAuditDetailAsync(
            BackendAuditReviewLookupEventId,
            id => _operatorAuditReviewApi.GetEventAsync(id),
            "event");
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task LookupBackendAuditIntentAsync()
    {
        await LookupAuditDetailAsync(
            BackendAuditReviewLookupIntentId,
            id => _operatorAuditReviewApi.GetIntentAsync(id),
            "intent");
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task LookupBackendAuditEvidenceAsync()
    {
        await LookupAuditDetailAsync(
            BackendAuditReviewLookupRegistrationId,
            id => _operatorAuditReviewApi.GetEvidenceAsync(id),
            "evidence");
    }

    // ── Helpers (Phase 10.19J) ──────────────────────────────────────────────

    private async System.Threading.Tasks.Task LookupAuditDetailAsync(
        string rawId,
        System.Func<string, System.Threading.Tasks.Task<OperatorAuditEventDetailDto?>> fetch,
        string kind)
    {
        if (BackendAuditReviewIsLoading) return;
        RefreshBackendAuditEvidenceFlagStatusTexts();
        if (!BackendAuditReviewUiEnabled)
        {
            BackendAuditReviewStatusMessage = "Disabled by local flag — no backend call made.";
            return;
        }
        if (string.IsNullOrWhiteSpace(rawId))
        {
            BackendAuditReviewStatusMessage = $"No {kind} id entered.";
            return;
        }

        BackendAuditReviewIsLoading = true;
        BackendAuditReviewStatusMessage = $"Looking up {kind} {rawId.Trim()}...";

        OperatorAuditEventDetailDto? detail = null;
        try
        {
            detail = await fetch(rawId.Trim());
        }
        catch (System.OperationCanceledException) { /* swallow */ }
        catch (System.Exception ex)
        {
            BackendAuditReviewErrors.Add($"BackendAuditReview: {ex.Message}");
            Warnings.Add($"BackendAuditReview: {ex.Message}");
        }
        finally
        {
            BackendAuditReviewIsLoading = false;
        }

        if (detail is null)
        {
            BackendAuditReviewStatusMessage = $"No {kind} found, or backend unreachable.";
            ClearSelectedAuditDetail();
            return;
        }

        ApplySelectedAuditDetail(detail);
        BackendAuditReviewStatusMessage = $"{kind} found.";
    }

    private void ApplySelectedAuditDetail(OperatorAuditEventDetailDto detail)
    {
        BackendAuditReviewSelectedEventId       = detail.EventId?.ToString() ?? "";
        BackendAuditReviewSelectedAction        = detail.Action ?? "";
        BackendAuditReviewSelectedEntityType    = detail.EntityType ?? "";
        BackendAuditReviewSelectedEntityId      = detail.EntityId ?? "";
        BackendAuditReviewSelectedTenantId      = detail.TenantId ?? "";
        BackendAuditReviewSelectedUsername      = detail.Username ?? "";
        BackendAuditReviewSelectedRole          = detail.Role ?? "";
        BackendAuditReviewSelectedOperationName = detail.OperationName ?? "";
        BackendAuditReviewSelectedPermissionKey = detail.PermissionKey ?? "";
        BackendAuditReviewSelectedAccepted      = detail.Accepted.HasValue
            ? (detail.Accepted.Value ? "true" : "false") : "";
        BackendAuditReviewSelectedAuditSource   = detail.AuditSource ?? "";
        BackendAuditReviewSelectedReviewSource  = detail.ReviewSource ?? "";
        BackendAuditReviewSelectedCreatedAt     = detail.CreatedAt.HasValue
            ? detail.CreatedAt.Value.ToString("u") : "";

        var (sanitized, desktopRedacted) = OperatorAuditReviewRedaction.Sanitize(detail.Metadata);
        BackendAuditReviewSelectedRedacted = detail.Redacted || desktopRedacted ? "true" : "false";
        if (desktopRedacted)
        {
            BackendAuditReviewWarnings.Add(
                "BackendAuditReview: desktop redaction applied to returned metadata.");
            Warnings.Add(
                "BackendAuditReview: desktop redaction applied to returned metadata.");
        }
        BackendAuditReviewSelectedMetadata.Clear();
        foreach (var line in OperatorAuditReviewRedaction.FlattenForDisplay(sanitized))
        {
            BackendAuditReviewSelectedMetadata.Add(line);
        }
    }

    private void ClearSelectedAuditDetail()
    {
        BackendAuditReviewSelectedEventId       = "";
        BackendAuditReviewSelectedAction        = "";
        BackendAuditReviewSelectedEntityType    = "";
        BackendAuditReviewSelectedEntityId      = "";
        BackendAuditReviewSelectedTenantId      = "";
        BackendAuditReviewSelectedUsername      = "";
        BackendAuditReviewSelectedRole          = "";
        BackendAuditReviewSelectedOperationName = "";
        BackendAuditReviewSelectedPermissionKey = "";
        BackendAuditReviewSelectedAccepted      = "";
        BackendAuditReviewSelectedAuditSource   = "";
        BackendAuditReviewSelectedRedacted      = "";
        BackendAuditReviewSelectedReviewSource  = "";
        BackendAuditReviewSelectedCreatedAt     = "";
        BackendAuditReviewSelectedMetadata.Clear();
    }

    private static string FormatSummary(OperatorAuditEventSummaryDto s)
    {
        var accepted = s.Accepted.HasValue ? (s.Accepted.Value ? "accepted" : "denied") : "?";
        var ts       = s.CreatedAt.HasValue ? s.CreatedAt.Value.ToString("u") : "";
        return $"[{s.EventId}] {ts} {s.Action} ({accepted}) tenant={s.TenantId} entity={s.EntityType}:{s.EntityId}";
    }

    private static int NormaliseAuditReviewSize(int requested)
    {
        if (requested <= 0)   return 50;
        if (requested > 200)  return 200;
        return requested;
    }

    private static string? NullIfBlank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ── Summary composition ─────────────────────────────────────────────────

    private static string ComposeAuditSummary(TenantMigrationDryRunReport r)
    {
        if (!r.SourceDbExists)
            return "Source pos.db missing — nothing to audit.";

        var tenantCount = r.Tenants.Count;
        var sb = new System.Text.StringBuilder();
        sb.Append(tenantCount).Append(" tenant(s) discovered");
        if (r.UntaggedSalesCount > 0)
            sb.Append($"; {r.UntaggedSalesCount} untagged sale(s) ({r.UntaggedPendingCount} still pending)");
        if (r.TenantSuffixedSettingKeys.Count > 0)
            sb.Append($"; {r.TenantSuffixedSettingKeys.Count} tenant-suffixed setting key(s)");
        if (r.Observations.Count > 0)
            sb.Append('\n').Append(string.Join("\n", r.Observations));
        return sb.ToString();
    }

    private static string ComposeVerificationSummary(MigrationVerificationReport r)
    {
        if (!r.SourceDbExists)
            return "Source pos.db missing — verification not applicable.";
        if (!r.GlobalMarkerPresent)
            return "Global migration marker not present — migration has not been run yet.";

        var sb = new System.Text.StringBuilder();
        sb.Append($"AllVerified={r.AllVerified}; tenants={r.Tenants.Count}; ")
          .Append($"orphan source rows={r.OrphanCountInSource}");
        var failed = r.Tenants.Where(t => !t.Verified).ToList();
        if (failed.Count > 0)
        {
            sb.Append("\nUnverified tenants:");
            foreach (var t in failed)
                sb.Append($"\n  - {t.Subdomain}: {string.Join("; ", t.Issues)}");
        }
        return sb.ToString();
    }

    // ── Phase 10.21G — Permission authoritative-status commands ─────────────
    //
    // Read-only. Local flag must be ON. No mutation. No execution. No
    // dangerous-operation lock usage. No phrase / token sent or displayed.

    [RelayCommand]
    private async System.Threading.Tasks.Task RefreshPermissionAuthoritativeStatusAsync()
    {
        if (PermissionAuthoritativeStatusIsLoading) return;
        RefreshBackendAuditEvidenceFlagStatusTexts();
        if (!PermissionAuthoritativeStatusUiEnabled)
        {
            PermissionAuthoritativeStatusMessage =
                "Disabled. Set operator_permission_authoritative_status_ui_enabled to \"1\" to enable the card.";
            return;
        }

        PermissionAuthoritativeStatusIsLoading = true;
        PermissionAuthoritativeStatusMessage = "Yangilanmoqda...";

        try
        {
            var resp = await _operatorPermissionAdminApi.GetAuthoritativeStatusAsync();
            if (resp == null)
            {
                PermissionAuthoritativeStatusMessage =
                    "Backend authoritative-status call failed (network / auth / 403). " +
                    "No runtime decision was changed by this attempt.";
                PermissionAuthoritativeErrors.Clear();
                PermissionAuthoritativeErrors.Add("Backend authoritative-status request returned null.");
                ClearPermissionAuthoritativeStatusBindings();
                return;
            }

            ApplyPermissionAuthoritativeStatus(resp);
            PermissionAuthoritativeStatusMessage = "Tayyor.";
        }
        catch (System.Exception ex)
        {
            var (safe, _) = OperatorPermissionAdminRedaction.ScrubAndTruncate(ex.Message);
            PermissionAuthoritativeStatusMessage =
                "Unexpected error refreshing authoritative status.";
            PermissionAuthoritativeErrors.Clear();
            PermissionAuthoritativeErrors.Add("Refresh exception: " + safe);
            ClearPermissionAuthoritativeStatusBindings();
        }
        finally
        {
            PermissionAuthoritativeStatusIsLoading = false;
        }
    }

    [RelayCommand]
    private void ClearPermissionAuthoritativeStatus()
    {
        ClearPermissionAuthoritativeStatusBindings();
        PermissionAuthoritativeStatusMessage = "Cleared.";
    }

    private void ClearPermissionAuthoritativeStatusBindings()
    {
        PermissionAuthoritativePermissionsSource         = "";
        PermissionAuthoritativeReadOnlyEnabled           = "";
        PermissionAuthoritativeDangerousPreflightEnabled = "";
        PermissionAuthoritativeDangerousEnabled          = "";
        PermissionAuthoritativeFailOnMismatch            = "";
        PermissionAuthoritativeAllowCodeFallbackReadOnly = "";
        PermissionAuthoritativeParityHealthy             = "";
        PermissionAuthoritativeDangerousPreflightHealthy = "";
        PermissionAuthoritativeReadyForReadOnly          = "";
        PermissionAuthoritativeReadyForDangerous         = "";
        PermissionAuthoritativeBlockerCount              = "";
        PermissionAuthoritativeWarningCount              = "";
        PermissionAuthoritativeInfoCount                 = "";
        PermissionAuthoritativeGeneratedAt               = null;
        PermissionAuthoritativeFlags.Clear();
        PermissionAuthoritativeReadiness.Clear();
        PermissionAuthoritativeRisks.Clear();
        PermissionAuthoritativeIssues.Clear();
        PermissionAuthoritativeErrors.Clear();
        PermissionAuthoritativeWarnings.Clear();
    }

    private void ApplyPermissionAuthoritativeStatus(
            PosSystem.Core.DTOs.OperatorPermissionAuthoritativeStatusDto resp)
    {
        ClearPermissionAuthoritativeStatusBindings();

        PermissionAuthoritativeGeneratedAt               = resp.GeneratedAt;
        PermissionAuthoritativePermissionsSource         =
                OperatorPermissionAdminRedaction.ScrubAndTruncate(resp.PermissionsSource).Sanitized;
        PermissionAuthoritativeReadOnlyEnabled           = resp.ReadOnlyAuthoritativeEnabled ? "ON" : "OFF";
        PermissionAuthoritativeDangerousPreflightEnabled = resp.DangerousPreflightEnabled ? "ON" : "OFF";
        PermissionAuthoritativeDangerousEnabled          = resp.DangerousAuthoritativeEnabled ? "ON" : "OFF";
        PermissionAuthoritativeFailOnMismatch            = resp.FailOnMismatch ? "true" : "false";
        PermissionAuthoritativeAllowCodeFallbackReadOnly = resp.AllowCodeFallbackReadOnly ? "true" : "false";
        PermissionAuthoritativeParityHealthy             = resp.ParityHealthy ? "true" : "false";
        PermissionAuthoritativeDangerousPreflightHealthy = resp.DangerousPreflightHealthy ? "true" : "false";
        PermissionAuthoritativeReadyForReadOnly          = resp.ReadyForReadOnlyAuthoritative ? "true" : "false";
        PermissionAuthoritativeReadyForDangerous         = resp.ReadyForDangerousAuthoritative ? "true" : "false";
        PermissionAuthoritativeBlockerCount              = resp.BlockerCount.ToString();
        PermissionAuthoritativeWarningCount              = resp.WarningCount.ToString();
        PermissionAuthoritativeInfoCount                 = resp.InfoCount.ToString();

        if (resp.Flags != null)
        {
            foreach (var f in resp.Flags)
            {
                var name = OperatorPermissionAdminRedaction.ScrubAndTruncate(f.FlagName).Sanitized;
                var scope = OperatorPermissionAdminRedaction.ScrubAndTruncate(f.Scope).Sanitized;
                var current = f.CurrentValue.HasValue
                        ? (f.CurrentValue.Value ? "ON" : "OFF")
                        : "(desktop-side)";
                var rec = OperatorPermissionAdminRedaction.ScrubAndTruncate(f.RecommendedProductionState).Sanitized;
                var risk = OperatorPermissionAdminRedaction.ScrubAndTruncate(f.Risk).Sanitized;
                PermissionAuthoritativeFlags.Add(
                        $"[{scope}] {name} = {current} (default={(f.DefaultValue ? "ON" : "OFF")}; " +
                        $"recommended={rec}; risk={risk})");
            }
        }

        if (resp.Readiness != null)
        {
            PermissionAuthoritativeReadiness.Add(
                    "parityHealthy="                + (resp.Readiness.ParityHealthy ? "true" : "false") +
                    "; parityEvaluated="            + (resp.Readiness.ParityEvaluated ? "true" : "false"));
            PermissionAuthoritativeReadiness.Add(
                    "dangerousPreflightHealthy="    + (resp.Readiness.DangerousPreflightHealthy ? "true" : "false") +
                    "; dangerousPreflightEvaluated="+ (resp.Readiness.DangerousPreflightEvaluated ? "true" : "false"));
            PermissionAuthoritativeReadiness.Add(
                    "readyForReadOnlyAuthoritative="  + (resp.Readiness.ReadyForReadOnlyAuthoritative ? "true" : "false") +
                    "; readyForDangerousAuthoritative=" + (resp.Readiness.ReadyForDangerousAuthoritative ? "true" : "false"));
        }

        if (resp.Risks != null)
        {
            PermissionAuthoritativeRisks.Add("dangerousAuthoritativeWithoutPreflight="
                    + (resp.Risks.DangerousAuthoritativeWithoutPreflight ? "BLOCKER" : "ok"));
            PermissionAuthoritativeRisks.Add("dangerousAuthoritativeWithUnhealthyPreflight="
                    + (resp.Risks.DangerousAuthoritativeWithUnhealthyPreflight ? "BLOCKER" : "ok"));
            PermissionAuthoritativeRisks.Add("readOnlyAuthoritativeWithUnhealthyParity="
                    + (resp.Risks.ReadOnlyAuthoritativeWithUnhealthyParity ? "BLOCKER" : "ok"));
            PermissionAuthoritativeRisks.Add("failOnMismatchDisabledWhileAuthoritative="
                    + (resp.Risks.FailOnMismatchDisabledWhileAuthoritative ? "BLOCKER" : "ok"));
            PermissionAuthoritativeRisks.Add("codeFallbackReadOnlyWhileAuthoritative="
                    + (resp.Risks.CodeFallbackReadOnlyWhileAuthoritative ? "WARNING" : "ok"));
        }

        if (resp.Issues != null)
        {
            foreach (var i in resp.Issues)
            {
                var sev = i.Severity ?? "?";
                var cat = i.Category ?? "?";
                var msg = OperatorPermissionAdminRedaction.ScrubAndTruncate(i.Message).Sanitized;
                var rec = OperatorPermissionAdminRedaction.ScrubAndTruncate(i.Recommendation).Sanitized;
                PermissionAuthoritativeIssues.Add($"[{sev}] [{cat}] {msg} — Recommendation: {rec}");
            }
        }

        if (resp.Errors != null)
        {
            foreach (var e in resp.Errors)
            {
                var (safe, _) = OperatorPermissionAdminRedaction.ScrubAndTruncate(e);
                PermissionAuthoritativeErrors.Add(safe);
            }
        }
    }

    // ── Phase 10.22E — Operator Evidence Bundle Local Export ────────────────
    //
    // Local-only ZIP + manifest generator. NO backend HTTP calls in this phase.
    // The card is hidden behind the local default-OFF flag
    // `operator_evidence_bundle_export_ui_enabled`. The pipeline mirrors the
    // backend Phase 10.22D validators (path safety, MIME / magic, redaction
    // scan) so anything the desktop accepts will round-trip cleanly when
    // Phase 10.22F wires the upload UI.

    [ObservableProperty]
    private bool _evidenceExportFlagEnabled;

    [ObservableProperty]
    private string _evidenceExportFlagStatusText =
        $"Disabled ({Services.EvidenceBundleExport.EvidenceBundleExportService.LocalFlagKey} missing or \"0\").";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ValidateEvidenceBundleFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateEvidenceBundleZipCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenEvidenceBundleOutputFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearEvidenceBundleExportCommand))]
    private string _evidenceExportSourceFolderDisplay = "";

    private string _evidenceExportSourceFolderAbsolute = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenEvidenceBundleOutputFolderCommand))]
    private string _evidenceExportOutputFolderDisplay = "";

    private string _evidenceExportOutputFolderAbsolute = "";

    [ObservableProperty]
    private string _evidenceExportOutputZipDisplay = "";

    [ObservableProperty]
    private string _evidenceExportManifestDisplay = "";

    [ObservableProperty]
    private string _evidenceExportEvidenceType = "PILOT_EVIDENCE";

    [ObservableProperty]
    private string _evidenceExportEnvironment = "STAGING";

    [ObservableProperty]
    private string _evidenceExportPhase = "";

    [ObservableProperty]
    private string _evidenceExportTenantId = "";

    [ObservableProperty]
    private string _evidenceExportStoreId = "";

    [ObservableProperty]
    private int? _evidenceExportWaveNumber;

    [ObservableProperty]
    private bool _evidenceExportAllowOverwrite;

    [ObservableProperty]
    private string _evidenceExportStatusMessage = "";

    [ObservableProperty]
    private string _evidenceExportOutcome = "";

    [ObservableProperty]
    private int _evidenceExportFileCount;

    [ObservableProperty]
    private long _evidenceExportTotalBytes;

    [ObservableProperty]
    private string _evidenceExportBundleSha256 = "";

    [ObservableProperty]
    private System.DateTime? _evidenceExportLastRunAtUtc;

    public ObservableCollection<string> EvidenceExportFiles            { get; } = new();
    public ObservableCollection<string> EvidenceExportValidationIssues { get; } = new();
    public ObservableCollection<string> EvidenceExportRedactionFindings{ get; } = new();
    public ObservableCollection<string> EvidenceExportGeneratedArtifacts { get; } = new();

    private static readonly string[] EvidenceTypeChoices = new[]
    {
        "PERM_ADMIN_PILOT", "PERM_ADMIN_ROLLOUT",
        "READONLY_AUTH_PILOT", "DANGEROUS_AUTH_PILOT", "DB_AUTH_ROLLOUT",
        "TENANT_DB_MIGRATION", "AUDIT_REVIEW",
        "EMERGENCY_OFFLINE_APPROVAL", "POS_SMOKE",
        "PILOT_EVIDENCE", "MIGRATION_EVIDENCE", "CUTOVER_EVIDENCE",
        "ROLLBACK_EVIDENCE", "RETENTION_EVIDENCE", "AUDIT_EXPORT",
        "DIAGNOSTICS_EXPORT", "OTHER",
    };

    private static readonly string[] EnvironmentChoices = new[]
    {
        "LOCAL_DEV", "CI", "STAGING", "PRODUCTION",
    };

    public System.Collections.Generic.IReadOnlyList<string> EvidenceExportTypeChoices => EvidenceTypeChoices;
    public System.Collections.Generic.IReadOnlyList<string> EvidenceExportEnvironmentChoices => EnvironmentChoices;

    /// <summary>
    /// Recomputes the local-flag display + enables the export commands.
    /// Safe to call repeatedly; mirrors the Phase 10.19D /
    /// Phase 10.21G refresh pattern.
    /// </summary>
    public void RefreshEvidenceBundleExportFlag()
    {
        var on = _evidenceBundleExport.IsEnabled();
        EvidenceExportFlagEnabled = on;
        EvidenceExportFlagStatusText = on
            ? $"Enabled ({Services.EvidenceBundleExport.EvidenceBundleExportService.LocalFlagKey}=\"1\")."
            : $"Disabled ({Services.EvidenceBundleExport.EvidenceBundleExportService.LocalFlagKey} missing or \"0\").";
        SelectEvidenceBundleFolderCommand.NotifyCanExecuteChanged();
        ValidateEvidenceBundleFolderCommand.NotifyCanExecuteChanged();
        GenerateEvidenceBundleZipCommand.NotifyCanExecuteChanged();
        OpenEvidenceBundleOutputFolderCommand.NotifyCanExecuteChanged();
        ClearEvidenceBundleExportCommand.NotifyCanExecuteChanged();
    }

    private bool CanRunEvidenceExport() => EvidenceExportFlagEnabled;

    private bool CanRunEvidenceExportOnSelectedFolder() =>
        EvidenceExportFlagEnabled
        && !string.IsNullOrEmpty(_evidenceExportSourceFolderAbsolute);

    [RelayCommand(CanExecute = nameof(CanRunEvidenceExport))]
    private void SelectEvidenceBundleFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select evidence folder (local export only — no upload)",
        };
        var ok = dlg.ShowDialog();
        if (ok != true) return;
        var path = dlg.FolderName;
        if (string.IsNullOrWhiteSpace(path)) return;
        _evidenceExportSourceFolderAbsolute = path;
        EvidenceExportSourceFolderDisplay = TruncatePathForDisplay(path);
        if (string.IsNullOrEmpty(_evidenceExportOutputFolderAbsolute))
        {
            _evidenceExportOutputFolderAbsolute = path;
            EvidenceExportOutputFolderDisplay = TruncatePathForDisplay(path);
        }
        EvidenceExportStatusMessage = "Folder selected. Run Validate or Generate to continue.";
        EvidenceExportOutcome = "";
    }

    [RelayCommand(CanExecute = nameof(CanRunEvidenceExportOnSelectedFolder))]
    private void ValidateEvidenceBundleFolder()
    {
        var req = BuildExportRequest(generateZip: false);
        var result = _evidenceBundleExport.Run(req);
        ApplyExportResult(result);
    }

    [RelayCommand(CanExecute = nameof(CanRunEvidenceExportOnSelectedFolder))]
    private void GenerateEvidenceBundleZip()
    {
        var req = BuildExportRequest(generateZip: true);
        var result = _evidenceBundleExport.Run(req);
        ApplyExportResult(result);
    }

    [RelayCommand(CanExecute = nameof(CanRunEvidenceExport))]
    private void OpenEvidenceBundleOutputFolder()
    {
        if (string.IsNullOrEmpty(_evidenceExportOutputFolderAbsolute)) return;
        if (!System.IO.Directory.Exists(_evidenceExportOutputFolderAbsolute)) return;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = _evidenceExportOutputFolderAbsolute,
                UseShellExecute = true,
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch
        {
            // Silently no-op — we never want a failed shell-exec to abort
            // the UI. The folder remains visible in the path display.
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunEvidenceExport))]
    private void ClearEvidenceBundleExport()
    {
        _evidenceExportSourceFolderAbsolute = "";
        _evidenceExportOutputFolderAbsolute = "";
        EvidenceExportSourceFolderDisplay = "";
        EvidenceExportOutputFolderDisplay = "";
        EvidenceExportOutputZipDisplay = "";
        EvidenceExportManifestDisplay = "";
        EvidenceExportStatusMessage = "Cleared.";
        EvidenceExportOutcome = "";
        EvidenceExportFileCount = 0;
        EvidenceExportTotalBytes = 0;
        EvidenceExportBundleSha256 = "";
        EvidenceExportLastRunAtUtc = null;
        EvidenceExportFiles.Clear();
        EvidenceExportValidationIssues.Clear();
        EvidenceExportRedactionFindings.Clear();
        EvidenceExportGeneratedArtifacts.Clear();
    }

    private Services.EvidenceBundleExport.EvidenceBundleExportService.EvidenceBundleExportRequest BuildExportRequest(bool generateZip)
    {
        // Operator identity for createdBy: prefer the local cached username
        // (best-effort; may be empty pre-login). NEVER include the real
        // machine name or absolute path here.
        var createdBy = _global.Get("user_username");
        if (string.IsNullOrWhiteSpace(createdBy)) createdBy = "desktop-operator";

        return new Services.EvidenceBundleExport.EvidenceBundleExportService.EvidenceBundleExportRequest
        {
            SourceFolder    = _evidenceExportSourceFolderAbsolute,
            OutputFolder    = string.IsNullOrEmpty(_evidenceExportOutputFolderAbsolute)
                                ? _evidenceExportSourceFolderAbsolute
                                : _evidenceExportOutputFolderAbsolute,
            EvidenceType    = (EvidenceExportEvidenceType ?? "").Trim(),
            Environment     = (EvidenceExportEnvironment  ?? "").Trim(),
            Phase           = (EvidenceExportPhase        ?? "").Trim(),
            TenantId        = NullIfBlank(EvidenceExportTenantId),
            StoreId         = NullIfBlank(EvidenceExportStoreId),
            WaveNumber      = EvidenceExportWaveNumber,
            CreatedBy       = createdBy,
            GenerateZip     = generateZip,
            AllowOverwrite  = EvidenceExportAllowOverwrite,
        };
    }

    private void ApplyExportResult(EvidenceBundleExportResult result)
    {
        EvidenceExportLastRunAtUtc = System.DateTime.UtcNow;
        EvidenceExportOutcome      = result.Outcome;
        EvidenceExportStatusMessage = ScrubForDisplay(result.StatusMessage);
        EvidenceExportFileCount    = result.FileCount;
        EvidenceExportTotalBytes   = result.TotalBytes;
        EvidenceExportBundleSha256 = result.BundleSha256Hex ?? "";

        if (!string.IsNullOrEmpty(result.ZipRelativePath))
        {
            EvidenceExportOutputZipDisplay = TruncatePathForDisplay(
                System.IO.Path.Combine(_evidenceExportOutputFolderAbsolute, result.ZipRelativePath));
        }
        if (!string.IsNullOrEmpty(result.ManifestRelativePath))
        {
            EvidenceExportManifestDisplay = result.ManifestRelativePath!; // bare name only
        }

        EvidenceExportFiles.Clear();
        foreach (var f in result.Files)
        {
            EvidenceExportFiles.Add(
                $"{f.RelativePath}  ({FormatBytes(f.SizeBytes)}, sha256:{ShortenHash(f.Sha256Hex)})");
        }

        EvidenceExportValidationIssues.Clear();
        foreach (var i in result.ValidationIssues)
        {
            EvidenceExportValidationIssues.Add(
                $"{ScrubForDisplay(i.FilePath)} — {i.Code}: {ScrubForDisplay(i.SafeMessage)}");
        }

        EvidenceExportRedactionFindings.Clear();
        foreach (var f in result.RedactionFindings)
        {
            // Preview is always "[REDACTED]" — defence in depth, never
            // expose the raw matched value here.
            EvidenceExportRedactionFindings.Add(
                $"{ScrubForDisplay(f.FilePath)}:{f.LineNumber} — {f.Type} {f.Preview}");
        }

        EvidenceExportGeneratedArtifacts.Clear();
        foreach (var p in result.GeneratedArtifactRelativePaths)
        {
            EvidenceExportGeneratedArtifacts.Add(p);
        }
    }

    private static string ScrubForDisplay(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var (safe, _) = OperatorPermissionAdminRedaction.ScrubAndTruncate(value);
        return safe;
    }

    private static string TruncatePathForDisplay(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        const int max = 80;
        if (path.Length <= max) return path;
        return "…" + path[^(max - 1)..];
    }

    private static string ShortenHash(string sha)
        => string.IsNullOrEmpty(sha) || sha.Length <= 16 ? sha : sha[..8] + "…" + sha[^4..];

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KiB";
        return $"{bytes / 1024.0 / 1024.0:F2} MiB";
    }

    // ── Phase 10.22F — Operator Evidence Bundle Backend Upload + Finalize ───
    //
    // Calls the Phase 10.22C/D backend endpoints
    // (`/api/v1/operator/evidence/bundles[/files|/finalize|/{uuid}]`)
    // ONLY when the local default-OFF flag
    // `operator_evidence_bundle_upload_ui_enabled` is `"1"`.
    // The Phase 10.22E local export flag is independent — operators
    // may manually point at any folder containing a valid
    // `manifest.json` produced by Phase 10.22E.
    //
    // SECURITY: NEVER uploads any `.zip` file. The Phase 10.22E ZIP is a
    // local archive only. The desktop only uploads `manifest.json`
    // + each entry in `manifest.files[]`.

    [ObservableProperty]
    private bool _evidenceUploadFlagEnabled;

    [ObservableProperty]
    private string _evidenceUploadFlagStatusText =
        $"Disabled ({Services.EvidenceBundleUpload.EvidenceBundleUploadService.LocalFlagKey} missing or \"0\").";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ValidateEvidenceBundleUploadFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(UploadEvidenceBundleToBackendCommand))]
    private string _evidenceUploadSourceFolderDisplay = "";

    private string _evidenceUploadSourceFolderAbsolute = "";

    [ObservableProperty]
    private string _evidenceUploadManifestPathDisplay = "";

    [ObservableProperty]
    private string _evidenceUploadLocalZipPathDisplay = "";

    [ObservableProperty]
    private string _evidenceUploadFinalizeNotes = "";

    [ObservableProperty]
    private string _evidenceUploadStatusMessage = "";

    [ObservableProperty]
    private string _evidenceUploadOutcome = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshEvidenceBundleBackendStatusCommand))]
    private string _evidenceUploadBundleUuid = "";

    [ObservableProperty]
    private string _evidenceUploadBackendStatus = "";

    [ObservableProperty]
    private string _evidenceUploadBackendSha256 = "";

    [ObservableProperty]
    private int _evidenceUploadFilesUploaded;

    [ObservableProperty]
    private int _evidenceUploadTotalFiles;

    [ObservableProperty]
    private long _evidenceUploadBytesUploaded;

    [ObservableProperty]
    private long _evidenceUploadTotalBytes;

    [ObservableProperty]
    private string _evidenceUploadCurrentFile = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UploadEvidenceBundleToBackendCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshEvidenceBundleBackendStatusCommand))]
    [NotifyCanExecuteChangedFor(nameof(ValidateEvidenceBundleUploadFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectEvidenceBundleUploadFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearEvidenceBundleUploadCommand))]
    private bool _evidenceUploadIsUploading;

    [ObservableProperty]
    private string _evidenceUploadLastBackendErrorCode = "";

    [ObservableProperty]
    private string _evidenceUploadLastBackendErrorMessage = "";

    [ObservableProperty]
    private System.DateTime? _evidenceUploadLastRunAtUtc;

    public ObservableCollection<string> EvidenceUploadSteps    { get; } = new();
    public ObservableCollection<string> EvidenceUploadedFiles  { get; } = new();
    public ObservableCollection<string> EvidenceUploadWarnings { get; } = new();
    public ObservableCollection<string> EvidenceUploadErrors   { get; } = new();

    /// <summary>
    /// Recomputes the local-flag display + enables the upload commands.
    /// </summary>
    public void RefreshEvidenceBundleUploadFlag()
    {
        var on = _evidenceBundleUpload.IsEnabled();
        EvidenceUploadFlagEnabled = on;
        EvidenceUploadFlagStatusText = on
            ? $"Enabled ({Services.EvidenceBundleUpload.EvidenceBundleUploadService.LocalFlagKey}=\"1\")."
            : $"Disabled ({Services.EvidenceBundleUpload.EvidenceBundleUploadService.LocalFlagKey} missing or \"0\").";
        RefreshEvidenceBundleUploadFlagFromUiCommand.NotifyCanExecuteChanged();
        SelectEvidenceBundleUploadFolderCommand.NotifyCanExecuteChanged();
        ValidateEvidenceBundleUploadFolderCommand.NotifyCanExecuteChanged();
        UploadEvidenceBundleToBackendCommand.NotifyCanExecuteChanged();
        RefreshEvidenceBundleBackendStatusCommand.NotifyCanExecuteChanged();
        ClearEvidenceBundleUploadCommand.NotifyCanExecuteChanged();
    }

    private bool CanRunEvidenceUpload() =>
        EvidenceUploadFlagEnabled && !EvidenceUploadIsUploading;

    private bool CanRunEvidenceUploadOnSelectedFolder() =>
        EvidenceUploadFlagEnabled
        && !EvidenceUploadIsUploading
        && !string.IsNullOrEmpty(_evidenceUploadSourceFolderAbsolute);

    private bool CanRefreshEvidenceUploadBackendStatus() =>
        EvidenceUploadFlagEnabled
        && !EvidenceUploadIsUploading
        && !string.IsNullOrWhiteSpace(EvidenceUploadBundleUuid);

    [RelayCommand]
    private void RefreshEvidenceBundleUploadFlagFromUi() => RefreshEvidenceBundleUploadFlag();

    [RelayCommand(CanExecute = nameof(CanRunEvidenceUpload))]
    private void SelectEvidenceBundleUploadFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Phase 10.22E evidence folder (must contain manifest.json)",
        };
        var ok = dlg.ShowDialog();
        if (ok != true) return;
        var path = dlg.FolderName;
        if (string.IsNullOrWhiteSpace(path)) return;
        _evidenceUploadSourceFolderAbsolute = path;
        EvidenceUploadSourceFolderDisplay = TruncatePathForDisplay(path);

        // Compute manifest + ZIP display paths (display only — ZIP is
        // never uploaded).
        var manifestPath = System.IO.Path.Combine(path, "manifest.json");
        EvidenceUploadManifestPathDisplay = System.IO.File.Exists(manifestPath)
            ? "manifest.json"
            : "(manifest.json not found in this folder)";

        EvidenceUploadLocalZipPathDisplay = FindLocalZipForDisplay(path);
        EvidenceUploadStatusMessage = "Folder selected. Run Validate or Upload + Finalize.";
        EvidenceUploadOutcome = "";
    }

    [RelayCommand(CanExecute = nameof(CanRunEvidenceUploadOnSelectedFolder))]
    private void ValidateEvidenceBundleUploadFolder()
    {
        ResetEvidenceUploadLists();
        var manifestPath = System.IO.Path.Combine(_evidenceUploadSourceFolderAbsolute, "manifest.json");
        if (!System.IO.File.Exists(manifestPath))
        {
            EvidenceUploadOutcome = "LocalBlocked";
            EvidenceUploadStatusMessage =
                "manifest.json is missing from the selected folder. " +
                "Generate it via Phase 10.22E export first.";
            EvidenceUploadLastRunAtUtc = System.DateTime.UtcNow;
            return;
        }
        Core.DTOs.EvidenceBundleManifestDto? manifest = null;
        try
        {
            manifest = System.Text.Json.JsonSerializer.Deserialize<Core.DTOs.EvidenceBundleManifestDto>(
                System.IO.File.ReadAllBytes(manifestPath));
        }
        catch (System.Exception ex)
        {
            EvidenceUploadOutcome = "LocalBlocked";
            EvidenceUploadStatusMessage = ScrubForDisplay("Failed to parse manifest.json: " + ex.Message);
            EvidenceUploadLastRunAtUtc = System.DateTime.UtcNow;
            return;
        }
        var errors = new System.Collections.Generic.List<string>();
        var ok = manifest != null
              && Services.EvidenceBundleUpload.EvidenceBundleUploadService
                    .ValidateManifestLocally(manifest, _evidenceUploadSourceFolderAbsolute, errors);
        foreach (var e in errors)
            EvidenceUploadErrors.Add(ScrubForDisplay(e));

        EvidenceUploadOutcome = ok ? "LocalValidationOnly" : "LocalBlocked";
        EvidenceUploadStatusMessage = ok
            ? $"Local validation passed ({(manifest!.Files?.Count ?? 0) + 1} file(s) ready to upload)."
            : "Local validation failed — see errors list.";
        EvidenceUploadTotalFiles = (manifest?.Files?.Count ?? 0) + 1;
        EvidenceUploadLastRunAtUtc = System.DateTime.UtcNow;
    }

    [RelayCommand(CanExecute = nameof(CanRunEvidenceUploadOnSelectedFolder))]
    private async System.Threading.Tasks.Task UploadEvidenceBundleToBackend(
        System.Threading.CancellationToken ct)
    {
        ResetEvidenceUploadLists();
        EvidenceUploadIsUploading = true;
        try
        {
            var input = new Services.EvidenceBundleUpload.EvidenceBundleUploadService.EvidenceBundleUploadInput
            {
                SourceFolder  = _evidenceUploadSourceFolderAbsolute,
                FinalizeNotes = string.IsNullOrWhiteSpace(EvidenceUploadFinalizeNotes)
                                    ? null
                                    : EvidenceUploadFinalizeNotes,
            };
            var progress = new System.Progress<string>(msg =>
            {
                EvidenceUploadCurrentFile = ScrubForDisplay(msg);
            });
            Services.EvidenceBundleUpload.EvidenceBundleUploadResult result;
            try
            {
                result = await _evidenceBundleUpload.RunAsync(input, progress, ct);
            }
            catch (System.OperationCanceledException)
            {
                EvidenceUploadOutcome      = "BackendBlocked";
                EvidenceUploadStatusMessage = "Upload cancelled.";
                EvidenceUploadLastBackendErrorCode = "CANCELLED";
                EvidenceUploadLastRunAtUtc = System.DateTime.UtcNow;
                return;
            }
            ApplyUploadResult(result);
        }
        finally
        {
            EvidenceUploadIsUploading = false;
            EvidenceUploadCurrentFile = "";
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefreshEvidenceUploadBackendStatus))]
    private async System.Threading.Tasks.Task RefreshEvidenceBundleBackendStatus(
        System.Threading.CancellationToken ct)
    {
        var outcome = await _evidenceBundleUpload.RefreshBackendStatusAsync(
            EvidenceUploadBundleUuid, ct);
        if (outcome.Succeeded && outcome.Value is not null)
        {
            EvidenceUploadBackendStatus = outcome.Value.Status ?? "";
            EvidenceUploadBackendSha256 = outcome.Value.BundleSha256 ?? "";
            EvidenceUploadStatusMessage =
                $"Refreshed: status={outcome.Value.Status}, files={outcome.Value.FileCount}.";
            EvidenceUploadLastBackendErrorCode    = "";
            EvidenceUploadLastBackendErrorMessage = "";
        }
        else
        {
            EvidenceUploadLastBackendErrorCode    = outcome.ErrorCode ?? "REFRESH_FAILED";
            EvidenceUploadLastBackendErrorMessage = ScrubForDisplay(outcome.SafeMessage ?? "Refresh failed.");
            EvidenceUploadStatusMessage =
                $"Refresh failed: {EvidenceUploadLastBackendErrorCode}.";
        }
        EvidenceUploadLastRunAtUtc = System.DateTime.UtcNow;
    }

    [RelayCommand(CanExecute = nameof(CanRunEvidenceUpload))]
    private void ClearEvidenceBundleUpload()
    {
        _evidenceUploadSourceFolderAbsolute = "";
        EvidenceUploadSourceFolderDisplay = "";
        EvidenceUploadManifestPathDisplay = "";
        EvidenceUploadLocalZipPathDisplay = "";
        EvidenceUploadFinalizeNotes = "";
        EvidenceUploadStatusMessage = "Cleared.";
        EvidenceUploadOutcome = "";
        EvidenceUploadBundleUuid = "";
        EvidenceUploadBackendStatus = "";
        EvidenceUploadBackendSha256 = "";
        EvidenceUploadFilesUploaded = 0;
        EvidenceUploadTotalFiles = 0;
        EvidenceUploadBytesUploaded = 0;
        EvidenceUploadTotalBytes = 0;
        EvidenceUploadCurrentFile = "";
        EvidenceUploadLastBackendErrorCode = "";
        EvidenceUploadLastBackendErrorMessage = "";
        EvidenceUploadLastRunAtUtc = null;
        ResetEvidenceUploadLists();
    }

    private void ResetEvidenceUploadLists()
    {
        EvidenceUploadSteps.Clear();
        EvidenceUploadedFiles.Clear();
        EvidenceUploadWarnings.Clear();
        EvidenceUploadErrors.Clear();
    }

    private void ApplyUploadResult(Services.EvidenceBundleUpload.EvidenceBundleUploadResult result)
    {
        EvidenceUploadOutcome              = result.Outcome;
        EvidenceUploadStatusMessage        = ScrubForDisplay(result.StatusMessage);
        EvidenceUploadBundleUuid           = result.BundleUuid ?? "";
        EvidenceUploadBackendStatus        = result.BackendBundleStatus ?? "";
        EvidenceUploadBackendSha256        = result.BackendBundleSha256 ?? "";
        EvidenceUploadFilesUploaded        = result.FilesUploaded;
        EvidenceUploadTotalFiles           = result.TotalFiles;
        EvidenceUploadBytesUploaded        = result.BytesUploaded;
        EvidenceUploadTotalBytes           = result.TotalBytes;
        EvidenceUploadCurrentFile          = ScrubForDisplay(result.CurrentFile ?? "");
        EvidenceUploadLastBackendErrorCode = result.LastBackendErrorCode ?? "";
        EvidenceUploadLastBackendErrorMessage = ScrubForDisplay(result.LastBackendErrorMessage ?? "");
        EvidenceUploadLastRunAtUtc         = System.DateTime.UtcNow;

        foreach (var s in result.UploadSteps)    EvidenceUploadSteps.Add(ScrubForDisplay(s));
        foreach (var f in result.UploadedFiles)  EvidenceUploadedFiles.Add(ScrubForDisplay(f));
        foreach (var w in result.Warnings)       EvidenceUploadWarnings.Add(ScrubForDisplay(w));
        foreach (var e in result.Errors)         EvidenceUploadErrors.Add(ScrubForDisplay(e));
    }

    private static string FindLocalZipForDisplay(string folder)
    {
        try
        {
            // Display only — the path is shown next to a banner saying
            // "Local archive only — not uploaded". The pipeline never
            // touches this file. Pick the most-recent .zip in the folder
            // root (if any) just so the operator can verify the local
            // export they're about to upload from.
            var zips = System.IO.Directory.GetFiles(folder, "*.zip", System.IO.SearchOption.TopDirectoryOnly);
            if (zips.Length == 0) return "(no local .zip in this folder — that's OK; ZIP is not uploaded)";
            System.Array.Sort(zips, (a, b) =>
                System.IO.File.GetLastWriteTimeUtc(b).CompareTo(System.IO.File.GetLastWriteTimeUtc(a)));
            return TruncatePathForDisplay(zips[0]);
        }
        catch
        {
            return "";
        }
    }

    // ── Phase 10.22G — Operator Evidence Bundle Reviewer + Download ─────────
    //
    // Calls the Phase 10.22G backend endpoints
    //   POST /api/v1/operator/evidence/bundles/{uuid}/review
    //   GET  /api/v1/operator/evidence/bundles/{uuid}/download
    // plus the read-only list/get-metadata endpoints (already shipped in
    // Phase 10.22C/D), gated by the local default-OFF flag
    // `operator_evidence_bundle_review_ui_enabled`.
    //
    // Reviewer card carries NO upload / finalize / delete / retention /
    // dangerous-execute / confirmation-phrase / raw SQL control.

    [ObservableProperty]
    private bool _evidenceReviewFlagEnabled;

    [ObservableProperty]
    private string _evidenceReviewFlagStatusText =
        $"Disabled ({Services.EvidenceBundleReview.EvidenceBundleReviewService.LocalFlagKey} missing or \"0\").";

    // ── List filters ────────────────────────────────────────────────────────

    [ObservableProperty] private string _evidenceReviewFilterEvidenceType = "";
    [ObservableProperty] private string _evidenceReviewFilterPhase        = "";
    [ObservableProperty] private string _evidenceReviewFilterTenantId     = "";
    [ObservableProperty] private string _evidenceReviewFilterStatus       = "FINALIZED";
    [ObservableProperty] private int    _evidenceReviewFilterPage;
    [ObservableProperty] private int    _evidenceReviewFilterSize = 20;

    public System.Collections.Generic.IReadOnlyList<string> EvidenceReviewStatusChoices =>
        new[] { "", "FINALIZED", "REVIEWED", "REJECTED", "NEEDS_CHANGES", "ARCHIVED", "QUARANTINED" };

    public ObservableCollection<EvidenceReviewBundleRow> EvidenceReviewBundles { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadEvidenceReviewSelectedBundleCommand))]
    [NotifyCanExecuteChangedFor(nameof(SubmitEvidenceReviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadEvidenceReviewBundleCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshEvidenceReviewSelectedBundleCommand))]
    private string _evidenceReviewSelectedBundleUuid = "";

    // ── Selected bundle metadata ────────────────────────────────────────────

    [ObservableProperty] private string _evidenceReviewSelectedEvidenceType = "";
    [ObservableProperty] private string _evidenceReviewSelectedPhase        = "";
    [ObservableProperty] private string _evidenceReviewSelectedEnvironment  = "";
    [ObservableProperty] private string _evidenceReviewSelectedTenantId     = "";
    [ObservableProperty] private string _evidenceReviewSelectedStoreId      = "";
    [ObservableProperty] private string _evidenceReviewSelectedStatus       = "";
    [ObservableProperty] private int    _evidenceReviewSelectedFileCount;
    [ObservableProperty] private long   _evidenceReviewSelectedTotalBytes;
    [ObservableProperty] private string _evidenceReviewSelectedBundleSha256 = "";
    [ObservableProperty] private string _evidenceReviewSelectedCreatedBy    = "";
    [ObservableProperty] private string _evidenceReviewSelectedFinalizedAt  = "";
    [ObservableProperty] private string _evidenceReviewSelectedReviewedAt   = "";
    public ObservableCollection<string> EvidenceReviewSelectedFiles { get; } = new();

    // ── Review decision ─────────────────────────────────────────────────────

    [ObservableProperty] private string _evidenceReviewDecision = "APPROVED";
    [ObservableProperty] private string _evidenceReviewNotes    = "";

    public System.Collections.Generic.IReadOnlyList<string> EvidenceReviewDecisionChoices =>
        new[] { "APPROVED", "REJECTED", "NEEDS_CHANGES" };

    // ── Download ────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadEvidenceReviewBundleCommand))]
    private string _evidenceReviewDownloadFolderDisplay = "";

    private string _evidenceReviewDownloadFolderAbsolute = "";

    [ObservableProperty] private bool   _evidenceReviewAllowOverwrite;
    [ObservableProperty] private string _evidenceReviewDownloadedFilename = "";
    [ObservableProperty] private long   _evidenceReviewDownloadedByteSize;
    [ObservableProperty] private string _evidenceReviewDownloadedSha256   = "";
    [ObservableProperty] private string _evidenceReviewDownloadedPathDisplay = "";

    // ── Status ──────────────────────────────────────────────────────────────

    [ObservableProperty] private string _evidenceReviewStatusMessage     = "";
    [ObservableProperty] private string _evidenceReviewOutcome           = "";
    [ObservableProperty] private string _evidenceReviewLastErrorCode     = "";
    [ObservableProperty] private string _evidenceReviewLastErrorMessage  = "";
    [ObservableProperty] private System.DateTime? _evidenceReviewLastRunAtUtc;

    public ObservableCollection<string> EvidenceReviewWarnings { get; } = new();
    public ObservableCollection<string> EvidenceReviewErrors   { get; } = new();

    public sealed record EvidenceReviewBundleRow(
        string Uuid,
        string EvidenceType,
        string Phase,
        string Status,
        string TenantId,
        int    FileCount,
        long   TotalBytes,
        string CreatedAt);

    public void RefreshEvidenceBundleReviewFlag()
    {
        var on = _evidenceBundleReview.IsEnabled();
        EvidenceReviewFlagEnabled = on;
        EvidenceReviewFlagStatusText = on
            ? $"Enabled ({Services.EvidenceBundleReview.EvidenceBundleReviewService.LocalFlagKey}=\"1\")."
            : $"Disabled ({Services.EvidenceBundleReview.EvidenceBundleReviewService.LocalFlagKey} missing or \"0\").";
        RefreshEvidenceBundleReviewFlagFromUiCommand.NotifyCanExecuteChanged();
        ListEvidenceReviewBundlesCommand.NotifyCanExecuteChanged();
        LoadEvidenceReviewSelectedBundleCommand.NotifyCanExecuteChanged();
        SubmitEvidenceReviewCommand.NotifyCanExecuteChanged();
        SelectEvidenceReviewDownloadFolderCommand.NotifyCanExecuteChanged();
        DownloadEvidenceReviewBundleCommand.NotifyCanExecuteChanged();
        RefreshEvidenceReviewSelectedBundleCommand.NotifyCanExecuteChanged();
        ClearEvidenceReviewCommand.NotifyCanExecuteChanged();
    }

    private bool CanRunEvidenceReview() => EvidenceReviewFlagEnabled;

    private bool CanRunEvidenceReviewWithSelected() =>
        EvidenceReviewFlagEnabled && !string.IsNullOrWhiteSpace(EvidenceReviewSelectedBundleUuid);

    private bool CanDownloadEvidenceReview() =>
        EvidenceReviewFlagEnabled
        && !string.IsNullOrWhiteSpace(EvidenceReviewSelectedBundleUuid)
        && !string.IsNullOrWhiteSpace(_evidenceReviewDownloadFolderAbsolute);

    [RelayCommand]
    private void RefreshEvidenceBundleReviewFlagFromUi() => RefreshEvidenceBundleReviewFlag();

    [RelayCommand(CanExecute = nameof(CanRunEvidenceReview))]
    private async System.Threading.Tasks.Task ListEvidenceReviewBundles(System.Threading.CancellationToken ct)
    {
        ResetEvidenceReviewLists();
        var outcome = await _evidenceBundleReview.ListAsync(
            NullIfBlank(EvidenceReviewFilterEvidenceType),
            NullIfBlank(EvidenceReviewFilterPhase),
            NullIfBlank(EvidenceReviewFilterTenantId),
            NullIfBlank(EvidenceReviewFilterStatus),
            EvidenceReviewFilterPage,
            EvidenceReviewFilterSize,
            ct);
        EvidenceReviewLastRunAtUtc = System.DateTime.UtcNow;
        if (!outcome.Succeeded || outcome.Value is null)
        {
            ApplyReviewError(outcome.ErrorCode, outcome.SafeMessage, "list");
            return;
        }
        EvidenceReviewBundles.Clear();
        foreach (var item in outcome.Value.Items ?? new System.Collections.Generic.List<EvidenceBundlePageItemDto>())
        {
            EvidenceReviewBundles.Add(new EvidenceReviewBundleRow(
                Uuid:         item.Uuid ?? "",
                EvidenceType: item.EvidenceType ?? "",
                Phase:        item.Phase ?? "",
                Status:       item.Status ?? "",
                TenantId:     item.TenantId ?? "",
                FileCount:    item.FileCount,
                TotalBytes:   item.TotalBytes,
                CreatedAt:    item.CreatedAt ?? ""));
        }
        EvidenceReviewOutcome       = "Listed";
        EvidenceReviewStatusMessage =
            ScrubForDisplay($"Listed {outcome.Value.TotalElements} bundle(s) (page {outcome.Value.Page}, size {outcome.Value.Size}).");
        EvidenceReviewLastErrorCode    = "";
        EvidenceReviewLastErrorMessage = "";
    }

    [RelayCommand(CanExecute = nameof(CanRunEvidenceReviewWithSelected))]
    private async System.Threading.Tasks.Task LoadEvidenceReviewSelectedBundle(System.Threading.CancellationToken ct)
    {
        ResetEvidenceReviewLists();
        var outcome = await _evidenceBundleReview.GetAsync(EvidenceReviewSelectedBundleUuid, ct);
        EvidenceReviewLastRunAtUtc = System.DateTime.UtcNow;
        if (!outcome.Succeeded || outcome.Value is null)
        {
            ApplyReviewError(outcome.ErrorCode, outcome.SafeMessage, "get");
            return;
        }
        ApplySelectedBundle(outcome.Value);
        EvidenceReviewOutcome       = "Loaded";
        EvidenceReviewStatusMessage = ScrubForDisplay(
            $"Loaded bundle {EvidenceReviewSelectedBundleUuid} (status={EvidenceReviewSelectedStatus}).");
        EvidenceReviewLastErrorCode    = "";
        EvidenceReviewLastErrorMessage = "";
    }

    [RelayCommand(CanExecute = nameof(CanRunEvidenceReviewWithSelected))]
    private async System.Threading.Tasks.Task RefreshEvidenceReviewSelectedBundle(System.Threading.CancellationToken ct)
    {
        await LoadEvidenceReviewSelectedBundle(ct);
    }

    [RelayCommand(CanExecute = nameof(CanRunEvidenceReviewWithSelected))]
    private async System.Threading.Tasks.Task SubmitEvidenceReview(System.Threading.CancellationToken ct)
    {
        ResetEvidenceReviewLists();
        var outcome = await _evidenceBundleReview.ReviewAsync(
            EvidenceReviewSelectedBundleUuid,
            EvidenceReviewDecision,
            EvidenceReviewNotes,
            ct);
        EvidenceReviewLastRunAtUtc = System.DateTime.UtcNow;
        if (!outcome.Succeeded || outcome.Value is null)
        {
            ApplyReviewError(outcome.ErrorCode, outcome.SafeMessage, "review");
            return;
        }
        ApplySelectedBundle(outcome.Value);
        EvidenceReviewOutcome       = "Reviewed";
        EvidenceReviewStatusMessage = ScrubForDisplay(
            $"Review recorded: status={EvidenceReviewSelectedStatus}.");
        EvidenceReviewLastErrorCode    = "";
        EvidenceReviewLastErrorMessage = "";
    }

    [RelayCommand(CanExecute = nameof(CanRunEvidenceReview))]
    private void SelectEvidenceReviewDownloadFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select local destination folder for evidence bundle download",
        };
        var ok = dlg.ShowDialog();
        if (ok != true) return;
        var path = dlg.FolderName;
        if (string.IsNullOrWhiteSpace(path)) return;
        _evidenceReviewDownloadFolderAbsolute = path;
        EvidenceReviewDownloadFolderDisplay = TruncatePathForDisplay(path);
        DownloadEvidenceReviewBundleCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanDownloadEvidenceReview))]
    private async System.Threading.Tasks.Task DownloadEvidenceReviewBundle(System.Threading.CancellationToken ct)
    {
        ResetEvidenceReviewLists();
        var outcome = await _evidenceBundleReview.DownloadAsync(
            EvidenceReviewSelectedBundleUuid,
            _evidenceReviewDownloadFolderAbsolute,
            EvidenceReviewAllowOverwrite,
            ct);
        EvidenceReviewLastRunAtUtc = System.DateTime.UtcNow;
        if (!outcome.Succeeded || outcome.Value is null)
        {
            ApplyReviewError(outcome.ErrorCode, outcome.SafeMessage, "download");
            return;
        }
        EvidenceReviewDownloadedFilename    = outcome.Value.DestinationFilename;
        EvidenceReviewDownloadedByteSize    = outcome.Value.ByteSize;
        EvidenceReviewDownloadedSha256      = outcome.Value.Sha256Hex;
        EvidenceReviewDownloadedPathDisplay = TruncatePathForDisplay(outcome.Value.DestinationPath);
        EvidenceReviewOutcome               = "Downloaded";
        EvidenceReviewStatusMessage         = ScrubForDisplay(
            $"Downloaded {outcome.Value.DestinationFilename} ({outcome.Value.ByteSize} B).");
        EvidenceReviewLastErrorCode    = "";
        EvidenceReviewLastErrorMessage = "";
    }

    [RelayCommand(CanExecute = nameof(CanRunEvidenceReview))]
    private void ClearEvidenceReview()
    {
        EvidenceReviewBundles.Clear();
        EvidenceReviewSelectedBundleUuid = "";
        EvidenceReviewSelectedEvidenceType = "";
        EvidenceReviewSelectedPhase = "";
        EvidenceReviewSelectedEnvironment = "";
        EvidenceReviewSelectedTenantId = "";
        EvidenceReviewSelectedStoreId = "";
        EvidenceReviewSelectedStatus = "";
        EvidenceReviewSelectedFileCount = 0;
        EvidenceReviewSelectedTotalBytes = 0;
        EvidenceReviewSelectedBundleSha256 = "";
        EvidenceReviewSelectedCreatedBy = "";
        EvidenceReviewSelectedFinalizedAt = "";
        EvidenceReviewSelectedReviewedAt = "";
        EvidenceReviewSelectedFiles.Clear();
        EvidenceReviewDecision = "APPROVED";
        EvidenceReviewNotes = "";
        _evidenceReviewDownloadFolderAbsolute = "";
        EvidenceReviewDownloadFolderDisplay = "";
        EvidenceReviewAllowOverwrite = false;
        EvidenceReviewDownloadedFilename = "";
        EvidenceReviewDownloadedByteSize = 0;
        EvidenceReviewDownloadedSha256 = "";
        EvidenceReviewDownloadedPathDisplay = "";
        EvidenceReviewStatusMessage = "Cleared.";
        EvidenceReviewOutcome = "";
        EvidenceReviewLastErrorCode = "";
        EvidenceReviewLastErrorMessage = "";
        EvidenceReviewLastRunAtUtc = null;
        ResetEvidenceReviewLists();
    }

    private void ApplySelectedBundle(EvidenceBundleResponseDto dto)
    {
        EvidenceReviewSelectedEvidenceType = dto.EvidenceType ?? "";
        EvidenceReviewSelectedPhase        = dto.Phase ?? "";
        EvidenceReviewSelectedEnvironment  = dto.Environment ?? "";
        EvidenceReviewSelectedTenantId     = dto.TenantId ?? "";
        EvidenceReviewSelectedStoreId      = dto.StoreId ?? "";
        EvidenceReviewSelectedStatus       = dto.Status ?? "";
        EvidenceReviewSelectedFileCount    = dto.FileCount;
        EvidenceReviewSelectedTotalBytes   = dto.TotalBytes;
        EvidenceReviewSelectedBundleSha256 = dto.BundleSha256 ?? "";
        EvidenceReviewSelectedCreatedBy    = dto.CreatedByUsername ?? (dto.CreatedBy?.ToString() ?? "");
        EvidenceReviewSelectedFinalizedAt  = dto.FinalizedAt ?? "";
        EvidenceReviewSelectedFiles.Clear();
        if (dto.Files != null)
        {
            foreach (var f in dto.Files)
            {
                EvidenceReviewSelectedFiles.Add(
                    $"{f.RelativePath ?? ""} ({f.FileSizeBytes} B, sha256:{ShortenHash(f.Sha256Hex ?? "")})");
            }
        }
    }

    private void ApplyReviewError(string? code, string? message, string operation)
    {
        EvidenceReviewOutcome          = "Failed";
        EvidenceReviewLastErrorCode    = code ?? "UNKNOWN";
        EvidenceReviewLastErrorMessage = ScrubForDisplay(message ?? "");
        EvidenceReviewStatusMessage    = ScrubForDisplay(
            $"{operation} failed: {EvidenceReviewLastErrorCode}.");
        EvidenceReviewErrors.Add(ScrubForDisplay($"[{EvidenceReviewLastErrorCode}] {message ?? ""}"));
    }

    private void ResetEvidenceReviewLists()
    {
        EvidenceReviewWarnings.Clear();
        EvidenceReviewErrors.Clear();
    }

    // ── Phase 10.22H — Operator Evidence Bundle Retention + Legal Hold ──────
    //
    // Calls the Phase 10.22H backend endpoints
    //   POST /api/v1/operator/evidence/bundles/{uuid}/retention
    //   POST /api/v1/operator/evidence/bundles/{uuid}/legal-hold
    //   POST /api/v1/operator/evidence/bundles/{uuid}/archive
    //   POST /api/v1/operator/evidence/bundles/{uuid}/expire
    //   GET  /api/v1/operator/evidence/bundles/retention-candidates
    // plus a read-only GetAsync passthrough.
    //
    // Gated by the local default-OFF flag
    // `operator_evidence_bundle_retention_ui_enabled`. The retention card
    // carries NO upload / finalize / delete / hard-delete / dangerous-
    // execute / confirmation-phrase / raw-SQL / storage-path control.

    [ObservableProperty]
    private bool _evidenceRetentionFlagEnabled;

    [ObservableProperty]
    private string _evidenceRetentionFlagStatusText =
        $"Disabled ({Services.EvidenceBundleRetention.EvidenceBundleRetentionService.LocalFlagKey} missing or \"0\").";

    // ── Candidate list filters ──────────────────────────────────────────────

    [ObservableProperty] private string _evidenceRetentionFilterEvidenceType = "";
    [ObservableProperty] private string _evidenceRetentionFilterStatus       = "";
    [ObservableProperty] private string _evidenceRetentionFilterTenantId     = "";
    [ObservableProperty] private string _evidenceRetentionFilterBeforeUtc    = "";
    [ObservableProperty] private int    _evidenceRetentionFilterPage;
    [ObservableProperty] private int    _evidenceRetentionFilterSize = 20;

    public System.Collections.Generic.IReadOnlyList<string> EvidenceRetentionStatusChoices =>
        new[] { "", "FINALIZED", "REVIEWED", "REJECTED", "NEEDS_CHANGES", "ARCHIVED" };

    public ObservableCollection<EvidenceRetentionCandidateRow> EvidenceRetentionCandidates { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadEvidenceRetentionSelectedBundleCommand))]
    [NotifyCanExecuteChangedFor(nameof(SubmitEvidenceRetentionUpdateCommand))]
    [NotifyCanExecuteChangedFor(nameof(SubmitEvidenceRetentionLegalHoldCommand))]
    [NotifyCanExecuteChangedFor(nameof(SubmitEvidenceRetentionArchiveCommand))]
    [NotifyCanExecuteChangedFor(nameof(SubmitEvidenceRetentionExpireCommand))]
    private string _evidenceRetentionSelectedBundleUuid = "";

    // ── Selected bundle snapshot ───────────────────────────────────────────

    [ObservableProperty] private string _evidenceRetentionSelectedEvidenceType   = "";
    [ObservableProperty] private string _evidenceRetentionSelectedPhase          = "";
    [ObservableProperty] private string _evidenceRetentionSelectedStatus         = "";
    [ObservableProperty] private string _evidenceRetentionSelectedTenantId       = "";
    [ObservableProperty] private string _evidenceRetentionSelectedRetentionClass = "";
    [ObservableProperty] private string _evidenceRetentionSelectedRetentionUntil = "";
    [ObservableProperty] private bool   _evidenceRetentionSelectedLegalHold;
    [ObservableProperty] private string _evidenceRetentionSelectedReviewedBy     = "";
    [ObservableProperty] private string _evidenceRetentionSelectedReviewedAt     = "";

    // ── Retention update form ──────────────────────────────────────────────

    [ObservableProperty] private string _evidenceRetentionNewRetentionUntilUtc = "";
    [ObservableProperty] private string _evidenceRetentionUpdateReason         = "";
    [ObservableProperty] private string _evidenceRetentionUpdateTicketId       = "";

    // ── Legal hold toggle form ─────────────────────────────────────────────

    [ObservableProperty] private bool   _evidenceRetentionLegalHoldNewValue;
    [ObservableProperty] private string _evidenceRetentionLegalHoldReason   = "";
    [ObservableProperty] private string _evidenceRetentionLegalHoldTicketId = "";

    // ── Archive / expire forms ─────────────────────────────────────────────

    [ObservableProperty] private string _evidenceRetentionArchiveReason   = "";
    [ObservableProperty] private string _evidenceRetentionArchiveTicketId = "";
    [ObservableProperty] private string _evidenceRetentionExpireReason    = "";
    [ObservableProperty] private string _evidenceRetentionExpireTicketId  = "";

    // ── Status ─────────────────────────────────────────────────────────────

    [ObservableProperty] private string _evidenceRetentionOutcome           = "";
    [ObservableProperty] private string _evidenceRetentionStatusMessage     = "";
    [ObservableProperty] private string _evidenceRetentionLastErrorCode     = "";
    [ObservableProperty] private string _evidenceRetentionLastErrorMessage  = "";
    [ObservableProperty] private System.DateTime? _evidenceRetentionLastRunAtUtc;

    public ObservableCollection<string> EvidenceRetentionWarnings { get; } = new();
    public ObservableCollection<string> EvidenceRetentionErrors   { get; } = new();

    public sealed record EvidenceRetentionCandidateRow(
        string Uuid,
        string EvidenceType,
        string Status,
        string TenantId,
        string RetentionUntil,
        bool   LegalHold,
        long   TotalBytes);

    public void RefreshEvidenceBundleRetentionFlag()
    {
        var on = _evidenceBundleRetention.IsEnabled();
        EvidenceRetentionFlagEnabled = on;
        EvidenceRetentionFlagStatusText = on
            ? $"Enabled ({Services.EvidenceBundleRetention.EvidenceBundleRetentionService.LocalFlagKey}=\"1\")."
            : $"Disabled ({Services.EvidenceBundleRetention.EvidenceBundleRetentionService.LocalFlagKey} missing or \"0\").";
        RefreshEvidenceBundleRetentionFlagFromUiCommand.NotifyCanExecuteChanged();
        ListEvidenceRetentionCandidatesCommand.NotifyCanExecuteChanged();
        LoadEvidenceRetentionSelectedBundleCommand.NotifyCanExecuteChanged();
        SubmitEvidenceRetentionUpdateCommand.NotifyCanExecuteChanged();
        SubmitEvidenceRetentionLegalHoldCommand.NotifyCanExecuteChanged();
        SubmitEvidenceRetentionArchiveCommand.NotifyCanExecuteChanged();
        SubmitEvidenceRetentionExpireCommand.NotifyCanExecuteChanged();
        ClearEvidenceRetentionCommand.NotifyCanExecuteChanged();
    }

    private bool CanRunEvidenceRetention() => EvidenceRetentionFlagEnabled;

    private bool CanRunEvidenceRetentionWithSelected() =>
        EvidenceRetentionFlagEnabled
        && !string.IsNullOrWhiteSpace(EvidenceRetentionSelectedBundleUuid);

    [RelayCommand]
    private void RefreshEvidenceBundleRetentionFlagFromUi() => RefreshEvidenceBundleRetentionFlag();

    [RelayCommand(CanExecute = nameof(CanRunEvidenceRetention))]
    private async System.Threading.Tasks.Task ListEvidenceRetentionCandidates(
        System.Threading.CancellationToken ct)
    {
        ResetEvidenceRetentionLists();
        var outcome = await _evidenceBundleRetention.ListCandidatesAsync(
            NullIfBlank(EvidenceRetentionFilterEvidenceType),
            NullIfBlank(EvidenceRetentionFilterStatus),
            NullIfBlank(EvidenceRetentionFilterTenantId),
            NullIfBlank(EvidenceRetentionFilterBeforeUtc),
            EvidenceRetentionFilterPage,
            EvidenceRetentionFilterSize,
            ct);
        EvidenceRetentionLastRunAtUtc = System.DateTime.UtcNow;
        if (!outcome.Succeeded || outcome.Value is null)
        {
            ApplyRetentionError(outcome.ErrorCode, outcome.SafeMessage, "list-candidates");
            return;
        }
        EvidenceRetentionCandidates.Clear();
        foreach (var item in outcome.Value.Items ?? new System.Collections.Generic.List<EvidenceBundlePageItemDto>())
        {
            EvidenceRetentionCandidates.Add(new EvidenceRetentionCandidateRow(
                Uuid:           item.Uuid ?? "",
                EvidenceType:   item.EvidenceType ?? "",
                Status:         item.Status ?? "",
                TenantId:       item.TenantId ?? "",
                RetentionUntil: item.RetentionUntil ?? "",
                LegalHold:      item.LegalHold,
                TotalBytes:     item.TotalBytes));
        }
        EvidenceRetentionOutcome       = "Listed";
        EvidenceRetentionStatusMessage = ScrubForDisplay(
            $"Retention candidates: {outcome.Value.TotalElements} bundle(s).");
        EvidenceRetentionLastErrorCode    = "";
        EvidenceRetentionLastErrorMessage = "";
    }

    [RelayCommand(CanExecute = nameof(CanRunEvidenceRetentionWithSelected))]
    private async System.Threading.Tasks.Task LoadEvidenceRetentionSelectedBundle(
        System.Threading.CancellationToken ct)
    {
        ResetEvidenceRetentionLists();
        var outcome = await _evidenceBundleRetention.GetAsync(
            EvidenceRetentionSelectedBundleUuid, ct);
        EvidenceRetentionLastRunAtUtc = System.DateTime.UtcNow;
        if (!outcome.Succeeded || outcome.Value is null)
        {
            ApplyRetentionError(outcome.ErrorCode, outcome.SafeMessage, "get");
            return;
        }
        ApplySelectedRetentionBundle(outcome.Value);
        EvidenceRetentionOutcome       = "Loaded";
        EvidenceRetentionStatusMessage = ScrubForDisplay(
            $"Loaded bundle {EvidenceRetentionSelectedBundleUuid} (status={EvidenceRetentionSelectedStatus}, legalHold={EvidenceRetentionSelectedLegalHold}).");
        EvidenceRetentionLastErrorCode    = "";
        EvidenceRetentionLastErrorMessage = "";
    }

    [RelayCommand(CanExecute = nameof(CanRunEvidenceRetentionWithSelected))]
    private async System.Threading.Tasks.Task SubmitEvidenceRetentionUpdate(
        System.Threading.CancellationToken ct)
    {
        ResetEvidenceRetentionLists();
        var outcome = await _evidenceBundleRetention.UpdateRetentionAsync(
            EvidenceRetentionSelectedBundleUuid,
            EvidenceRetentionNewRetentionUntilUtc,
            EvidenceRetentionUpdateReason,
            EvidenceRetentionUpdateTicketId,
            ct);
        EvidenceRetentionLastRunAtUtc = System.DateTime.UtcNow;
        if (!outcome.Succeeded || outcome.Value is null)
        {
            ApplyRetentionError(outcome.ErrorCode, outcome.SafeMessage, "update-retention");
            return;
        }
        ApplySelectedRetentionBundle(outcome.Value);
        EvidenceRetentionOutcome       = "RetentionUpdated";
        EvidenceRetentionStatusMessage = ScrubForDisplay(
            $"Retention updated: until={EvidenceRetentionSelectedRetentionUntil}.");
        EvidenceRetentionLastErrorCode    = "";
        EvidenceRetentionLastErrorMessage = "";
    }

    [RelayCommand(CanExecute = nameof(CanRunEvidenceRetentionWithSelected))]
    private async System.Threading.Tasks.Task SubmitEvidenceRetentionLegalHold(
        System.Threading.CancellationToken ct)
    {
        ResetEvidenceRetentionLists();
        var outcome = await _evidenceBundleRetention.UpdateLegalHoldAsync(
            EvidenceRetentionSelectedBundleUuid,
            EvidenceRetentionLegalHoldNewValue,
            EvidenceRetentionLegalHoldReason,
            EvidenceRetentionLegalHoldTicketId,
            ct);
        EvidenceRetentionLastRunAtUtc = System.DateTime.UtcNow;
        if (!outcome.Succeeded || outcome.Value is null)
        {
            ApplyRetentionError(outcome.ErrorCode, outcome.SafeMessage, "legal-hold");
            return;
        }
        ApplySelectedRetentionBundle(outcome.Value);
        EvidenceRetentionOutcome       = EvidenceRetentionSelectedLegalHold ? "LegalHoldOn" : "LegalHoldOff";
        EvidenceRetentionStatusMessage = ScrubForDisplay(
            $"Legal hold = {EvidenceRetentionSelectedLegalHold}.");
        EvidenceRetentionLastErrorCode    = "";
        EvidenceRetentionLastErrorMessage = "";
    }

    [RelayCommand(CanExecute = nameof(CanRunEvidenceRetentionWithSelected))]
    private async System.Threading.Tasks.Task SubmitEvidenceRetentionArchive(
        System.Threading.CancellationToken ct)
    {
        ResetEvidenceRetentionLists();
        var outcome = await _evidenceBundleRetention.ArchiveAsync(
            EvidenceRetentionSelectedBundleUuid,
            EvidenceRetentionArchiveReason,
            EvidenceRetentionArchiveTicketId,
            ct);
        EvidenceRetentionLastRunAtUtc = System.DateTime.UtcNow;
        if (!outcome.Succeeded || outcome.Value is null)
        {
            ApplyRetentionError(outcome.ErrorCode, outcome.SafeMessage, "archive");
            return;
        }
        ApplySelectedRetentionBundle(outcome.Value);
        EvidenceRetentionOutcome       = "Archived";
        EvidenceRetentionStatusMessage = ScrubForDisplay(
            $"Bundle archived. Status = {EvidenceRetentionSelectedStatus}.");
        EvidenceRetentionLastErrorCode    = "";
        EvidenceRetentionLastErrorMessage = "";
    }

    [RelayCommand(CanExecute = nameof(CanRunEvidenceRetentionWithSelected))]
    private async System.Threading.Tasks.Task SubmitEvidenceRetentionExpire(
        System.Threading.CancellationToken ct)
    {
        ResetEvidenceRetentionLists();
        var outcome = await _evidenceBundleRetention.ExpireAsync(
            EvidenceRetentionSelectedBundleUuid,
            EvidenceRetentionExpireReason,
            EvidenceRetentionExpireTicketId,
            ct);
        EvidenceRetentionLastRunAtUtc = System.DateTime.UtcNow;
        if (!outcome.Succeeded || outcome.Value is null)
        {
            ApplyRetentionError(outcome.ErrorCode, outcome.SafeMessage, "expire");
            return;
        }
        ApplySelectedRetentionBundle(outcome.Value);
        EvidenceRetentionOutcome       = "Expired";
        EvidenceRetentionStatusMessage = ScrubForDisplay(
            $"Bundle expired. Status = {EvidenceRetentionSelectedStatus}.");
        EvidenceRetentionLastErrorCode    = "";
        EvidenceRetentionLastErrorMessage = "";
    }

    [RelayCommand(CanExecute = nameof(CanRunEvidenceRetention))]
    private void ClearEvidenceRetention()
    {
        EvidenceRetentionCandidates.Clear();
        EvidenceRetentionSelectedBundleUuid = "";
        EvidenceRetentionSelectedEvidenceType = "";
        EvidenceRetentionSelectedPhase = "";
        EvidenceRetentionSelectedStatus = "";
        EvidenceRetentionSelectedTenantId = "";
        EvidenceRetentionSelectedRetentionClass = "";
        EvidenceRetentionSelectedRetentionUntil = "";
        EvidenceRetentionSelectedLegalHold = false;
        EvidenceRetentionSelectedReviewedBy = "";
        EvidenceRetentionSelectedReviewedAt = "";
        EvidenceRetentionNewRetentionUntilUtc = "";
        EvidenceRetentionUpdateReason = "";
        EvidenceRetentionUpdateTicketId = "";
        EvidenceRetentionLegalHoldNewValue = false;
        EvidenceRetentionLegalHoldReason = "";
        EvidenceRetentionLegalHoldTicketId = "";
        EvidenceRetentionArchiveReason = "";
        EvidenceRetentionArchiveTicketId = "";
        EvidenceRetentionExpireReason = "";
        EvidenceRetentionExpireTicketId = "";
        EvidenceRetentionOutcome = "";
        EvidenceRetentionStatusMessage = "Cleared.";
        EvidenceRetentionLastErrorCode = "";
        EvidenceRetentionLastErrorMessage = "";
        EvidenceRetentionLastRunAtUtc = null;
        ResetEvidenceRetentionLists();
    }

    private void ApplySelectedRetentionBundle(EvidenceBundleResponseDto dto)
    {
        EvidenceRetentionSelectedEvidenceType   = dto.EvidenceType ?? "";
        EvidenceRetentionSelectedPhase          = dto.Phase ?? "";
        EvidenceRetentionSelectedStatus         = dto.Status ?? "";
        EvidenceRetentionSelectedTenantId       = dto.TenantId ?? "";
        EvidenceRetentionSelectedRetentionClass = dto.RetentionClass ?? "";
        EvidenceRetentionSelectedRetentionUntil = dto.RetentionUntil ?? "";
        EvidenceRetentionSelectedLegalHold      = dto.LegalHold;
        EvidenceRetentionSelectedReviewedBy     = dto.ReviewedBy?.ToString() ?? "";
        EvidenceRetentionSelectedReviewedAt     = dto.ReviewedAt ?? "";
    }

    private void ApplyRetentionError(string? code, string? message, string operation)
    {
        EvidenceRetentionOutcome          = "Failed";
        EvidenceRetentionLastErrorCode    = code ?? "UNKNOWN";
        EvidenceRetentionLastErrorMessage = ScrubForDisplay(message ?? "");
        EvidenceRetentionStatusMessage    = ScrubForDisplay(
            $"{operation} failed: {EvidenceRetentionLastErrorCode}.");
        EvidenceRetentionErrors.Add(ScrubForDisplay($"[{EvidenceRetentionLastErrorCode}] {message ?? ""}"));
    }

    private void ResetEvidenceRetentionLists()
    {
        EvidenceRetentionWarnings.Clear();
        EvidenceRetentionErrors.Clear();
    }

    // ── Phase 10.22P — Lifecycle Scheduler Status (read-only monitoring) ─────

    // Flag state
    [ObservableProperty] private bool   _lifecycleSchedulerEnabled;
    [ObservableProperty] private string _lifecycleSchedulerStatusText   = "";
    [ObservableProperty] private bool   _lifecycleSchedulerManualRunEnabled;
    [ObservableProperty] private string _lifecycleSchedulerManualRunStatusText = "";

    // Shared outcome / error
    [ObservableProperty] private string _lifecycleSchedulerStatusMessage = "";
    [ObservableProperty] private string _lifecycleSchedulerErrorCode     = "";
    [ObservableProperty] private string _lifecycleSchedulerErrorMessage  = "";

    // Retention sweeper — run list
    public System.Collections.ObjectModel.ObservableCollection<RetentionSweepRunResponseDto>
        RetentionSweepRuns { get; } = new();
    [ObservableProperty] private long   _retentionRunsTotalElements;
    [ObservableProperty] private int    _retentionRunsPage;

    // Retention sweeper — selected run detail
    [ObservableProperty] private string   _retentionRunLastUuid        = "";
    [ObservableProperty] private string   _retentionRunLastStatus      = "";
    [ObservableProperty] private string   _retentionRunLastTriggerType = "";
    [ObservableProperty] private bool     _retentionRunLastDryRun;
    [ObservableProperty] private string   _retentionRunLastStartedAt   = "";
    [ObservableProperty] private string   _retentionRunLastFinishedAt  = "";
    [ObservableProperty] private string   _retentionRunLastCandidateCount = "";
    [ObservableProperty] private string   _retentionRunLastArchivedCount  = "";
    [ObservableProperty] private string   _retentionRunLastSkippedCount   = "";
    [ObservableProperty] private string   _retentionRunLastFailedCount    = "";
    [ObservableProperty] private string   _retentionRunLastErrorCode      = "";
    [ObservableProperty] private string   _retentionRunLastErrorMessage   = "";
    [ObservableProperty] private string   _retentionRunLastCreatedBy      = "";

    // Retention sweeper — manual run inputs
    [ObservableProperty] private bool   _retentionManualRunDryRun = true;
    [ObservableProperty] private string _retentionManualRunReason  = "";
    [ObservableProperty] private string _retentionManualRunTicketId = "";
    [ObservableProperty] private string _retentionManualRunOutcome  = "";

    // Expiration sweeper — run list
    public System.Collections.ObjectModel.ObservableCollection<ExpirationSweepRunResponseDto>
        ExpirationSweepRuns { get; } = new();
    [ObservableProperty] private long   _expirationRunsTotalElements;
    [ObservableProperty] private int    _expirationRunsPage;

    // Expiration sweeper — selected run detail
    [ObservableProperty] private string   _expirationRunLastUuid           = "";
    [ObservableProperty] private string   _expirationRunLastStatus         = "";
    [ObservableProperty] private string   _expirationRunLastTriggerType    = "";
    [ObservableProperty] private bool     _expirationRunLastDryRun;
    [ObservableProperty] private string   _expirationRunLastStartedAt      = "";
    [ObservableProperty] private string   _expirationRunLastFinishedAt     = "";
    [ObservableProperty] private string   _expirationRunLastCandidateCount = "";
    [ObservableProperty] private string   _expirationRunLastExpiredCount   = "";
    [ObservableProperty] private string   _expirationRunLastSkippedCount   = "";
    [ObservableProperty] private string   _expirationRunLastFailedCount    = "";
    [ObservableProperty] private string   _expirationRunLastErrorCode      = "";
    [ObservableProperty] private string   _expirationRunLastErrorMessage   = "";
    [ObservableProperty] private string   _expirationRunLastCreatedBy      = "";

    // Expiration sweeper — manual run inputs
    [ObservableProperty] private bool   _expirationManualRunDryRun = true;
    [ObservableProperty] private string _expirationManualRunReason   = "";
    [ObservableProperty] private string _expirationManualRunTicketId = "";
    [ObservableProperty] private string _expirationManualRunOutcome  = "";

    // ── Flag refresh ──────────────────────────────────────────────────────────

    private void RefreshLifecycleSchedulerFlag()
    {
        LifecycleSchedulerEnabled = _lifecycleScheduler.IsEnabled();
        LifecycleSchedulerStatusText = LifecycleSchedulerEnabled
            ? $"Enabled (\"{LifecycleSchedulerStatusUiFlagKey}\"=1)"
            : $"Disabled (set {LifecycleSchedulerStatusUiFlagKey}=1 to enable)";

        LifecycleSchedulerManualRunEnabled = _lifecycleScheduler.IsManualRunEnabled();
        LifecycleSchedulerManualRunStatusText = LifecycleSchedulerManualRunEnabled
            ? $"Enabled (\"{LifecycleSchedulerManualRunFlagKey}\"=1)"
            : $"Disabled (set {LifecycleSchedulerManualRunFlagKey}=1 to enable)";
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void RefreshLifecycleSchedulerStatus()
    {
        RefreshLifecycleSchedulerFlag();
        LifecycleSchedulerStatusMessage = "Flag state refreshed.";
        LifecycleSchedulerErrorCode     = "";
        LifecycleSchedulerErrorMessage  = "";
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task ListRetentionSweepRunsAsync()
    {
        RefreshLifecycleSchedulerFlag();
        if (!LifecycleSchedulerEnabled)
        {
            LifecycleSchedulerStatusMessage = LifecycleSchedulerStatusText;
            return;
        }
        LifecycleSchedulerStatusMessage = "Loading retention sweeper run history…";
        LifecycleSchedulerErrorCode     = "";
        LifecycleSchedulerErrorMessage  = "";

        var outcome = await _lifecycleScheduler
            .ListRetentionRunsAsync(RetentionRunsPage, size: 20)
            .ConfigureAwait(true);

        if (outcome.Succeeded && outcome.Value != null)
        {
            RetentionSweepRuns.Clear();
            foreach (var r in outcome.Value.Content)
                RetentionSweepRuns.Add(r);
            RetentionRunsTotalElements = outcome.Value.TotalElements;
            LifecycleSchedulerStatusMessage = $"Retention runs loaded ({outcome.Value.TotalElements} total).";
        }
        else
        {
            LifecycleSchedulerErrorCode    = ScrubForDisplay(outcome.ErrorCode ?? "UNKNOWN");
            LifecycleSchedulerErrorMessage = ScrubForDisplay(outcome.SafeMessage ?? "");
            LifecycleSchedulerStatusMessage = $"List retention runs failed: {LifecycleSchedulerErrorCode}.";
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task SelectRetentionSweepRunAsync(RetentionSweepRunResponseDto? run)
    {
        if (run == null) return;
        ApplySelectedRetentionSweepRun(run);

        var outcome = await _lifecycleScheduler
            .GetRetentionRunAsync(run.RunUuid ?? "")
            .ConfigureAwait(true);

        if (outcome.Succeeded && outcome.Value != null)
            ApplySelectedRetentionSweepRun(outcome.Value);
        else
        {
            LifecycleSchedulerErrorCode    = ScrubForDisplay(outcome.ErrorCode ?? "UNKNOWN");
            LifecycleSchedulerErrorMessage = ScrubForDisplay(outcome.SafeMessage ?? "");
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task RunRetentionSweepOnceAsync()
    {
        RefreshLifecycleSchedulerFlag();
        if (!LifecycleSchedulerEnabled || !LifecycleSchedulerManualRunEnabled)
        {
            RetentionManualRunOutcome = "Manual run disabled.";
            return;
        }
        RetentionManualRunOutcome           = "Submitting…";
        LifecycleSchedulerErrorCode         = "";
        LifecycleSchedulerErrorMessage      = "";

        var outcome = await _lifecycleScheduler
            .RunRetentionSweepOnceAsync(
                RetentionManualRunDryRun,
                batchLimit: null,
                RetentionManualRunReason,
                RetentionManualRunTicketId)
            .ConfigureAwait(true);

        if (outcome.Succeeded && outcome.Value != null)
        {
            ApplySelectedRetentionSweepRun(outcome.Value);
            RetentionManualRunOutcome       = $"Run submitted — status: {outcome.Value.Status ?? "unknown"}.";
            LifecycleSchedulerStatusMessage = RetentionManualRunOutcome;
        }
        else
        {
            LifecycleSchedulerErrorCode    = ScrubForDisplay(outcome.ErrorCode ?? "UNKNOWN");
            LifecycleSchedulerErrorMessage = ScrubForDisplay(outcome.SafeMessage ?? "");
            RetentionManualRunOutcome       = $"Failed: {LifecycleSchedulerErrorCode}.";
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task ListExpirationSweepRunsAsync()
    {
        RefreshLifecycleSchedulerFlag();
        if (!LifecycleSchedulerEnabled)
        {
            LifecycleSchedulerStatusMessage = LifecycleSchedulerStatusText;
            return;
        }
        LifecycleSchedulerStatusMessage = "Loading expiration sweeper run history…";
        LifecycleSchedulerErrorCode     = "";
        LifecycleSchedulerErrorMessage  = "";

        var outcome = await _lifecycleScheduler
            .ListExpirationRunsAsync(ExpirationRunsPage, size: 20)
            .ConfigureAwait(true);

        if (outcome.Succeeded && outcome.Value != null)
        {
            ExpirationSweepRuns.Clear();
            foreach (var r in outcome.Value.Content)
                ExpirationSweepRuns.Add(r);
            ExpirationRunsTotalElements = outcome.Value.TotalElements;
            LifecycleSchedulerStatusMessage = $"Expiration runs loaded ({outcome.Value.TotalElements} total).";
        }
        else
        {
            LifecycleSchedulerErrorCode    = ScrubForDisplay(outcome.ErrorCode ?? "UNKNOWN");
            LifecycleSchedulerErrorMessage = ScrubForDisplay(outcome.SafeMessage ?? "");
            LifecycleSchedulerStatusMessage = $"List expiration runs failed: {LifecycleSchedulerErrorCode}.";
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task SelectExpirationSweepRunAsync(ExpirationSweepRunResponseDto? run)
    {
        if (run == null) return;
        ApplySelectedExpirationSweepRun(run);

        var outcome = await _lifecycleScheduler
            .GetExpirationRunAsync(run.RunUuid ?? "")
            .ConfigureAwait(true);

        if (outcome.Succeeded && outcome.Value != null)
            ApplySelectedExpirationSweepRun(outcome.Value);
        else
        {
            LifecycleSchedulerErrorCode    = ScrubForDisplay(outcome.ErrorCode ?? "UNKNOWN");
            LifecycleSchedulerErrorMessage = ScrubForDisplay(outcome.SafeMessage ?? "");
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task RunExpirationSweepOnceAsync()
    {
        RefreshLifecycleSchedulerFlag();
        if (!LifecycleSchedulerEnabled || !LifecycleSchedulerManualRunEnabled)
        {
            ExpirationManualRunOutcome = "Manual run disabled.";
            return;
        }
        ExpirationManualRunOutcome          = "Submitting…";
        LifecycleSchedulerErrorCode         = "";
        LifecycleSchedulerErrorMessage      = "";

        var outcome = await _lifecycleScheduler
            .RunExpirationSweepOnceAsync(
                ExpirationManualRunDryRun,
                batchLimit: null,
                ExpirationManualRunReason,
                ExpirationManualRunTicketId)
            .ConfigureAwait(true);

        if (outcome.Succeeded && outcome.Value != null)
        {
            ApplySelectedExpirationSweepRun(outcome.Value);
            ExpirationManualRunOutcome      = $"Run submitted — status: {outcome.Value.Status ?? "unknown"}.";
            LifecycleSchedulerStatusMessage = ExpirationManualRunOutcome;
        }
        else
        {
            LifecycleSchedulerErrorCode    = ScrubForDisplay(outcome.ErrorCode ?? "UNKNOWN");
            LifecycleSchedulerErrorMessage = ScrubForDisplay(outcome.SafeMessage ?? "");
            ExpirationManualRunOutcome      = $"Failed: {LifecycleSchedulerErrorCode}.";
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void ApplySelectedRetentionSweepRun(RetentionSweepRunResponseDto r)
    {
        RetentionRunLastUuid            = ScrubForDisplay(r.RunUuid ?? "");
        RetentionRunLastStatus          = ScrubForDisplay(r.Status ?? "");
        RetentionRunLastTriggerType     = ScrubForDisplay(r.TriggerType ?? "");
        RetentionRunLastDryRun          = r.DryRun;
        RetentionRunLastStartedAt       = r.StartedAt?.ToString("O") ?? "";
        RetentionRunLastFinishedAt      = r.FinishedAt?.ToString("O") ?? "";
        RetentionRunLastCandidateCount  = r.CandidateCount?.ToString() ?? "";
        RetentionRunLastArchivedCount   = r.ArchivedCount?.ToString()  ?? "";
        RetentionRunLastSkippedCount    = r.SkippedCount?.ToString()   ?? "";
        RetentionRunLastFailedCount     = r.FailedCount?.ToString()    ?? "";
        RetentionRunLastErrorCode       = ScrubForDisplay(r.SafeErrorCode    ?? "");
        RetentionRunLastErrorMessage    = ScrubForDisplay(r.SafeErrorMessage ?? "");
        RetentionRunLastCreatedBy       = ScrubForDisplay(r.CreatedBy ?? "");
    }

    private void ApplySelectedExpirationSweepRun(ExpirationSweepRunResponseDto r)
    {
        ExpirationRunLastUuid            = ScrubForDisplay(r.RunUuid ?? "");
        ExpirationRunLastStatus          = ScrubForDisplay(r.Status ?? "");
        ExpirationRunLastTriggerType     = ScrubForDisplay(r.TriggerType ?? "");
        ExpirationRunLastDryRun          = r.DryRun;
        ExpirationRunLastStartedAt       = r.StartedAt?.ToString("O")  ?? "";
        ExpirationRunLastFinishedAt      = r.FinishedAt?.ToString("O") ?? "";
        ExpirationRunLastCandidateCount  = r.CandidateCount?.ToString() ?? "";
        ExpirationRunLastExpiredCount    = r.ExpiredCount?.ToString()   ?? "";
        ExpirationRunLastSkippedCount    = r.SkippedCount?.ToString()   ?? "";
        ExpirationRunLastFailedCount     = r.FailedCount?.ToString()    ?? "";
        ExpirationRunLastErrorCode       = ScrubForDisplay(r.SafeErrorCode    ?? "");
        ExpirationRunLastErrorMessage    = ScrubForDisplay(r.SafeErrorMessage ?? "");
        ExpirationRunLastCreatedBy       = ScrubForDisplay(r.CreatedBy ?? "");
    }
}
