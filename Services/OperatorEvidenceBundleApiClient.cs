using System;
using System.Threading;
using System.Threading.Tasks;
using PosSystem.Core.DTOs;

namespace PosSystem.Services;

// Phase 10.22F desktop wrapper around the Phase 10.22C/D backend
// evidence bundle endpoints. Mirrors the OperatorAuditEvidenceApiClient
// / OperatorPermissionApiClient idiom:
//   • Re-throws OperationCanceledException for cooperative cancellation.
//   • Catches every other exception (network / DNS / TLS / JSON / etc.)
//     and converts it into a typed `Failure` outcome with code
//     "NETWORK_FAILURE" so the orchestrator can branch without
//     crashing the UI thread.
//   • Backend-shaped errors (FEATURE_FLAG_OFF, REDACTION_FAILED, …)
//     are already typed by ApiClient.ReadEvidenceBundleOutcomeAsync and
//     flow through unchanged.
//   • Never logs Authorization headers, refresh tokens, multipart
//     bodies, file content, or absolute paths.
public sealed class OperatorEvidenceBundleApiClient
{
    private readonly ApiClient _api;

    public OperatorEvidenceBundleApiClient(ApiClient api)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
    }

    public Task<EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>> CreateAsync(
        EvidenceBundleCreateRequestDto request, CancellationToken ct = default) =>
        SafeAsync(() => _api.CreateEvidenceBundleAsync(request, ct));

    public Task<EvidenceBundleApiCallOutcome<EvidenceBundleUploadResponseDto>> UploadFileAsync(
        string bundleUuid,
        string relativePath,
        string absoluteSourcePath,
        bool redacted,
        string? declaredSha256,
        string? contentType,
        CancellationToken ct = default) =>
        SafeAsync(() => _api.UploadEvidenceBundleFileAsync(
            bundleUuid, relativePath, absoluteSourcePath, redacted, declaredSha256, contentType, ct));

    public Task<EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>> FinalizeAsync(
        string bundleUuid,
        EvidenceBundleFinalizeRequestDto? request,
        CancellationToken ct = default) =>
        SafeAsync(() => _api.FinalizeEvidenceBundleAsync(bundleUuid, request, ct));

    public Task<EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>> GetAsync(
        string bundleUuid, CancellationToken ct = default) =>
        SafeAsync(() => _api.GetEvidenceBundleAsync(bundleUuid, ct));

    public Task<EvidenceBundleApiCallOutcome<EvidenceBundlePageResponseDto>> ListAsync(
        string? evidenceType, string? phase, string? tenantId, string? status,
        int page, int size, CancellationToken ct = default) =>
        SafeAsync(() => _api.ListEvidenceBundlesAsync(
            evidenceType, phase, tenantId, status, page, size, ct));

    // ── Phase 10.22G — reviewer decision + binary download ──────────────────

    public Task<EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>> ReviewAsync(
        string bundleUuid,
        EvidenceBundleReviewRequestDto request,
        CancellationToken ct = default) =>
        SafeAsync(() => _api.ReviewEvidenceBundleAsync(bundleUuid, request, ct));

    public Task<EvidenceBundleApiCallOutcome<EvidenceBundleDownloadResultDto>> DownloadAsync(
        string bundleUuid,
        string destinationFolder,
        bool allowOverwrite,
        CancellationToken ct = default) =>
        SafeAsync(() => _api.DownloadEvidenceBundleAsync(
            bundleUuid, destinationFolder, allowOverwrite, ct));

    // ── Phase 10.22H — retention / legal hold / archive / expire ──────────

    public Task<EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>> UpdateRetentionAsync(
        string bundleUuid,
        EvidenceBundleRetentionRequestDto request,
        CancellationToken ct = default) =>
        SafeAsync(() => _api.UpdateEvidenceBundleRetentionAsync(bundleUuid, request, ct));

    public Task<EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>> UpdateLegalHoldAsync(
        string bundleUuid,
        EvidenceBundleLegalHoldRequestDto request,
        CancellationToken ct = default) =>
        SafeAsync(() => _api.UpdateEvidenceBundleLegalHoldAsync(bundleUuid, request, ct));

    public Task<EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>> ArchiveAsync(
        string bundleUuid,
        EvidenceBundleArchiveRequestDto request,
        CancellationToken ct = default) =>
        SafeAsync(() => _api.ArchiveEvidenceBundleAsync(bundleUuid, request, ct));

    public Task<EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>> ExpireAsync(
        string bundleUuid,
        EvidenceBundleExpireRequestDto request,
        CancellationToken ct = default) =>
        SafeAsync(() => _api.ExpireEvidenceBundleAsync(bundleUuid, request, ct));

    public Task<EvidenceBundleApiCallOutcome<EvidenceBundlePageResponseDto>> ListRetentionCandidatesAsync(
        string? evidenceType, string? status, string? tenantId,
        string? beforeIsoUtc, int page, int size,
        CancellationToken ct = default) =>
        SafeAsync(() => _api.ListEvidenceBundleRetentionCandidatesAsync(
            evidenceType, status, tenantId, beforeIsoUtc, page, size, ct));

    // ── Phase 10.22P — lifecycle scheduler run-history ──────────────────────

    public Task<EvidenceBundleApiCallOutcome<RetentionSweepRunPageResponseDto>> ListRetentionSweepRunsAsync(
        int page = 0, int size = 20, CancellationToken ct = default) =>
        SafeAsync(() => _api.ListRetentionSweepRunsAsync(page, size, ct));

    public Task<EvidenceBundleApiCallOutcome<RetentionSweepRunResponseDto>> GetRetentionSweepRunAsync(
        string runUuid, CancellationToken ct = default) =>
        SafeAsync(() => _api.GetRetentionSweepRunAsync(runUuid, ct));

    public Task<EvidenceBundleApiCallOutcome<RetentionSweepRunResponseDto>> RunRetentionSweepOnceAsync(
        RetentionSweepRunRequestDto request, CancellationToken ct = default) =>
        SafeAsync(() => _api.RunRetentionSweepOnceAsync(request, ct));

    public Task<EvidenceBundleApiCallOutcome<ExpirationSweepRunPageResponseDto>> ListExpirationSweepRunsAsync(
        int page = 0, int size = 20, CancellationToken ct = default) =>
        SafeAsync(() => _api.ListExpirationSweepRunsAsync(page, size, ct));

    public Task<EvidenceBundleApiCallOutcome<ExpirationSweepRunResponseDto>> GetExpirationSweepRunAsync(
        string runUuid, CancellationToken ct = default) =>
        SafeAsync(() => _api.GetExpirationSweepRunAsync(runUuid, ct));

    public Task<EvidenceBundleApiCallOutcome<ExpirationSweepRunResponseDto>> RunExpirationSweepOnceAsync(
        ExpirationSweepRunRequestDto request, CancellationToken ct = default) =>
        SafeAsync(() => _api.RunExpirationSweepOnceAsync(request, ct));

    private static async Task<EvidenceBundleApiCallOutcome<T>> SafeAsync<T>(
        Func<Task<EvidenceBundleApiCallOutcome<T>>> call)
        where T : class
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            // Defence in depth: ApiClient already typed the failure
            // path; this branch covers transport-layer exceptions thrown
            // before the response arrives (DNS resolution, TLS, …).
            return EvidenceBundleApiCallOutcome<T>.Failure(
                "NETWORK_FAILURE",
                "Backend is unreachable.",
                httpStatus: 0);
        }
    }
}
