using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PosSystem.Core.DTOs;

// Phase 10.22P — desktop DTOs matching the backend Phase 10.22N/O
// retention-sweeper and expiration-sweeper run history response shapes.
// No storage key, bucket, endpoint, confirmation phrase, or raw path
// is ever present in these records.

// ── Retention sweeper (Phase 10.22N) ────────────────────────────────────────

public sealed class RetentionSweepRunRequestDto
{
    [JsonPropertyName("dryRun")]     public bool?   DryRun     { get; set; }
    [JsonPropertyName("batchLimit")] public int?    BatchLimit { get; set; }
    [JsonPropertyName("reason")]     public string? Reason     { get; set; }
    [JsonPropertyName("ticketId")]   public string? TicketId   { get; set; }
}

public sealed class RetentionSweepRunResponseDto
{
    [JsonPropertyName("runUuid")]          public string?   RunUuid          { get; init; }
    [JsonPropertyName("triggerType")]      public string?   TriggerType      { get; init; }
    [JsonPropertyName("startedAt")]        public DateTime? StartedAt        { get; init; }
    [JsonPropertyName("finishedAt")]       public DateTime? FinishedAt       { get; init; }
    [JsonPropertyName("status")]           public string?   Status           { get; init; }
    [JsonPropertyName("dryRun")]           public bool      DryRun           { get; init; }
    [JsonPropertyName("batchLimit")]       public int       BatchLimit       { get; init; }
    [JsonPropertyName("candidateCount")]   public int?      CandidateCount   { get; init; }
    [JsonPropertyName("archivedCount")]    public int?      ArchivedCount    { get; init; }
    [JsonPropertyName("skippedCount")]     public int?      SkippedCount     { get; init; }
    [JsonPropertyName("failedCount")]      public int?      FailedCount      { get; init; }
    [JsonPropertyName("safeErrorCode")]    public string?   SafeErrorCode    { get; init; }
    [JsonPropertyName("safeErrorMessage")] public string?   SafeErrorMessage { get; init; }
    [JsonPropertyName("createdBy")]        public string?   CreatedBy        { get; init; }
}

public sealed class RetentionSweepRunPageResponseDto
{
    [JsonPropertyName("content")]       public List<RetentionSweepRunResponseDto> Content       { get; init; } = new();
    [JsonPropertyName("totalElements")] public long TotalElements { get; init; }
    [JsonPropertyName("totalPages")]    public int  TotalPages    { get; init; }
    [JsonPropertyName("page")]          public int  Page          { get; init; }
    [JsonPropertyName("size")]          public int  Size          { get; init; }
}

// ── Expiration sweeper (Phase 10.22O) ────────────────────────────────────────

public sealed class ExpirationSweepRunRequestDto
{
    [JsonPropertyName("dryRun")]     public bool?   DryRun     { get; set; }
    [JsonPropertyName("batchLimit")] public int?    BatchLimit { get; set; }
    [JsonPropertyName("reason")]     public string? Reason     { get; set; }
    [JsonPropertyName("ticketId")]   public string? TicketId   { get; set; }
}

public sealed class ExpirationSweepRunResponseDto
{
    [JsonPropertyName("runUuid")]          public string?   RunUuid          { get; init; }
    [JsonPropertyName("triggerType")]      public string?   TriggerType      { get; init; }
    [JsonPropertyName("startedAt")]        public DateTime? StartedAt        { get; init; }
    [JsonPropertyName("finishedAt")]       public DateTime? FinishedAt       { get; init; }
    [JsonPropertyName("status")]           public string?   Status           { get; init; }
    [JsonPropertyName("dryRun")]           public bool      DryRun           { get; init; }
    [JsonPropertyName("batchLimit")]       public int       BatchLimit       { get; init; }
    [JsonPropertyName("candidateCount")]   public int?      CandidateCount   { get; init; }
    [JsonPropertyName("expiredCount")]     public int?      ExpiredCount     { get; init; }
    [JsonPropertyName("skippedCount")]     public int?      SkippedCount     { get; init; }
    [JsonPropertyName("failedCount")]      public int?      FailedCount      { get; init; }
    [JsonPropertyName("safeErrorCode")]    public string?   SafeErrorCode    { get; init; }
    [JsonPropertyName("safeErrorMessage")] public string?   SafeErrorMessage { get; init; }
    [JsonPropertyName("createdBy")]        public string?   CreatedBy        { get; init; }
}

public sealed class ExpirationSweepRunPageResponseDto
{
    [JsonPropertyName("content")]       public List<ExpirationSweepRunResponseDto> Content       { get; init; } = new();
    [JsonPropertyName("totalElements")] public long TotalElements { get; init; }
    [JsonPropertyName("totalPages")]    public int  TotalPages    { get; init; }
    [JsonPropertyName("page")]          public int  Page          { get; init; }
    [JsonPropertyName("size")]          public int  Size          { get; init; }
}
