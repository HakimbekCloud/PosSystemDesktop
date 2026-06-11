using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PosSystem.Core.DTOs;
using PosSystem.Data.Repositories;

namespace PosSystem.Services.EvidenceBundleReview;

// Phase 10.22G desktop orchestrator for the reviewer + download
// workflow. Calls the Phase 10.22G backend endpoints
//   POST /api/v1/operator/evidence/bundles/{uuid}/review
//   GET  /api/v1/operator/evidence/bundles/{uuid}/download
// plus the read-only list/get-metadata endpoints from Phase 10.22C/D.
//
// Behaviour:
//   • Gated by the local default-OFF flag
//     `operator_evidence_bundle_review_ui_enabled`. When OFF every
//     method returns a Disabled-shaped outcome and no backend call
//     is made.
//   • No mutation beyond the review POST and download GET. NO
//     finalize / upload / delete / retention / hard-delete call.
//   • Surfaces backend error codes verbatim (e.g. FEATURE_FLAG_OFF,
//     FORBIDDEN, INVALID_STATUS, SELF_REVIEW_FORBIDDEN,
//     ALREADY_REVIEWED, DOWNLOAD_NOT_AVAILABLE, STORAGE_OBJECT_MISSING).
public sealed class EvidenceBundleReviewService
{
    public const string LocalFlagKey = "operator_evidence_bundle_review_ui_enabled";

    private readonly GlobalSettingsRepository _global;
    private readonly OperatorEvidenceBundleApiClient _api;

    public EvidenceBundleReviewService(
        GlobalSettingsRepository global,
        OperatorEvidenceBundleApiClient api)
    {
        _global = global ?? throw new ArgumentNullException(nameof(global));
        _api    = api    ?? throw new ArgumentNullException(nameof(api));
    }

    public bool IsEnabled() =>
        string.Equals(_global.Get(LocalFlagKey), "1", StringComparison.Ordinal);

    // ── List + Get metadata (read-only) ─────────────────────────────────────

    public async Task<EvidenceBundleApiCallOutcome<EvidenceBundlePageResponseDto>> ListAsync(
        string? evidenceType, string? phase, string? tenantId, string? status,
        int page, int size, CancellationToken ct = default)
    {
        if (!IsEnabled()) return DisabledList();
        return await _api.ListAsync(evidenceType, phase, tenantId, status, page, size, ct)
                         .ConfigureAwait(false);
    }

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

    // ── Review ──────────────────────────────────────────────────────────────

    public async Task<EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>> ReviewAsync(
        string bundleUuid,
        string decision,
        string? reviewNotes,
        CancellationToken ct = default)
    {
        if (!IsEnabled()) return DisabledGet();
        if (string.IsNullOrWhiteSpace(bundleUuid))
        {
            return EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>.Failure(
                "MISSING_BUNDLE_UUID", "Select a bundle first.", httpStatus: 0);
        }
        if (string.IsNullOrWhiteSpace(decision))
        {
            return EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>.Failure(
                "VALIDATION_FAILED", "Decision is required.", httpStatus: 0);
        }
        var request = new EvidenceBundleReviewRequestDto
        {
            Decision    = decision.Trim().ToUpperInvariant(),
            ReviewNotes = string.IsNullOrWhiteSpace(reviewNotes) ? null : reviewNotes,
        };
        return await _api.ReviewAsync(bundleUuid, request, ct).ConfigureAwait(false);
    }

    // ── Download ────────────────────────────────────────────────────────────

    public async Task<EvidenceBundleApiCallOutcome<EvidenceBundleDownloadResultDto>> DownloadAsync(
        string bundleUuid,
        string destinationFolder,
        bool allowOverwrite,
        CancellationToken ct = default)
    {
        if (!IsEnabled())
        {
            return EvidenceBundleApiCallOutcome<EvidenceBundleDownloadResultDto>.Failure(
                "FEATURE_FLAG_OFF",
                $"Review UI is disabled (set {LocalFlagKey}=1 to enable).",
                httpStatus: 0);
        }
        if (string.IsNullOrWhiteSpace(bundleUuid))
        {
            return EvidenceBundleApiCallOutcome<EvidenceBundleDownloadResultDto>.Failure(
                "MISSING_BUNDLE_UUID", "Select a bundle first.", httpStatus: 0);
        }
        if (string.IsNullOrWhiteSpace(destinationFolder))
        {
            return EvidenceBundleApiCallOutcome<EvidenceBundleDownloadResultDto>.Failure(
                "MISSING_DESTINATION_FOLDER", "Choose a download folder first.", httpStatus: 0);
        }
        return await _api.DownloadAsync(bundleUuid, destinationFolder, allowOverwrite, ct)
                         .ConfigureAwait(false);
    }

    // ── Disabled-state shortcuts ────────────────────────────────────────────

    private static EvidenceBundleApiCallOutcome<EvidenceBundlePageResponseDto> DisabledList() =>
        EvidenceBundleApiCallOutcome<EvidenceBundlePageResponseDto>.Failure(
            "FEATURE_FLAG_OFF",
            $"Review UI is disabled (set {LocalFlagKey}=1 to enable).",
            httpStatus: 0);

    private static EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto> DisabledGet() =>
        EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>.Failure(
            "FEATURE_FLAG_OFF",
            $"Review UI is disabled (set {LocalFlagKey}=1 to enable).",
            httpStatus: 0);
}
