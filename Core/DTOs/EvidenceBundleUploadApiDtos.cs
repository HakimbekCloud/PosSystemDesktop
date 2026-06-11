using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PosSystem.Core.DTOs;

// Phase 10.22F desktop-side DTOs for the backend evidence bundle
// upload API (Ham-Pos Phase 10.22C/D). camelCase via JsonPropertyName
// to match the backend's record / record-component names exactly.
//
// Safety contract (mirrors backend Phase 10.22C/D):
//   • No ConfirmationPhrase / Token / Password / RawPath field on any DTO.
//   • The desktop wrapper scrubs every backend-supplied message via
//     OperatorPermissionAdminRedaction.ScrubAndTruncate before display.
//   • No DTO carries an absolute filesystem path — the desktop never
//     sends one, and the backend never returns one.

// ── Create bundle ───────────────────────────────────────────────────────────

public sealed class EvidenceBundleCreateRequestDto
{
    [JsonPropertyName("evidenceType")]   public string  EvidenceType   { get; init; } = "";
    [JsonPropertyName("phase")]          public string  Phase          { get; init; } = "";
    [JsonPropertyName("environment")]    public string  Environment    { get; init; } = "";
    [JsonPropertyName("tenantId")]       public string? TenantId       { get; init; }
    [JsonPropertyName("storeId")]        public string? StoreId        { get; init; }
    [JsonPropertyName("waveNumber")]     public int?    WaveNumber     { get; init; }
    [JsonPropertyName("pilotWindowStart")]   public string? PilotWindowStart   { get; init; }
    [JsonPropertyName("pilotWindowEnd")]     public string? PilotWindowEnd     { get; init; }
    [JsonPropertyName("auditCorrelationId")] public string? AuditCorrelationId { get; init; }
    [JsonPropertyName("notes")]          public string? Notes          { get; init; }
}

// ── Bundle metadata / file response ─────────────────────────────────────────

public sealed class EvidenceBundleResponseDto
{
    [JsonPropertyName("uuid")]              public string?  Uuid              { get; init; }
    [JsonPropertyName("status")]            public string?  Status            { get; init; }
    [JsonPropertyName("evidenceType")]      public string?  EvidenceType      { get; init; }
    [JsonPropertyName("phase")]             public string?  Phase             { get; init; }
    [JsonPropertyName("environment")]       public string?  Environment       { get; init; }
    [JsonPropertyName("tenantId")]          public string?  TenantId          { get; init; }
    [JsonPropertyName("storeId")]           public string?  StoreId           { get; init; }
    [JsonPropertyName("retentionClass")]    public string?  RetentionClass    { get; init; }
    [JsonPropertyName("fileCount")]         public int      FileCount         { get; init; }
    [JsonPropertyName("totalBytes")]        public long     TotalBytes        { get; init; }
    [JsonPropertyName("bundleSha256")]      public string?  BundleSha256      { get; init; }
    [JsonPropertyName("createdBy")]         public long?    CreatedBy         { get; init; }
    [JsonPropertyName("createdByUsername")] public string?  CreatedByUsername { get; init; }
    [JsonPropertyName("createdAt")]         public string?  CreatedAt         { get; init; }
    [JsonPropertyName("finalizedAt")]       public string?  FinalizedAt       { get; init; }
    // Phase 10.22H additive fields. Old desktop builds ignore them.
    [JsonPropertyName("retentionUntil")]    public string?  RetentionUntil    { get; init; }
    [JsonPropertyName("legalHold")]         public bool     LegalHold         { get; init; }
    [JsonPropertyName("reviewedBy")]        public long?    ReviewedBy        { get; init; }
    [JsonPropertyName("reviewedAt")]        public string?  ReviewedAt        { get; init; }
    [JsonPropertyName("files")]             public List<EvidenceBundleFileResponseDto>? Files { get; init; }
}

public sealed class EvidenceBundleFileResponseDto
{
    [JsonPropertyName("relativePath")]  public string?  RelativePath  { get; init; }
    [JsonPropertyName("fileSizeBytes")] public long     FileSizeBytes { get; init; }
    [JsonPropertyName("sha256Hex")]     public string?  Sha256Hex     { get; init; }
    [JsonPropertyName("contentType")]   public string?  ContentType   { get; init; }
    [JsonPropertyName("redacted")]      public bool     Redacted      { get; init; }
    [JsonPropertyName("uploadedAt")]    public string?  UploadedAt    { get; init; }
}

// ── Upload response ─────────────────────────────────────────────────────────

public sealed class EvidenceBundleUploadResponseDto
{
    [JsonPropertyName("bundleUuid")]       public string? BundleUuid       { get; init; }
    [JsonPropertyName("relativePath")]     public string? RelativePath     { get; init; }
    [JsonPropertyName("fileSizeBytes")]    public long    FileSizeBytes    { get; init; }
    [JsonPropertyName("sha256Hex")]        public string? Sha256Hex        { get; init; }
    [JsonPropertyName("redacted")]         public bool    Redacted         { get; init; }
    [JsonPropertyName("bundleFileCount")]  public int     BundleFileCount  { get; init; }
    [JsonPropertyName("bundleTotalBytes")] public long    BundleTotalBytes { get; init; }
    [JsonPropertyName("bundleStatus")]     public string? BundleStatus     { get; init; }
}

// ── Finalize request ────────────────────────────────────────────────────────

public sealed class EvidenceBundleFinalizeRequestDto
{
    [JsonPropertyName("notes")] public string? Notes { get; init; }
}

// ── Phase 10.22G — review request ───────────────────────────────────────────
//
// Reviewer decision body for POST /{uuid}/review. `Decision` must be
// one of APPROVED / REJECTED / NEEDS_CHANGES (backend rejects others
// with VALIDATION_FAILED). `ReviewNotes` is scrubbed server-side
// before appending to the bundle's reason column + audit metadata.
public sealed class EvidenceBundleReviewRequestDto
{
    [JsonPropertyName("decision")]    public string  Decision    { get; init; } = "";
    [JsonPropertyName("reviewNotes")] public string? ReviewNotes { get; init; }
}

// ── List / page response ────────────────────────────────────────────────────

public sealed class EvidenceBundlePageResponseDto
{
    [JsonPropertyName("items")]         public List<EvidenceBundlePageItemDto>? Items { get; init; }
    [JsonPropertyName("page")]          public int  Page          { get; init; }
    [JsonPropertyName("size")]          public int  Size          { get; init; }
    [JsonPropertyName("totalElements")] public long TotalElements { get; init; }
}

public sealed class EvidenceBundlePageItemDto
{
    [JsonPropertyName("uuid")]         public string? Uuid         { get; init; }
    [JsonPropertyName("status")]       public string? Status       { get; init; }
    [JsonPropertyName("evidenceType")] public string? EvidenceType { get; init; }
    [JsonPropertyName("phase")]        public string? Phase        { get; init; }
    [JsonPropertyName("environment")]  public string? Environment  { get; init; }
    [JsonPropertyName("tenantId")]     public string? TenantId     { get; init; }
    [JsonPropertyName("storeId")]      public string? StoreId      { get; init; }
    [JsonPropertyName("fileCount")]    public int     FileCount    { get; init; }
    [JsonPropertyName("totalBytes")]   public long    TotalBytes   { get; init; }
    [JsonPropertyName("createdAt")]    public string? CreatedAt    { get; init; }
    [JsonPropertyName("finalizedAt")]  public string? FinalizedAt  { get; init; }
    // Phase 10.22H additive fields.
    [JsonPropertyName("retentionUntil")] public string? RetentionUntil { get; init; }
    [JsonPropertyName("legalHold")]      public bool   LegalHold      { get; init; }
}

// ── Backend error body ──────────────────────────────────────────────────────

// Backend Phase 10.22C controller ExceptionHandler returns
//   { "code": "<ENUM_NAME>", "message": "<sanitized text>" }
// for every EvidenceBundleApiException. The desktop deserializes this
// shape into EvidenceBundleApiErrorDto to surface the stable error
// code in the upload-result UI.
public sealed class EvidenceBundleApiErrorDto
{
    [JsonPropertyName("code")]    public string? Code    { get; init; }
    [JsonPropertyName("message")] public string? Message { get; init; }
}

// ── Phase 10.22H — retention / legal hold / archive / expire requests ──────
//
// Three positional fields shared by all four mutation requests below:
//   • RetentionUntil — ISO-8601 UTC string (e.g. "2029-05-30T00:00:00Z").
//     Required by the retention update; ignored by the other endpoints.
//   • Reason         — operator-supplied free-form context (scrubbed +
//                      truncated server-side).
//   • TicketId       — change-management / legal ticket ID
//                      (scrubbed + truncated server-side).
//
// No DTO carries a confirmation phrase, token, password, or absolute
// path. The desktop NEVER inspects or persists ticket / reason content
// after the call returns.

public sealed class EvidenceBundleRetentionRequestDto
{
    [JsonPropertyName("retentionUntil")] public string RetentionUntil { get; init; } = "";
    [JsonPropertyName("reason")]         public string Reason         { get; init; } = "";
    [JsonPropertyName("ticketId")]       public string TicketId       { get; init; } = "";
}

public sealed class EvidenceBundleLegalHoldRequestDto
{
    [JsonPropertyName("legalHold")] public bool   LegalHold { get; init; }
    [JsonPropertyName("reason")]    public string Reason    { get; init; } = "";
    [JsonPropertyName("ticketId")]  public string TicketId  { get; init; } = "";
}

public sealed class EvidenceBundleArchiveRequestDto
{
    [JsonPropertyName("reason")]   public string Reason   { get; init; } = "";
    [JsonPropertyName("ticketId")] public string TicketId { get; init; } = "";
}

public sealed class EvidenceBundleExpireRequestDto
{
    [JsonPropertyName("reason")]   public string Reason   { get; init; } = "";
    [JsonPropertyName("ticketId")] public string TicketId { get; init; } = "";
}

// ── Phase 10.22G — download result ──────────────────────────────────────────
//
// Returned by ApiClient.DownloadEvidenceBundleAsync. Carries only safe
// fields — the absolute destination path appears so the desktop UI can
// surface a redacted/truncated form to the operator. The card never
// echoes the absolute path raw.
public sealed class EvidenceBundleDownloadResultDto
{
    public string BundleUuid          { get; init; } = "";
    public string DestinationPath     { get; init; } = "";
    public string DestinationFilename { get; init; } = "";
    public long   ByteSize            { get; init; }
    public string Sha256Hex           { get; init; } = "";
}

// ── Generic call outcome wrapper ────────────────────────────────────────────

// Phase 10.22F desktop result wrapper. Every backend evidence bundle
// API call returns one of these so the upload service can branch on
// {Succeeded, ErrorCode, SafeMessage, HttpStatus} without throwing.
//
// Success path: Value is non-null, ErrorCode is null.
// Backend error: Value is null, ErrorCode is the stable backend code
//   (e.g. "FEATURE_FLAG_OFF", "REDACTION_FAILED"); SafeMessage is the
//   sanitized backend message.
// Network / transport failure: Value is null, ErrorCode is
//   "NETWORK_FAILURE" or "DESERIALIZATION_FAILURE"; SafeMessage is a
//   short safe summary.
public sealed class EvidenceBundleApiCallOutcome<T> where T : class
{
    public bool    Succeeded   { get; init; }
    public T?      Value       { get; init; }
    public string? ErrorCode   { get; init; }
    public string? SafeMessage { get; init; }
    public int     HttpStatus  { get; init; }

    public static EvidenceBundleApiCallOutcome<T> Success(T value, int httpStatus) =>
        new() { Succeeded = true, Value = value, HttpStatus = httpStatus };

    public static EvidenceBundleApiCallOutcome<T> Failure(
        string code, string safeMessage, int httpStatus) =>
        new()
        {
            Succeeded   = false,
            ErrorCode   = code,
            SafeMessage = safeMessage,
            HttpStatus  = httpStatus,
        };
}
