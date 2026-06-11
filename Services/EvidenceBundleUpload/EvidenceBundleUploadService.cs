using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PosSystem.Core.DTOs;
using PosSystem.Data.Repositories;
using PosSystem.Services.EvidenceBundleExport;

namespace PosSystem.Services.EvidenceBundleUpload;

// Phase 10.22F desktop orchestrator that uploads a Phase 10.22E
// export (manifest.json + the files it references) to the backend
// Phase 10.22C/D endpoints.
//
// Behaviour:
//   • Gated by the local default-OFF flag
//     `operator_evidence_bundle_upload_ui_enabled`.
//   • Reads the operator-selected folder; reads `manifest.json` at its
//     root; uploads `manifest.json` first then every file listed in
//     `manifest.files[]` in stable ordinal order. Finalize is the
//     last step.
//   • NEVER uploads any `.zip` file. The desktop's Phase 10.22E ZIP
//     remains a local archive only; the backend accepts the bare
//     manifest + per-file streams.
//   • Each file upload passes the manifest's recorded SHA-256 as
//     `declaredSha256`; the backend re-computes server-side and
//     rejects on mismatch.
//   • If the backend rejects any step, the orchestrator stops the
//     workflow immediately. The bundle UUID (if create succeeded)
//     is preserved on the result so the operator can re-run after a
//     transient backend issue.
//   • Re-uses Phase 10.22E `EvidenceBundlePathSafety` +
//     `EvidenceBundleSha256` for local sanity checks before any
//     backend call — a folder that would fail the backend's strict
//     validator is rejected before we open an HTTP socket.
//   • No backend code is touched in Phase 10.22F; this service only
//     consumes the existing endpoints.
public sealed class EvidenceBundleUploadService
{
    public const string LocalFlagKey = "operator_evidence_bundle_upload_ui_enabled";

    private const string ManifestFilename = "manifest.json";
    private const string ExpectedSchemaVersion = "operator-evidence-bundle-v1";

    private readonly GlobalSettingsRepository _global;
    private readonly OperatorEvidenceBundleApiClient _api;

    public EvidenceBundleUploadService(
        GlobalSettingsRepository global,
        OperatorEvidenceBundleApiClient api)
    {
        _global = global ?? throw new ArgumentNullException(nameof(global));
        _api    = api    ?? throw new ArgumentNullException(nameof(api));
    }

    public bool IsEnabled() => string.Equals(_global.Get(LocalFlagKey), "1", StringComparison.Ordinal);

    public sealed class EvidenceBundleUploadInput
    {
        public string SourceFolder { get; init; } = "";
        /// <summary>
        /// Operator-supplied optional notes appended to the finalize
        /// request. Never carries machine names / paths / secrets.
        /// </summary>
        public string? FinalizeNotes { get; init; }
    }

    public async Task<EvidenceBundleUploadResult> RunAsync(
        EvidenceBundleUploadInput input,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var steps     = new List<string>();
        var uploaded  = new List<string>();
        var warnings  = new List<string>();
        var errors    = new List<string>();

        if (!IsEnabled())
        {
            return Disabled();
        }

        if (string.IsNullOrWhiteSpace(input.SourceFolder)
            || !Directory.Exists(input.SourceFolder))
        {
            return LocalBlocked(
                "Selected evidence folder does not exist.",
                steps, uploaded, warnings, errors);
        }

        var sourceRoot = Path.GetFullPath(input.SourceFolder);
        steps.Add($"Reading manifest from selected folder.");
        progress?.Report("Reading manifest.json.");

        var manifestPath = Path.Combine(sourceRoot, ManifestFilename);
        if (!File.Exists(manifestPath))
        {
            return LocalBlocked(
                $"{ManifestFilename} is missing from the selected folder. " +
                "Generate it with the Phase 10.22E local export card before uploading.",
                steps, uploaded, warnings, errors);
        }

        EvidenceBundleManifestDto? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<EvidenceBundleManifestDto>(
                File.ReadAllBytes(manifestPath));
        }
        catch (Exception)
        {
            return LocalBlocked(
                $"{ManifestFilename} could not be parsed as JSON.",
                steps, uploaded, warnings, errors);
        }
        if (manifest is null)
        {
            return LocalBlocked(
                $"{ManifestFilename} parsed as null.",
                steps, uploaded, warnings, errors);
        }

        var localCheck = ValidateManifestLocally(manifest, sourceRoot, errors);
        if (!localCheck)
        {
            return LocalBlocked(
                "Local manifest validation failed. See errors list.",
                steps, uploaded, warnings, errors);
        }
        steps.Add("Local manifest validation passed.");
        progress?.Report("Local validation passed.");

        // ── Backend create ──────────────────────────────────────────────────
        steps.Add("Creating backend bundle (POST /api/v1/operator/evidence/bundles).");
        progress?.Report("Creating backend bundle.");

        var createRequest = new EvidenceBundleCreateRequestDto
        {
            EvidenceType        = manifest.EvidenceType,
            Phase               = manifest.Phase,
            Environment         = manifest.Environment,
            TenantId            = manifest.TenantId,
            StoreId             = manifest.StoreId,
            WaveNumber          = manifest.WaveNumber,
            AuditCorrelationId  = null,
            Notes               = null,
        };
        var createOutcome = await _api.CreateAsync(createRequest, ct).ConfigureAwait(false);
        if (!createOutcome.Succeeded || createOutcome.Value is null)
        {
            return BackendBlocked(
                /*bundleUuid*/ null,
                createOutcome.ErrorCode ?? "BACKEND_UNAVAILABLE",
                createOutcome.SafeMessage ?? "Backend create call failed.",
                /*backendStatus*/ null,
                /*backendSha*/ null,
                /*filesUploaded*/ 0,
                /*totalFiles*/ 1 + manifest.Files.Count,
                /*bytesUploaded*/ 0,
                /*totalBytes*/ EstimateTotalBytes(manifest),
                /*currentFile*/ null,
                steps, uploaded, warnings, errors);
        }
        var bundleUuid = createOutcome.Value.Uuid ?? "";
        if (string.IsNullOrWhiteSpace(bundleUuid))
        {
            return BackendBlocked(
                bundleUuid: null,
                "BACKEND_EMPTY_UUID",
                "Backend created the bundle but did not return a UUID.",
                createOutcome.Value.Status,
                /*backendSha*/ null,
                0, 1 + manifest.Files.Count, 0, EstimateTotalBytes(manifest), null,
                steps, uploaded, warnings, errors);
        }
        steps.Add($"Backend bundle created. uuid={bundleUuid}, status={createOutcome.Value.Status}.");

        // ── Upload manifest.json first ─────────────────────────────────────
        steps.Add($"Uploading {ManifestFilename}.");
        progress?.Report($"Uploading {ManifestFilename}.");

        string manifestSha = EvidenceBundleSha256.OfFile(manifestPath);
        long manifestSize = new FileInfo(manifestPath).Length;

        var manifestUpload = await _api.UploadFileAsync(
            bundleUuid,
            relativePath: ManifestFilename,
            absoluteSourcePath: manifestPath,
            redacted: true,
            declaredSha256: manifestSha,
            contentType: "application/json",
            ct).ConfigureAwait(false);
        if (!manifestUpload.Succeeded || manifestUpload.Value is null)
        {
            return BackendBlocked(
                bundleUuid,
                manifestUpload.ErrorCode ?? "MANIFEST_UPLOAD_FAILED",
                manifestUpload.SafeMessage ?? "Backend rejected the manifest upload.",
                createOutcome.Value.Status,
                createOutcome.Value.BundleSha256,
                /*filesUploaded*/ 0,
                /*totalFiles*/ 1 + manifest.Files.Count,
                /*bytesUploaded*/ 0,
                /*totalBytes*/ manifestSize + EstimateTotalBytes(manifest),
                ManifestFilename,
                steps, uploaded, warnings, errors);
        }
        uploaded.Add($"{ManifestFilename} ({manifestSize} B, sha256:{Shorten(manifestSha)})");

        long bytesAccum = manifestSize;
        int  filesUploadedCount = 1; // manifest counts as the first uploaded file

        // ── Upload manifest.files[] in stable ordinal order ────────────────
        var orderedEntries = manifest.Files
            .OrderBy(f => f.Path, StringComparer.Ordinal)
            .ToList();

        foreach (var entry in orderedEntries)
        {
            ct.ThrowIfCancellationRequested();

            // The local validator already verified the file exists and the
            // sha/size match. Re-resolve the absolute path here without
            // trusting the manifest's raw path.
            var safePath = entry.Path.Replace('\\', '/');
            var absolutePath = Path.GetFullPath(Path.Combine(sourceRoot, safePath));
            if (!absolutePath.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"Resolved path is outside the selected folder: {safePath}. Skipped.");
                return BackendBlocked(
                    bundleUuid,
                    "LOCAL_PATH_ESCAPE",
                    "Resolved manifest path escapes the selected folder.",
                    createOutcome.Value.Status,
                    createOutcome.Value.BundleSha256,
                    filesUploadedCount,
                    1 + manifest.Files.Count,
                    bytesAccum,
                    bytesAccum + EstimateTotalBytes(manifest),
                    safePath,
                    steps, uploaded, warnings, errors);
            }

            progress?.Report($"Uploading {safePath}.");
            steps.Add($"Uploading {safePath}.");

            var upload = await _api.UploadFileAsync(
                bundleUuid,
                relativePath: safePath,
                absoluteSourcePath: absolutePath,
                redacted: entry.Redacted,
                declaredSha256: entry.Sha256,
                contentType: null,
                ct).ConfigureAwait(false);

            if (!upload.Succeeded || upload.Value is null)
            {
                return BackendBlocked(
                    bundleUuid,
                    upload.ErrorCode ?? "FILE_UPLOAD_FAILED",
                    upload.SafeMessage ?? "Backend rejected the file upload.",
                    createOutcome.Value.Status,
                    createOutcome.Value.BundleSha256,
                    filesUploadedCount,
                    1 + manifest.Files.Count,
                    bytesAccum,
                    bytesAccum + EstimateTotalBytes(manifest),
                    safePath,
                    steps, uploaded, warnings, errors);
            }
            uploaded.Add($"{safePath} ({entry.SizeBytes} B, sha256:{Shorten(entry.Sha256)})");
            bytesAccum += entry.SizeBytes;
            filesUploadedCount++;
        }

        // ── Finalize ───────────────────────────────────────────────────────
        steps.Add($"Finalizing backend bundle (POST /{bundleUuid}/finalize).");
        progress?.Report("Finalizing backend bundle.");

        var finalize = await _api.FinalizeAsync(
            bundleUuid,
            new EvidenceBundleFinalizeRequestDto { Notes = input.FinalizeNotes },
            ct).ConfigureAwait(false);
        if (!finalize.Succeeded || finalize.Value is null)
        {
            return BackendBlocked(
                bundleUuid,
                finalize.ErrorCode ?? "FINALIZE_FAILED",
                finalize.SafeMessage ?? "Backend rejected the finalize call.",
                createOutcome.Value.Status,
                createOutcome.Value.BundleSha256,
                filesUploadedCount,
                1 + manifest.Files.Count,
                bytesAccum,
                bytesAccum,
                null,
                steps, uploaded, warnings, errors);
        }

        steps.Add($"Finalized. status={finalize.Value.Status}, sha256={Shorten(finalize.Value.BundleSha256)}.");

        return new EvidenceBundleUploadResult(
            Outcome: "Finalized",
            StatusMessage:
                $"Uploaded {filesUploadedCount} file(s) and finalized backend bundle.",
            BundleUuid: bundleUuid,
            BackendBundleStatus: finalize.Value.Status,
            BackendBundleSha256: finalize.Value.BundleSha256,
            FilesUploaded: filesUploadedCount,
            TotalFiles: 1 + manifest.Files.Count,
            BytesUploaded: bytesAccum,
            TotalBytes: bytesAccum,
            CurrentFile: null,
            LastBackendErrorCode: null,
            LastBackendErrorMessage: null,
            UploadSteps: steps,
            UploadedFiles: uploaded,
            Warnings: warnings,
            Errors: errors);
    }

    public async Task<EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>> RefreshBackendStatusAsync(
        string bundleUuid, CancellationToken ct = default)
    {
        if (!IsEnabled())
        {
            return EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>.Failure(
                "FEATURE_FLAG_OFF",
                $"Upload UI is disabled (set {LocalFlagKey}=1 to enable).",
                httpStatus: 0);
        }
        if (string.IsNullOrWhiteSpace(bundleUuid))
        {
            return EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>.Failure(
                "MISSING_BUNDLE_UUID",
                "No bundle UUID to refresh.",
                httpStatus: 0);
        }
        return await _api.GetAsync(bundleUuid, ct).ConfigureAwait(false);
    }

    // ── Local manifest validation ───────────────────────────────────────────

    /// <summary>
    /// Mirrors the backend Phase 10.22D strict-validator gates that the
    /// desktop can check without an HTTP call: schema version, file
    /// matching, sha/size parity, redaction-checklist all-true, no .zip
    /// in files[], manifest-self-reference exclusion, path safety. Adds
    /// errors to <paramref name="errors"/> and returns false on any
    /// failure.
    /// </summary>
    public static bool ValidateManifestLocally(
        EvidenceBundleManifestDto manifest,
        string sourceRoot,
        List<string> errors)
    {
        var initialErrors = errors.Count;

        if (!string.Equals(manifest.SchemaVersion, ExpectedSchemaVersion, StringComparison.Ordinal))
        {
            errors.Add($"manifest.json schemaVersion must be '{ExpectedSchemaVersion}'.");
        }
        if (string.IsNullOrWhiteSpace(manifest.Phase))
            errors.Add("manifest.json phase is required.");
        if (string.IsNullOrWhiteSpace(manifest.EvidenceType))
            errors.Add("manifest.json evidenceType is required.");
        if (string.IsNullOrWhiteSpace(manifest.Environment))
            errors.Add("manifest.json environment is required.");
        if (string.IsNullOrWhiteSpace(manifest.GeneratedAt))
            errors.Add("manifest.json generatedAt is required.");
        if (string.IsNullOrWhiteSpace(manifest.CreatedBy))
            errors.Add("manifest.json createdBy is required.");

        var checklist = manifest.RedactionChecklist;
        if (checklist is null
            || !checklist.AuthorizationHeadersRemoved
            || !checklist.BearerTokensRemoved
            || !checklist.JwtBodiesRemoved
            || !checklist.PasswordsRemoved
            || !checklist.TokensRemoved
            || !checklist.ConfirmationPhrasesRemoved)
        {
            errors.Add(
                "manifest.json redactionChecklist must have all six required keys set to true.");
        }

        if (manifest.Files is null)
        {
            errors.Add("manifest.json files[] is required.");
            return false;
        }

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifest.Files)
        {
            if (string.Equals(entry.Path, ManifestFilename, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("manifest.json must not reference itself in files[].");
                continue;
            }
            // Phase 10.22D backend rejects .zip in any upload. Block it
            // locally so we don't even try.
            var lower = entry.Path?.ToLowerInvariant() ?? "";
            if (lower.EndsWith(".zip"))
            {
                errors.Add($"manifest.files[] contains '.zip' which is not uploadable: {entry.Path}.");
                continue;
            }

            // Path safety (Phase 10.22E mirror of backend Phase 10.22D).
            var safe = EvidenceBundlePathSafety.NormalizeRelativePath(entry.Path);
            if (!safe.Ok)
            {
                errors.Add($"manifest.files[] has unsafe path '{entry.Path}': {safe.SafeMessage}.");
                continue;
            }
            if (!seenPaths.Add(safe.NormalizedPath!))
            {
                errors.Add($"manifest.files[] has duplicate path: {safe.NormalizedPath}.");
                continue;
            }

            var absolutePath = Path.GetFullPath(
                Path.Combine(sourceRoot, safe.NormalizedPath!));
            if (!absolutePath.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"manifest.files[] path resolves outside the selected folder: {entry.Path}.");
                continue;
            }
            if (!File.Exists(absolutePath))
            {
                errors.Add($"manifest.files[] references missing local file: {entry.Path}.");
                continue;
            }

            long actualSize = new FileInfo(absolutePath).Length;
            if (actualSize != entry.SizeBytes)
            {
                errors.Add(
                    $"Local size for {entry.Path} ({actualSize}) does not match manifest sizeBytes ({entry.SizeBytes}).");
                continue;
            }
            string actualSha = EvidenceBundleSha256.OfFile(absolutePath);
            if (!string.Equals(actualSha, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"Local SHA-256 for {entry.Path} does not match manifest sha256.");
                continue;
            }
            if (!entry.Redacted)
            {
                errors.Add(
                    $"manifest.files[] entry {entry.Path} has redacted=false.");
            }
        }

        return errors.Count == initialErrors;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static long EstimateTotalBytes(EvidenceBundleManifestDto manifest)
    {
        long sum = 0;
        if (manifest.Files != null)
        {
            foreach (var f in manifest.Files) sum += System.Math.Max(0, f.SizeBytes);
        }
        return sum;
    }

    private static string Shorten(string? sha)
        => string.IsNullOrEmpty(sha) || sha.Length <= 16 ? (sha ?? "") : sha[..8] + "…" + sha[^4..];

    private static EvidenceBundleUploadResult Disabled() =>
        new(
            Outcome: "Disabled",
            StatusMessage: $"Upload UI is disabled (set {LocalFlagKey}=1 to enable).",
            BundleUuid: null,
            BackendBundleStatus: null,
            BackendBundleSha256: null,
            FilesUploaded: 0,
            TotalFiles: 0,
            BytesUploaded: 0,
            TotalBytes: 0,
            CurrentFile: null,
            LastBackendErrorCode: null,
            LastBackendErrorMessage: null,
            UploadSteps: Array.Empty<string>(),
            UploadedFiles: Array.Empty<string>(),
            Warnings: Array.Empty<string>(),
            Errors: Array.Empty<string>());

    private static EvidenceBundleUploadResult LocalBlocked(
        string statusMessage,
        List<string> steps,
        List<string> uploaded,
        List<string> warnings,
        List<string> errors) =>
        new(
            Outcome: "LocalBlocked",
            StatusMessage: statusMessage,
            BundleUuid: null,
            BackendBundleStatus: null,
            BackendBundleSha256: null,
            FilesUploaded: 0,
            TotalFiles: 0,
            BytesUploaded: 0,
            TotalBytes: 0,
            CurrentFile: null,
            LastBackendErrorCode: null,
            LastBackendErrorMessage: null,
            UploadSteps: steps,
            UploadedFiles: uploaded,
            Warnings: warnings,
            Errors: errors);

    private static EvidenceBundleUploadResult BackendBlocked(
        string? bundleUuid,
        string errorCode,
        string errorMessage,
        string? backendStatus,
        string? backendSha,
        int filesUploaded,
        int totalFiles,
        long bytesUploaded,
        long totalBytes,
        string? currentFile,
        List<string> steps,
        List<string> uploaded,
        List<string> warnings,
        List<string> errors)
    {
        errors.Add($"[{errorCode}] {errorMessage}");
        return new EvidenceBundleUploadResult(
            Outcome: "BackendBlocked",
            StatusMessage: $"Backend rejected: {errorCode}. {errorMessage}",
            BundleUuid: bundleUuid,
            BackendBundleStatus: backendStatus,
            BackendBundleSha256: backendSha,
            FilesUploaded: filesUploaded,
            TotalFiles: totalFiles,
            BytesUploaded: bytesUploaded,
            TotalBytes: totalBytes,
            CurrentFile: currentFile,
            LastBackendErrorCode: errorCode,
            LastBackendErrorMessage: errorMessage,
            UploadSteps: steps,
            UploadedFiles: uploaded,
            Warnings: warnings,
            Errors: errors);
    }
}
