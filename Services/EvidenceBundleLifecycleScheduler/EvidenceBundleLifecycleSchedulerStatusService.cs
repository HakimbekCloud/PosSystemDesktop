using System;
using System.Threading;
using System.Threading.Tasks;
using PosSystem.Core.DTOs;
using PosSystem.Data.Repositories;

namespace PosSystem.Services.EvidenceBundleLifecycleScheduler;

// Phase 10.22P — desktop read-only orchestrator for evidence bundle
// lifecycle scheduler run history (Phase 10.22N retention archive sweeper
// and Phase 10.22O expiration sweeper).
//
// Behaviour:
//   • Gated by local default-OFF flag
//     `operator_evidence_bundle_lifecycle_scheduler_status_ui_enabled`.
//     When OFF every read method returns FEATURE_FLAG_OFF and no HTTP call
//     is made.
//   • Optional manual-run sub-flag
//     `operator_evidence_bundle_lifecycle_scheduler_manual_run_ui_enabled`
//     (default OFF). When ON the ViewModel enables manual-run buttons.
//   • NO hard-delete. NO storage object deletion. NO dangerous-execute.
//     NO confirmation-phrase input. NO storage-path display.
//   • All user-visible strings are scrubbed via ScrubForDisplay in the
//     ViewModel — this service never surfaces storage key, bucket, endpoint,
//     raw path, or credential.
public sealed class EvidenceBundleLifecycleSchedulerStatusService
{
    public const string LocalFlagKey     = "operator_evidence_bundle_lifecycle_scheduler_status_ui_enabled";
    public const string ManualRunFlagKey = "operator_evidence_bundle_lifecycle_scheduler_manual_run_ui_enabled";

    private readonly GlobalSettingsRepository _global;
    private readonly OperatorEvidenceBundleApiClient _api;

    public EvidenceBundleLifecycleSchedulerStatusService(
        GlobalSettingsRepository global,
        OperatorEvidenceBundleApiClient api)
    {
        _global = global ?? throw new ArgumentNullException(nameof(global));
        _api    = api    ?? throw new ArgumentNullException(nameof(api));
    }

    public bool IsEnabled()          => string.Equals(_global.Get(LocalFlagKey),     "1", StringComparison.Ordinal);
    public bool IsManualRunEnabled() => string.Equals(_global.Get(ManualRunFlagKey), "1", StringComparison.Ordinal);

    // ── Retention sweeper runs (Phase 10.22N) ────────────────────────────────

    public async Task<EvidenceBundleApiCallOutcome<RetentionSweepRunPageResponseDto>> ListRetentionRunsAsync(
        int page = 0, int size = 20, CancellationToken ct = default)
    {
        if (!IsEnabled()) return DisabledRetentionList();
        return await _api.ListRetentionSweepRunsAsync(page, size, ct).ConfigureAwait(false);
    }

    public async Task<EvidenceBundleApiCallOutcome<RetentionSweepRunResponseDto>> GetRetentionRunAsync(
        string runUuid, CancellationToken ct = default)
    {
        if (!IsEnabled()) return DisabledRetentionGet();
        if (string.IsNullOrWhiteSpace(runUuid))
            return EvidenceBundleApiCallOutcome<RetentionSweepRunResponseDto>.Failure(
                "MISSING_RUN_UUID", "Select a run first.", httpStatus: 0);
        return await _api.GetRetentionSweepRunAsync(runUuid, ct).ConfigureAwait(false);
    }

    public async Task<EvidenceBundleApiCallOutcome<RetentionSweepRunResponseDto>> RunRetentionSweepOnceAsync(
        bool dryRun, int? batchLimit, string? reason, string? ticketId,
        CancellationToken ct = default)
    {
        if (!IsEnabled() || !IsManualRunEnabled())
            return EvidenceBundleApiCallOutcome<RetentionSweepRunResponseDto>.Failure(
                "FEATURE_FLAG_OFF",
                $"Manual run UI is disabled (set {ManualRunFlagKey}=1 to enable).",
                httpStatus: 0);
        var request = new RetentionSweepRunRequestDto
        {
            DryRun     = dryRun,
            BatchLimit = batchLimit,
            Reason     = string.IsNullOrWhiteSpace(reason)   ? null : reason.Trim(),
            TicketId   = string.IsNullOrWhiteSpace(ticketId) ? null : ticketId.Trim(),
        };
        return await _api.RunRetentionSweepOnceAsync(request, ct).ConfigureAwait(false);
    }

    // ── Expiration sweeper runs (Phase 10.22O) ───────────────────────────────

    public async Task<EvidenceBundleApiCallOutcome<ExpirationSweepRunPageResponseDto>> ListExpirationRunsAsync(
        int page = 0, int size = 20, CancellationToken ct = default)
    {
        if (!IsEnabled()) return DisabledExpirationList();
        return await _api.ListExpirationSweepRunsAsync(page, size, ct).ConfigureAwait(false);
    }

    public async Task<EvidenceBundleApiCallOutcome<ExpirationSweepRunResponseDto>> GetExpirationRunAsync(
        string runUuid, CancellationToken ct = default)
    {
        if (!IsEnabled()) return DisabledExpirationGet();
        if (string.IsNullOrWhiteSpace(runUuid))
            return EvidenceBundleApiCallOutcome<ExpirationSweepRunResponseDto>.Failure(
                "MISSING_RUN_UUID", "Select a run first.", httpStatus: 0);
        return await _api.GetExpirationSweepRunAsync(runUuid, ct).ConfigureAwait(false);
    }

    public async Task<EvidenceBundleApiCallOutcome<ExpirationSweepRunResponseDto>> RunExpirationSweepOnceAsync(
        bool dryRun, int? batchLimit, string? reason, string? ticketId,
        CancellationToken ct = default)
    {
        if (!IsEnabled() || !IsManualRunEnabled())
            return EvidenceBundleApiCallOutcome<ExpirationSweepRunResponseDto>.Failure(
                "FEATURE_FLAG_OFF",
                $"Manual run UI is disabled (set {ManualRunFlagKey}=1 to enable).",
                httpStatus: 0);
        var request = new ExpirationSweepRunRequestDto
        {
            DryRun     = dryRun,
            BatchLimit = batchLimit,
            Reason     = string.IsNullOrWhiteSpace(reason)   ? null : reason.Trim(),
            TicketId   = string.IsNullOrWhiteSpace(ticketId) ? null : ticketId.Trim(),
        };
        return await _api.RunExpirationSweepOnceAsync(request, ct).ConfigureAwait(false);
    }

    // ── Disabled-state shortcuts ──────────────────────────────────────────────

    private static EvidenceBundleApiCallOutcome<RetentionSweepRunPageResponseDto> DisabledRetentionList() =>
        EvidenceBundleApiCallOutcome<RetentionSweepRunPageResponseDto>.Failure(
            "FEATURE_FLAG_OFF",
            $"Lifecycle scheduler status UI is disabled (set {LocalFlagKey}=1 to enable).",
            httpStatus: 0);

    private static EvidenceBundleApiCallOutcome<RetentionSweepRunResponseDto> DisabledRetentionGet() =>
        EvidenceBundleApiCallOutcome<RetentionSweepRunResponseDto>.Failure(
            "FEATURE_FLAG_OFF",
            $"Lifecycle scheduler status UI is disabled (set {LocalFlagKey}=1 to enable).",
            httpStatus: 0);

    private static EvidenceBundleApiCallOutcome<ExpirationSweepRunPageResponseDto> DisabledExpirationList() =>
        EvidenceBundleApiCallOutcome<ExpirationSweepRunPageResponseDto>.Failure(
            "FEATURE_FLAG_OFF",
            $"Lifecycle scheduler status UI is disabled (set {LocalFlagKey}=1 to enable).",
            httpStatus: 0);

    private static EvidenceBundleApiCallOutcome<ExpirationSweepRunResponseDto> DisabledExpirationGet() =>
        EvidenceBundleApiCallOutcome<ExpirationSweepRunResponseDto>.Failure(
            "FEATURE_FLAG_OFF",
            $"Lifecycle scheduler status UI is disabled (set {LocalFlagKey}=1 to enable).",
            httpStatus: 0);
}
