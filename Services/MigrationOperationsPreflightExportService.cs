using System.IO;
using System.Text.Json;
using PosSystem.Data.Repositories;

namespace PosSystem.Services;

// ── Options / Result DTOs (Phase 10.10C) ─────────────────────────────────────

public sealed class MigrationOperationsPreflightExportOptions
{
    // Tenant to scope the readiness checks at. Null/blank → falls back to
    // global_settings.json[last_tenant_subdomain].
    public string? TenantSubdomain    { get; init; }

    // Override output directory. Null → %LocalAppData%\PosSystem\logs\preflight\
    public string? OutputDirectory    { get; init; }

    public bool   IncludeMachineInfo  { get; init; } = true;
}

public sealed class MigrationOperationsPreflightExportResult
{
    public bool   Success           { get; init; }
    public string? FilePath         { get; init; }
    public System.DateTime StartedAtUtc   { get; init; }
    public System.DateTime CompletedAtUtc { get; init; }

    // Always true on a successful export. The preflight service unconditionally
    // pipes its JSON through MigrationAuditLogger.RedactSecrets plus a defense-
    // in-depth literal scrub of the rollback confirmation phrases.
    public bool   RedactionApplied  { get; init; }

    public System.Collections.Generic.List<string> Warnings { get; init; } = new();
    public System.Collections.Generic.List<string> Errors   { get; init; } = new();
}

// ── Payload DTO (serialized to JSON) ─────────────────────────────────────────

public sealed class MigrationOperationsPreflightPayload
{
    public System.DateTime GeneratedAtUtc        { get; init; } = System.DateTime.UtcNow;
    public string?        TenantSubdomain        { get; init; }

    public string         OverallStatus          { get; init; } = "Failed";
    public bool           CanConsiderRealMigration { get; init; }
    public bool           CanConsiderRealRollback  { get; init; }

    public System.Collections.Generic.List<string> BlockingReasons
        { get; init; } = new();
    public System.Collections.Generic.List<string> Warnings
        { get; init; } = new();

    public OperatorDiagnosticsReport?         Diagnostics            { get; init; }
    public TenantMigrationDryRunReport?       MigrationAudit         { get; init; }
    public MigrationVerificationReport?       MigrationVerification  { get; init; }
    public TenantCutoverReadinessReport?      CutoverReadiness       { get; init; }
    public TenantDbRollbackReadinessReport?   RollbackReadiness      { get; init; }
    public MigrationDryRunPreviewReport?      MigrationDryRun        { get; init; }
    public RollbackDryRunPreviewReport?       RollbackDryRun         { get; init; }

    public object?        MachineInfo            { get; init; }
}

// ── Service ──────────────────────────────────────────────────────────────────

// Aggregates every operator-relevant read-only / preview-only signal into a
// single redacted JSON report. Intended as the artifact an operator/support
// engineer attaches to a change-management ticket before any real migration
// or real rollback is ever considered.
//
// What this service does:
//   • Calls only read-only / preview-only services.
//   • Pipes the serialized payload through MigrationAuditLogger.RedactSecrets
//     (multi-pass) plus a literal-string scrub of the rollback confirmation
//     phrases (defense-in-depth — these strings are not structurally present
//     in any source DTO, but a future field could leak them).
//   • Writes one file under %LocalAppData%\PosSystem\logs\preflight\ via
//     temp-file + atomic rename. Filename collisions append "-1", "-2", …
//
// What this service NEVER does:
//   • Execute real migration (SharedToTenantDatabaseMigrator is not injected).
//   • Execute real rollback (TenantDbRollbackExecutor is not injected).
//   • Switch the path provider (TenantScopeService is not injected).
//   • Mutate global_settings.json, the active DB, or any tenant DB.
//   • Delete files or rename directories.
//   • Pass Force / DryRunOnly=false / ConfirmationPhrase to any underlying
//     executor — those executors are not reachable from this service.
//   • Serialize raw tokens, JWTs, DPAPI blobs, or the rollback confirmation
//     phrase — these are either structurally absent from the payload DTOs,
//     scrubbed by RedactSecrets, or scrubbed by the literal-phrase pass.
public sealed class MigrationOperationsPreflightExportService
{
    private const string DefaultSubdirectory = "preflight";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly OperatorDiagnosticsService         _diagnostics;
    private readonly SharedToTenantMigrationAuditor     _auditor;
    private readonly SharedToTenantMigrationVerifier    _verifier;
    private readonly TenantCutoverReadinessGate         _cutoverGate;
    private readonly TenantDbRollbackReadinessChecker   _rollbackChecker;
    private readonly MigrationDryRunPreviewService      _migrationPreview;
    private readonly RollbackDryRunPreviewService       _rollbackPreview;
    private readonly GlobalSettingsRepository           _global;

    public MigrationOperationsPreflightExportService(
        OperatorDiagnosticsService diagnostics,
        SharedToTenantMigrationAuditor auditor,
        SharedToTenantMigrationVerifier verifier,
        TenantCutoverReadinessGate cutoverGate,
        TenantDbRollbackReadinessChecker rollbackChecker,
        MigrationDryRunPreviewService migrationPreview,
        RollbackDryRunPreviewService rollbackPreview,
        GlobalSettingsRepository global)
    {
        _diagnostics      = diagnostics;
        _auditor          = auditor;
        _verifier         = verifier;
        _cutoverGate      = cutoverGate;
        _rollbackChecker  = rollbackChecker;
        _migrationPreview = migrationPreview;
        _rollbackPreview  = rollbackPreview;
        _global           = global;
    }

    public async System.Threading.Tasks.Task<MigrationOperationsPreflightExportResult> ExportAsync(
        MigrationOperationsPreflightExportOptions options,
        System.Threading.CancellationToken ct = default)
    {
        if (options is null) throw new System.ArgumentNullException(nameof(options));

        var started         = System.DateTime.UtcNow;
        var warnings        = new System.Collections.Generic.List<string>();
        var errors          = new System.Collections.Generic.List<string>();
        var blockingReasons = new System.Collections.Generic.List<string>();

        try
        {
            // Resolve tenant: explicit option → last-used → null.
            var tenantInput = string.IsNullOrWhiteSpace(options.TenantSubdomain)
                ? _global.Get("last_tenant_subdomain")
                : options.TenantSubdomain!.Trim();

            // ── 1. Collect every subsection best-effort. ────────────────────
            OperatorDiagnosticsReport? diagnostics = null;
            try
            {
                diagnostics = await _diagnostics.GetReportAsync(tenantInput, ct);
            }
            catch (System.Exception ex)
            {
                errors.Add($"Diagnostics: {ex.Message}");
                blockingReasons.Add("Diagnostics subsystem failed.");
            }

            TenantMigrationDryRunReport? audit = null;
            try
            {
                audit = await _auditor.AnalyzeAsync(ct);
            }
            catch (System.Exception ex)
            {
                errors.Add($"Migration audit: {ex.Message}");
                blockingReasons.Add("Migration audit failed.");
            }

            MigrationVerificationReport? verification = null;
            try
            {
                verification = await _verifier.VerifyAsync(ct);
            }
            catch (System.Exception ex)
            {
                errors.Add($"Migration verification: {ex.Message}");
                blockingReasons.Add("Migration verification failed.");
            }

            TenantCutoverReadinessReport? cutover = null;
            if (!string.IsNullOrWhiteSpace(tenantInput))
            {
                try
                {
                    cutover = await _cutoverGate.CheckAsync(tenantInput, ct);
                }
                catch (System.Exception ex)
                {
                    errors.Add($"Cutover readiness: {ex.Message}");
                    blockingReasons.Add("Cutover readiness check failed.");
                }
            }
            else
            {
                warnings.Add("No tenant subdomain provided — cutover readiness not evaluated.");
            }

            TenantDbRollbackReadinessReport? rollbackReadiness = null;
            try
            {
                rollbackReadiness = _rollbackChecker.Check();
            }
            catch (System.Exception ex)
            {
                errors.Add($"Rollback readiness: {ex.Message}");
                blockingReasons.Add("Rollback readiness check failed.");
            }

            MigrationDryRunPreviewReport? migrationDryRun = null;
            try
            {
                migrationDryRun = await _migrationPreview.PreviewAsync(ct);
            }
            catch (System.Exception ex)
            {
                errors.Add($"Migration dry-run preview: {ex.Message}");
                blockingReasons.Add("Migration dry-run preview failed to execute.");
            }

            RollbackDryRunPreviewReport? rollbackDryRun = null;
            try
            {
                rollbackDryRun = await _rollbackPreview.PreviewAsync(ct);
            }
            catch (System.Exception ex)
            {
                errors.Add($"Rollback dry-run preview: {ex.Message}");
                blockingReasons.Add("Rollback dry-run preview failed to execute.");
            }

            // ── 2. Derive blocking conditions and warnings. ─────────────────
            EvaluateBlockers(
                diagnostics, cutover, rollbackReadiness,
                verification, migrationDryRun, rollbackDryRun,
                blockingReasons, warnings);

            // ── 3. Go/no-go classification. Informational only. ─────────────
            var overallStatus = blockingReasons.Count > 0
                ? "Failed"
                : (warnings.Count > 0 ? "PassedWithWarnings" : "Passed");

            var canConsiderRealMigration =
                blockingReasons.Count == 0 &&
                cutover is not null &&
                cutover.Status != TenantDbCutoverReadinessStatus.Disabled &&
                cutover.Status != TenantDbCutoverReadinessStatus.Blocked &&
                migrationDryRun is not null &&
                migrationDryRun.IsAvailable &&
                migrationDryRun.SideEffectCheckPassed;

            var canConsiderRealRollback =
                blockingReasons.Count == 0 &&
                rollbackReadiness is not null &&
                rollbackReadiness.Status != TenantDbRollbackReadinessStatus.Blocked &&
                rollbackDryRun is not null &&
                rollbackDryRun.IsAvailable &&
                rollbackDryRun.SideEffectCheckPassed;

            // ── 4. Build payload. ───────────────────────────────────────────
            object? machineInfo = options.IncludeMachineInfo
                ? new
                {
                    machineName          = System.Environment.MachineName,
                    osVersion            = System.Environment.OSVersion.VersionString,
                    userName             = System.Environment.UserName,
                    appBaseDirectory     = System.AppContext.BaseDirectory,
                    processId            = System.Environment.ProcessId,
                    exportGeneratedAtUtc = System.DateTime.UtcNow,
                }
                : null;

            if (options.IncludeMachineInfo)
                warnings.Add("Machine/user info is included in this preflight export.");

            var payload = new MigrationOperationsPreflightPayload
            {
                GeneratedAtUtc           = System.DateTime.UtcNow,
                TenantSubdomain          = tenantInput,
                OverallStatus            = overallStatus,
                CanConsiderRealMigration = canConsiderRealMigration,
                CanConsiderRealRollback  = canConsiderRealRollback,
                BlockingReasons          = new System.Collections.Generic.List<string>(blockingReasons),
                Warnings                 = new System.Collections.Generic.List<string>(warnings),
                Diagnostics              = diagnostics,
                MigrationAudit           = audit,
                MigrationVerification    = verification,
                CutoverReadiness         = cutover,
                RollbackReadiness        = rollbackReadiness,
                MigrationDryRun          = migrationDryRun,
                RollbackDryRun           = rollbackDryRun,
                MachineInfo              = machineInfo,
            };

            // ── 5. Serialize + redact + scrub. ──────────────────────────────
            var rawJson  = JsonSerializer.Serialize(payload, JsonOptions);
            var safeJson = MigrationAuditLogger.RedactSecrets(rawJson);
            safeJson     = ScrubConfirmationPhrases(safeJson);

            // ── 6. Resolve output dir + filename + write atomically. ────────
            var outputDir = string.IsNullOrWhiteSpace(options.OutputDirectory)
                ? Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    "PosSystem", "logs", DefaultSubdirectory)
                : options.OutputDirectory!;

            try
            {
                Directory.CreateDirectory(outputDir);
            }
            catch (System.Exception ioEx)
            {
                errors.Add($"Failed to create output directory: {ioEx.Message}");
                return Failure(started, warnings, errors);
            }

            var label    = SanitizeForFileName(tenantInput) ?? "global";
            var stamp    = started.ToString("yyyyMMdd-HHmmss");
            var basePath = Path.Combine(outputDir, $"migration-preflight-{stamp}-{label}.json");
            var finalPath = ResolveNonCollidingPath(basePath);

            try
            {
                var tmp = finalPath + ".tmp";
                File.WriteAllText(tmp, safeJson);
                if (File.Exists(finalPath))
                    File.Replace(tmp, finalPath, destinationBackupFileName: null);
                else
                    File.Move(tmp, finalPath);
            }
            catch (System.Exception ioEx)
            {
                errors.Add($"Failed to write preflight file: {ioEx.Message}");
                return Failure(started, warnings, errors);
            }

            return new MigrationOperationsPreflightExportResult
            {
                Success          = true,
                FilePath         = finalPath,
                StartedAtUtc     = started,
                CompletedAtUtc   = System.DateTime.UtcNow,
                RedactionApplied = true,
                Warnings         = warnings,
                Errors           = errors,
            };
        }
        catch (System.Exception ex)
        {
            errors.Add($"Preflight export failed: {ex.Message}");
            return Failure(started, warnings, errors);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static MigrationOperationsPreflightExportResult Failure(
        System.DateTime started,
        System.Collections.Generic.List<string> warnings,
        System.Collections.Generic.List<string> errors)
        => new()
        {
            Success          = false,
            FilePath         = null,
            StartedAtUtc     = started,
            CompletedAtUtc   = System.DateTime.UtcNow,
            RedactionApplied = false,
            Warnings         = warnings,
            Errors           = errors,
        };

    // Derives blocking reasons + warnings from collected subsections. Each
    // condition is conservative: any concern that real migration/rollback
    // could be unsafe lands in BlockingReasons. Cosmetic/contextual issues
    // land in Warnings.
    private static void EvaluateBlockers(
        OperatorDiagnosticsReport? diagnostics,
        TenantCutoverReadinessReport? cutover,
        TenantDbRollbackReadinessReport? rollbackReadiness,
        MigrationVerificationReport? verification,
        MigrationDryRunPreviewReport? migrationDryRun,
        RollbackDryRunPreviewReport? rollbackDryRun,
        System.Collections.Generic.List<string> blockers,
        System.Collections.Generic.List<string> warnings)
    {
        if (diagnostics is null)
            blockers.Add("Diagnostics report unavailable.");

        if (cutover is not null && cutover.Status == TenantDbCutoverReadinessStatus.Blocked)
            blockers.Add("Cutover readiness is Blocked.");
        if (cutover is not null && cutover.Status == TenantDbCutoverReadinessStatus.AllowedWithWarnings)
            warnings.Add("Cutover readiness is AllowedWithWarnings.");

        if (rollbackReadiness is not null &&
            rollbackReadiness.Status == TenantDbRollbackReadinessStatus.Blocked)
            blockers.Add("Rollback readiness is Blocked.");
        if (rollbackReadiness is not null &&
            rollbackReadiness.Status == TenantDbRollbackReadinessStatus.ReadyWithWarnings)
            warnings.Add("Rollback readiness is ReadyWithWarnings.");

        if (diagnostics is not null)
        {
            if (diagnostics.Sales.PendingSalesCount > 0)
                blockers.Add($"{diagnostics.Sales.PendingSalesCount} pending sales exist.");
            if (diagnostics.Sales.PoisonSalesCount > 0)
                blockers.Add($"{diagnostics.Sales.PoisonSalesCount} poison sales exist.");

            foreach (var w in diagnostics.Warnings) warnings.Add($"Diagnostics: {w}");
            foreach (var e in diagnostics.Errors)
                blockers.Add($"Diagnostics error: {e}");
        }

        // Verification is meaningful only after a real migration has run
        // (global marker present). Pre-migration the verifier returns
        // AllVerified=false trivially.
        if (verification is not null &&
            verification.SourceDbExists &&
            verification.GlobalMarkerPresent &&
            !verification.AllVerified)
        {
            blockers.Add("Migration verification did not pass (AllVerified=false post-migration).");
        }

        if (migrationDryRun is not null)
        {
            if (!migrationDryRun.IsAvailable || migrationDryRun.Outcome == "Failed")
                blockers.Add("Migration dry-run preview failed.");
            if (!migrationDryRun.SideEffectCheckPassed)
                blockers.Add($"Migration dry-run side-effect check failed ({migrationDryRun.SideEffectDifferenceCount} difference(s)).");
            foreach (var w in migrationDryRun.Warnings)
                warnings.Add($"Migration dry-run: {w}");
        }

        if (rollbackDryRun is not null)
        {
            if (!rollbackDryRun.IsAvailable || rollbackDryRun.Outcome == "Failed")
                blockers.Add("Rollback dry-run preview failed.");
            if (!rollbackDryRun.SideEffectCheckPassed)
                blockers.Add($"Rollback dry-run side-effect check failed ({rollbackDryRun.SideEffectDifferenceCount} difference(s)).");
            foreach (var w in rollbackDryRun.Warnings)
                warnings.Add($"Rollback dry-run: {w}");
        }
    }

    // Defense-in-depth: even though no source DTO carries these literal
    // strings, scrub any occurrence post-redaction. The rollback executor's
    // RequiredConfirmationPhrase constant is referenced rather than hardcoded
    // here so the two stay in sync.
    private static string ScrubConfirmationPhrases(string json)
    {
        if (string.IsNullOrEmpty(json)) return json;
        const string OldPhrase = "I UNDERSTAND TENANT DB ROLLBACK";
        var current = TenantDbRollbackExecutor.RequiredConfirmationPhrase;
        return json
            .Replace(current,   "<redacted-confirmation-phrase>")
            .Replace(OldPhrase, "<redacted-confirmation-phrase>");
    }

    private static string? SanitizeForFileName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().ToLowerInvariant();
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        s = s.Replace('\\', '_').Replace('/', '_').Replace(':', '_');
        const int MaxLen = 64;
        if (s.Length > MaxLen) s = s.Substring(0, MaxLen);
        return string.IsNullOrEmpty(s) ? "global" : s;
    }

    private static string ResolveNonCollidingPath(string basePath)
    {
        if (!File.Exists(basePath)) return basePath;

        var dir  = Path.GetDirectoryName(basePath) ?? "";
        var name = Path.GetFileNameWithoutExtension(basePath);
        var ext  = Path.GetExtension(basePath);

        for (int i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{name}-{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
        return Path.Combine(dir, $"{name}-{System.DateTime.UtcNow.Ticks}{ext}");
    }
}
