using System;
using System.Threading;
using System.Threading.Tasks;
using PosSystem.Core.DTOs;
using PosSystem.Data.Repositories;

namespace PosSystem.Services.EvidenceBundleRetention;

// Phase 10.22H desktop orchestrator for the retention + legal hold
// workflow. Calls the Phase 10.22H backend endpoints
//   POST /api/v1/operator/evidence/bundles/{uuid}/retention
//   POST /api/v1/operator/evidence/bundles/{uuid}/legal-hold
//   POST /api/v1/operator/evidence/bundles/{uuid}/archive
//   POST /api/v1/operator/evidence/bundles/{uuid}/expire
//   GET  /api/v1/operator/evidence/bundles/retention-candidates
// plus a thin GetAsync passthrough for the metadata refresh.
//
// Behaviour:
//   • Gated by the local default-OFF flag
//     `operator_evidence_bundle_retention_ui_enabled`. When OFF every
//     method returns a FEATURE_FLAG_OFF outcome and no backend call
//     is made.
//   • NO upload / finalize / delete / hard-delete / dangerous-execute
//     call. NO confirmation-phrase input. NO storage-path display.
//   • Surfaces backend error codes verbatim (e.g. FEATURE_FLAG_OFF,
//     FORBIDDEN, LEGAL_HOLD_ACTIVE, RETENTION_INVALID,
//     RETENTION_TICKET_REQUIRED, ARCHIVE_NOT_ALLOWED,
//     EXPIRE_NOT_ALLOWED).
public sealed class EvidenceBundleRetentionService
{
    public const string LocalFlagKey = "operator_evidence_bundle_retention_ui_enabled";

    private readonly GlobalSettingsRepository _global;
    private readonly OperatorEvidenceBundleApiClient _api;

    public EvidenceBundleRetentionService(
        GlobalSettingsRepository global,
        OperatorEvidenceBundleApiClient api)
    {
        _global = global ?? throw new ArgumentNullException(nameof(global));
        _api    = api    ?? throw new ArgumentNullException(nameof(api));
    }

    public bool IsEnabled() =>
        string.Equals(_global.Get(LocalFlagKey), "1", StringComparison.Ordinal);

    // ── Read-only metadata passthrough (so the card does not need a second
    //    service injection for the existing GET endpoint).
    public async Task<EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>> GetAsync(
        string bundleUuid, CancellationToken ct = default)
    {
        if (!IsEnabled()) return DisabledGet();
        if (string.IsNullOrWhiteSpace(bundleUuid))
        {
            return EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>.Failure(
                "MISSING_BUNDLE_UUID", "Select a bundle first.", httpStatus: 0);
        }
        return await _api.GetAsync(bundleUuid, ct).ConfigureAwait(false);
    }

    // ── Retention candidates listing ──────────────────────────────────────

    public async Task<EvidenceBundleApiCallOutcome<EvidenceBundlePageResponseDto>> ListCandidatesAsync(
        string? evidenceType, string? status, string? tenantId,
        string? beforeIsoUtc, int page, int size,
        CancellationToken ct = default)
    {
        if (!IsEnabled()) return DisabledList();
        return await _api.ListRetentionCandidatesAsync(
                evidenceType, status, tenantId, beforeIsoUtc, page, size, ct)
            .ConfigureAwait(false);
    }

    // ── Update retentionUntil ─────────────────────────────────────────────

    public async Task<EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>> UpdateRetentionAsync(
        string bundleUuid,
        string retentionUntilIsoUtc,
        string reason,
        string ticketId,
        CancellationToken ct = default)
    {
        if (!IsEnabled()) return DisabledGet();
        var preflight = PreflightCommon(bundleUuid, reason, ticketId);
        if (preflight != null) return preflight;
        if (string.IsNullOrWhiteSpace(retentionUntilIsoUtc))
        {
            return EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>.Failure(
                "RETENTION_INVALID", "retentionUntil is required.", httpStatus: 0);
        }
        var request = new EvidenceBundleRetentionRequestDto
        {
            RetentionUntil = retentionUntilIsoUtc.Trim(),
            Reason         = reason.Trim(),
            TicketId       = ticketId.Trim(),
        };
        return await _api.UpdateRetentionAsync(bundleUuid, request, ct).ConfigureAwait(false);
    }

    // ── Toggle legal hold ────────────────────────────────────────────────

    public async Task<EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>> UpdateLegalHoldAsync(
        string bundleUuid,
        bool legalHold,
        string reason,
        string ticketId,
        CancellationToken ct = default)
    {
        if (!IsEnabled()) return DisabledGet();
        var preflight = PreflightCommon(bundleUuid, reason, ticketId);
        if (preflight != null) return preflight;
        var request = new EvidenceBundleLegalHoldRequestDto
        {
            LegalHold = legalHold,
            Reason    = reason.Trim(),
            TicketId  = ticketId.Trim(),
        };
        return await _api.UpdateLegalHoldAsync(bundleUuid, request, ct).ConfigureAwait(false);
    }

    // ── Archive ──────────────────────────────────────────────────────────

    public async Task<EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>> ArchiveAsync(
        string bundleUuid,
        string reason,
        string ticketId,
        CancellationToken ct = default)
    {
        if (!IsEnabled()) return DisabledGet();
        var preflight = PreflightCommon(bundleUuid, reason, ticketId);
        if (preflight != null) return preflight;
        var request = new EvidenceBundleArchiveRequestDto
        {
            Reason   = reason.Trim(),
            TicketId = ticketId.Trim(),
        };
        return await _api.ArchiveAsync(bundleUuid, request, ct).ConfigureAwait(false);
    }

    // ── Expire ───────────────────────────────────────────────────────────

    public async Task<EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>> ExpireAsync(
        string bundleUuid,
        string reason,
        string ticketId,
        CancellationToken ct = default)
    {
        if (!IsEnabled()) return DisabledGet();
        var preflight = PreflightCommon(bundleUuid, reason, ticketId);
        if (preflight != null) return preflight;
        var request = new EvidenceBundleExpireRequestDto
        {
            Reason   = reason.Trim(),
            TicketId = ticketId.Trim(),
        };
        return await _api.ExpireAsync(bundleUuid, request, ct).ConfigureAwait(false);
    }

    // ── Shared validation ────────────────────────────────────────────────

    private static EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>? PreflightCommon(
        string bundleUuid, string reason, string ticketId)
    {
        if (string.IsNullOrWhiteSpace(bundleUuid))
        {
            return EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>.Failure(
                "MISSING_BUNDLE_UUID", "Select a bundle first.", httpStatus: 0);
        }
        if (string.IsNullOrWhiteSpace(reason))
        {
            return EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>.Failure(
                "RETENTION_TICKET_REQUIRED", "Reason is required.", httpStatus: 0);
        }
        if (string.IsNullOrWhiteSpace(ticketId))
        {
            return EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>.Failure(
                "RETENTION_TICKET_REQUIRED", "ticketId is required.", httpStatus: 0);
        }
        return null;
    }

    // ── Disabled-state shortcuts ─────────────────────────────────────────

    private static EvidenceBundleApiCallOutcome<EvidenceBundlePageResponseDto> DisabledList() =>
        EvidenceBundleApiCallOutcome<EvidenceBundlePageResponseDto>.Failure(
            "FEATURE_FLAG_OFF",
            $"Retention UI is disabled (set {LocalFlagKey}=1 to enable).",
            httpStatus: 0);

    private static EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto> DisabledGet() =>
        EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>.Failure(
            "FEATURE_FLAG_OFF",
            $"Retention UI is disabled (set {LocalFlagKey}=1 to enable).",
            httpStatus: 0);
}
