using System.Collections.Generic;

namespace PosSystem.Services.EvidenceBundleUpload;

// Phase 10.22F — outcome of one
// <see cref="EvidenceBundleUploadService"/> invocation.
//
// `Outcome` values:
//   • Disabled            — local upload flag is OFF.
//   • LocalValidationOnly — local checks passed; no backend call made.
//   • LocalBlocked        — local validation rejected (manifest invalid,
//                           sha/size mismatch, .zip in manifest.files, etc.).
//   • BackendBlocked      — backend create / upload / finalize returned
//                           a typed error (FEATURE_FLAG_OFF / FORBIDDEN /
//                           REDACTION_FAILED / MIME_MISMATCH /
//                           DUPLICATE_FILE / MANIFEST_INVALID / …).
//                           `BundleUuid` may be populated if create
//                           succeeded but a later step failed.
//   • Finalized           — bundle status = FINALIZED on the backend.
//
// Carries only safe display fields; never the operator's token,
// the file content, the absolute path, or the multipart body.
public sealed record EvidenceBundleUploadResult(
    string Outcome,
    string StatusMessage,
    string? BundleUuid,
    string? BackendBundleStatus,
    string? BackendBundleSha256,
    int FilesUploaded,
    int TotalFiles,
    long BytesUploaded,
    long TotalBytes,
    string? CurrentFile,
    string? LastBackendErrorCode,
    string? LastBackendErrorMessage,
    IReadOnlyList<string> UploadSteps,
    IReadOnlyList<string> UploadedFiles,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);
