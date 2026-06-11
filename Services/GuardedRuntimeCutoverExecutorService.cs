using System.IO;
using System.Text.Json;
using PosSystem.Data.Repositories;

namespace PosSystem.Services;

// ── Options / Result DTOs (Phase 10.13A) ─────────────────────────────────────

public sealed class GuardedRuntimeCutoverExecutionOptions
{
    public string? TenantSubdomain              { get; init; }

    public bool   Force                         { get; init; }

    // Must equal
    // GuardedRuntimeCutoverExecutorService.RequiredConfirmationPhrase verbatim.
    // The raw value is NEVER logged or copied into the result.
    public string? ConfirmationPhrase           { get; init; }

    // Operator's manual acknowledgement that an off-machine backup of
    // %LocalAppData%\PosSystem exists. Note is free-form.
    public bool   ExternalBackupAcknowledged    { get; init; }
    public string? ExternalBackupNote           { get; init; }

    // When cutover readiness is AllowedWithWarnings, this must be true for
    // execution to proceed.
    public bool   AllowWarnings                 { get; init; }

    // Paths to the preflight + inventory bundles the operator reviewed. Both
    // must exist, be .json, live under the expected logs subdirectories, and
    // be modified within the last 7 days. Prefix tricks (`preflight_evil\…`)
    // are rejected.
    public string? ReviewedPreflightExportPath  { get; init; }
    public string? ReviewedInventoryExportPath  { get; init; }

    public bool   WriteAuditLog                 { get; init; } = true;
}

public sealed class GuardedRuntimeCutoverExecutionResult
{
    public System.DateTime StartedAtUtc          { get; init; }
    public System.DateTime CompletedAtUtc        { get; init; }

    // Rejected | Success | Failed
    public string Outcome                        { get; init; } = "Rejected";

    // True only when tenant_db_runtime_enabled was actually flipped from "0"
    // (or empty) to "1" by this execution. Stays false for every rejection
    // (no setting write happens before guards pass).
    public bool   RuntimeFlagChanged             { get; init; }

    public string? TenantSubdomain               { get; init; }
    public string? CutoverStatus                 { get; init; }

    public bool   ConfirmationPhraseAccepted     { get; init; }
    public bool   ExternalBackupAcknowledged     { get; init; }

    public string? ReviewedPreflightExportPath   { get; init; }
    public string? ReviewedInventoryExportPath   { get; init; }

    public string? RuntimeFlagBefore             { get; init; }
    public string? RuntimeFlagAfter              { get; init; }

    public System.Collections.Generic.List<string> Steps           { get; init; } = new();
    public System.Collections.Generic.List<string> BlockingReasons { get; init; } = new();
    public System.Collections.Generic.List<string> Warnings        { get; init; } = new();
    public System.Collections.Generic.List<string> Errors          { get; init; } = new();
}

// ── Service ──────────────────────────────────────────────────────────────────

// Strict wrapper that's the ONLY legitimate code path to write
// tenant_db_runtime_enabled = "1" in this codebase. Phase 10.13A introduces
// the wrapper in DI; no UI surface, no auto-invocation. A future operator UI
// / CLI must construct GuardedRuntimeCutoverExecutionOptions and call
// ExecuteAsync explicitly.
//
// Guard sequence (all collected, then evaluated together):
//   1. Force=true
//   2. ConfirmationPhrase == RequiredConfirmationPhrase (ordinal, case-sensitive)
//   3. ExternalBackupAcknowledged=true
//   4. ReviewedPreflightExportPath + ReviewedInventoryExportPath both exist,
//      end in .json, live under the expected logs subdirs (prefix-trick safe),
//      mtime within 7 days
//   5. Runtime / provider state: runtime flag not already on, provider not
//      tenant-scoped, tenant resolvable
//   6. Migration marker shared_to_tenant_migrated_at non-empty
//   7. Verifier AllVerified, and per-tenant entry for the requested tenant is
//      Verified
//   8. TenantCutoverReadinessGate verdict allows execution
//   9. Diagnostics report obtainable, no pending sales, no poison sales
//
// If any guard fails: Outcome=Rejected, RuntimeFlagChanged=false, NO setting
// write happens. If every guard passes: writes tenant_db_runtime_enabled="1"
// plus metadata (enabled_at, enabled_by) and returns Success.
//
// What this wrapper NEVER does:
//   • Invoke the migrator (SharedToTenantDatabaseMigrator is not injected).
//   • Invoke rollback (TenantDbRollbackExecutor is not injected).
//   • Switch the path provider (TenantScopeService is not injected;
//     ILocalDatabasePathProvider.UseTenantDatabase / UseLegacyDatabase is
//     never called from the wrapper).
//   • Delete / archive / restore / copy any file beyond the audit log temp
//     file + atomic rename.
//   • Log the raw confirmation phrase anywhere.
//   • Auto-logout / auto-relaunch — the spec requires the operator to
//     restart/re-login manually after enabling runtime mode.
public sealed class GuardedRuntimeCutoverExecutorService
{
    public const string RequiredConfirmationPhrase = "ENABLE_TENANT_DB_RUNTIME_MODE";

    // Outcome enum-as-string. Same convention as GuardedRealMigrationExecutorService.
    public const string OutcomeRejected = "Rejected";
    public const string OutcomeSuccess  = "Success";
    public const string OutcomeFailed   = "Failed";

    private const string RuntimeFlagKey      = "tenant_db_runtime_enabled";
    private const string RuntimeAtKey        = "tenant_db_runtime_enabled_at";
    private const string RuntimeByKey        = "tenant_db_runtime_enabled_by";
    private const string MigrationMarkerKey  = "shared_to_tenant_migrated_at";
    private const string LastTenantKey       = "last_tenant_subdomain";
    private const int    RecentExportDays    = 7;
    private const string PreflightSubdir     = "preflight";
    private const string InventorySubdir     = "inventory";
    private const string CutoverLogSubdir    = "runtime-cutover";

    // Phrases scrubbed from every audit log entry as defense in depth.
    private const string PhraseRuntime       = "ENABLE_TENANT_DB_RUNTIME_MODE";
    private const string PhraseMigration     = "EXECUTE_REAL_TENANT_DB_MIGRATION";
    private const string PhraseRollback      = "ROLLBACK_TO_LEGACY_POS_DB";
    private const string PhraseRollbackOld   = "I UNDERSTAND TENANT DB ROLLBACK";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly TenantCutoverReadinessGate       _cutoverGate;
    private readonly SharedToTenantMigrationVerifier  _verifier;
    private readonly OperatorDiagnosticsService       _diagnostics;
    private readonly GlobalSettingsRepository         _global;
    private readonly Data.ILocalDatabasePathProvider  _paths;

    public GuardedRuntimeCutoverExecutorService(
        TenantCutoverReadinessGate cutoverGate,
        SharedToTenantMigrationVerifier verifier,
        OperatorDiagnosticsService diagnostics,
        GlobalSettingsRepository global,
        Data.ILocalDatabasePathProvider paths)
    {
        _cutoverGate = cutoverGate;
        _verifier    = verifier;
        _diagnostics = diagnostics;
        _global      = global;
        _paths       = paths;
    }

    public async System.Threading.Tasks.Task<GuardedRuntimeCutoverExecutionResult> ExecuteAsync(
        GuardedRuntimeCutoverExecutionOptions options,
        System.Threading.CancellationToken ct = default)
    {
        if (options is null) throw new System.ArgumentNullException(nameof(options));

        var started  = System.DateTime.UtcNow;
        var steps    = new System.Collections.Generic.List<string>();
        var blockers = new System.Collections.Generic.List<string>();
        var warnings = new System.Collections.Generic.List<string>();
        var errors   = new System.Collections.Generic.List<string>();

        // Snapshot the runtime flag BEFORE doing anything else so we can
        // report a precise before/after even on a Rejected outcome.
        var runtimeFlagBefore = _global.Get(RuntimeFlagKey);

        // ── Guard 1 — Force. ────────────────────────────────────────────────
        if (!options.Force)
            blockers.Add("Runtime cutover requires Force=true.");
        else
            steps.Add("Guard passed: Force=true.");

        // ── Guard 2 — Confirmation phrase (ordinal, case-sensitive). ────────
        var phraseAccepted = string.Equals(
            options.ConfirmationPhrase,
            RequiredConfirmationPhrase,
            System.StringComparison.Ordinal);
        if (!phraseAccepted)
            blockers.Add(
                "Runtime cutover requires the exact ConfirmationPhrase " +
                "(see GuardedRuntimeCutoverExecutorService.RequiredConfirmationPhrase). " +
                "This deliberate double-confirmation prevents accidental cutover.");
        else
            steps.Add("Guard passed: confirmation phrase accepted.");

        // ── Guard 3 — External backup acknowledgement. ──────────────────────
        if (!options.ExternalBackupAcknowledged)
        {
            blockers.Add(
                "Runtime cutover requires ExternalBackupAcknowledged=true. " +
                "Operator must capture an off-machine backup of " +
                @"%LocalAppData%\PosSystem before proceeding.");
        }
        else
        {
            steps.Add("Guard passed: external backup acknowledged.");
            if (string.IsNullOrWhiteSpace(options.ExternalBackupNote))
                warnings.Add("ExternalBackupAcknowledged=true but no ExternalBackupNote was provided.");
        }

        // ── Guard 4 — Reviewed export files (preflight + inventory). ────────
        var legacyDb = _paths.GetLegacyDbPath();
        var baseDir  = Path.GetDirectoryName(legacyDb)
                       ?? Path.Combine(
                              System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                              "PosSystem");
        var expectedPreflightDir = Path.Combine(baseDir, "logs", PreflightSubdir);
        var expectedInventoryDir = Path.Combine(baseDir, "logs", InventorySubdir);

        if (ValidateReviewedExport(
                "Preflight export",
                options.ReviewedPreflightExportPath,
                expectedPreflightDir,
                blockers))
        {
            steps.Add("Guard passed: reviewed preflight export is recent and under expected directory.");
        }
        if (ValidateReviewedExport(
                "Inventory export",
                options.ReviewedInventoryExportPath,
                expectedInventoryDir,
                blockers))
        {
            steps.Add("Guard passed: reviewed inventory export is recent and under expected directory.");
        }

        // ── Guard 5 — Runtime / provider / tenant resolution. ───────────────
        var runtimeAlreadyOn = runtimeFlagBefore == "1";
        var providerScoped   = _paths.IsTenantScoped;

        if (runtimeAlreadyOn)
            blockers.Add($"Direct check: {RuntimeFlagKey} is already \"1\" — runtime mode is already enabled.");
        if (providerScoped)
            blockers.Add(
                "Direct check: path provider is already tenant-scoped — restart the app in legacy mode " +
                "before performing runtime cutover.");

        var resolvedTenant = string.IsNullOrWhiteSpace(options.TenantSubdomain)
            ? _global.Get(LastTenantKey)
            : options.TenantSubdomain.Trim();
        if (string.IsNullOrWhiteSpace(resolvedTenant))
            blockers.Add($"Tenant subdomain is missing and cannot be resolved from {LastTenantKey}.");
        else if (!runtimeAlreadyOn && !providerScoped)
            steps.Add($"Guard passed: runtime/provider state is safe; resolved tenant '{resolvedTenant}'.");

        // ── Guard 6 — Migration marker. ─────────────────────────────────────
        var migrationMarker = _global.Get(MigrationMarkerKey);
        if (string.IsNullOrWhiteSpace(migrationMarker))
        {
            blockers.Add(
                $"Migration marker {MigrationMarkerKey} is missing — real migration must complete before runtime cutover.");
        }
        else
        {
            steps.Add($"Guard passed: migration marker present ({MigrationMarkerKey}=\"{migrationMarker}\").");
        }

        // ── Guard 7 — Verification. ─────────────────────────────────────────
        try
        {
            var verify = await _verifier.VerifyAsync(ct);
            if (verify is null)
            {
                blockers.Add("Migration verification report is null.");
            }
            else if (verify.SourceDbExists && verify.GlobalMarkerPresent && !verify.AllVerified)
            {
                blockers.Add("Migration verification did not pass (AllVerified=false).");
            }
            else if (!verify.GlobalMarkerPresent)
            {
                blockers.Add("Migration verifier reports no global marker — migration has not completed.");
            }
            else if (!string.IsNullOrWhiteSpace(resolvedTenant))
            {
                // Per-tenant entry must be present and Verified for the
                // requested tenant.
                var match = verify.Tenants.FirstOrDefault(t =>
                    string.Equals(t.Subdomain, resolvedTenant, System.StringComparison.OrdinalIgnoreCase));
                if (match is null)
                    blockers.Add($"Verifier has no entry for tenant '{resolvedTenant}'.");
                else if (!match.Verified)
                    blockers.Add(
                        $"Verifier marked tenant '{resolvedTenant}' as not Verified " +
                        $"({string.Join("; ", match.Issues)}).");
                else
                    steps.Add($"Guard passed: verifier confirms tenant '{resolvedTenant}' is Verified.");
            }
            else
            {
                steps.Add("Guard passed: verifier AllVerified, no specific tenant to check.");
            }
        }
        catch (System.Exception ex)
        {
            blockers.Add($"Migration verifier failed: {ex.Message}");
        }

        // ── Guard 8 — Cutover readiness. ────────────────────────────────────
        string? cutoverStatus = null;
        if (!string.IsNullOrWhiteSpace(resolvedTenant))
        {
            try
            {
                var cutover = await _cutoverGate.CheckAsync(resolvedTenant, ct);
                cutoverStatus = cutover.Status.ToString();

                foreach (var w in cutover.Warnings) warnings.Add($"Cutover: {w}");

                if (cutover.Status == TenantDbCutoverReadinessStatus.Disabled ||
                    cutover.Status == TenantDbCutoverReadinessStatus.Blocked)
                {
                    blockers.Add($"Cutover readiness is {cutover.Status}.");
                }
                else if (cutover.Status == TenantDbCutoverReadinessStatus.AllowedWithWarnings &&
                         !options.AllowWarnings)
                {
                    blockers.Add(
                        "Cutover readiness is AllowedWithWarnings but options.AllowWarnings=false. " +
                        "Set AllowWarnings=true to proceed despite warnings.");
                }
                else
                {
                    steps.Add($"Guard passed: cutover readiness is {cutover.Status}.");
                }
            }
            catch (System.Exception ex)
            {
                blockers.Add($"Cutover readiness check failed: {ex.Message}");
            }
        }
        // (If tenant unresolved, the resolution blocker in Guard 5 already
        //  covers that case; we don't double-blame here.)

        // ── Guard 9 — Diagnostics (pending/poison sales). ───────────────────
        try
        {
            var diag = await _diagnostics.GetReportAsync(resolvedTenant, ct);
            if (diag is null)
            {
                blockers.Add("Diagnostics report unavailable.");
            }
            else
            {
                if (diag.Sales.PendingSalesCount > 0)
                    blockers.Add($"{diag.Sales.PendingSalesCount} pending sales exist.");
                if (diag.Sales.PoisonSalesCount > 0)
                    blockers.Add($"{diag.Sales.PoisonSalesCount} poison sales exist.");
                if (diag.Sales.PendingSalesCount == 0 && diag.Sales.PoisonSalesCount == 0)
                    steps.Add("Guard passed: no pending/poison sales.");
                foreach (var w in diag.Warnings) warnings.Add($"Diagnostics: {w}");
            }
        }
        catch (System.Exception ex)
        {
            blockers.Add($"Diagnostics subsystem failed: {ex.Message}");
        }

        // ── Evaluate guards. Any blocker → reject without writing settings.
        if (blockers.Count > 0)
        {
            steps.Add($"Rejected at guard stage: {blockers.Count} blocker(s). No setting write occurred.");
            var rejected = new GuardedRuntimeCutoverExecutionResult
            {
                StartedAtUtc                 = started,
                CompletedAtUtc               = System.DateTime.UtcNow,
                Outcome                      = OutcomeRejected,
                RuntimeFlagChanged           = false,
                TenantSubdomain              = resolvedTenant,
                CutoverStatus                = cutoverStatus,
                ConfirmationPhraseAccepted   = phraseAccepted,
                ExternalBackupAcknowledged   = options.ExternalBackupAcknowledged,
                ReviewedPreflightExportPath  = options.ReviewedPreflightExportPath,
                ReviewedInventoryExportPath  = options.ReviewedInventoryExportPath,
                RuntimeFlagBefore            = runtimeFlagBefore,
                RuntimeFlagAfter             = runtimeFlagBefore, // unchanged
                Steps                        = steps,
                BlockingReasons              = blockers,
                Warnings                     = warnings,
                Errors                       = errors,
            };
            WriteAuditLogIfRequested(options, rejected);
            return rejected;
        }

        // ── All guards passed — perform the only allowed mutation. ──────────
        var outcome = OutcomeSuccess;
        var flagChanged = false;
        string? runtimeFlagAfter = runtimeFlagBefore;

        try
        {
            steps.Add($"Writing {RuntimeFlagKey} = \"1\" (the only mutation this wrapper performs beyond audit log).");
            _global.Set(RuntimeFlagKey, "1");
            flagChanged = true;
            runtimeFlagAfter = "1";
            steps.Add($"{RuntimeFlagKey} set to 1.");

            // Best-effort metadata. Failures here are recorded as warnings —
            // the runtime flag flip is already complete.
            try
            {
                _global.Set(RuntimeAtKey, System.DateTime.UtcNow.ToString("o"));
                _global.Set(RuntimeByKey, "operator");
                steps.Add($"Wrote metadata: {RuntimeAtKey}, {RuntimeByKey}.");
            }
            catch (System.Exception ex)
            {
                warnings.Add($"Failed to write metadata setting: {ex.Message}");
            }

            steps.Add("No DB switch was performed. Restart/re-login is required for the runtime tenant DB to take effect.");
        }
        catch (System.Exception ex)
        {
            outcome = OutcomeFailed;
            errors.Add($"Failed to write {RuntimeFlagKey}: {ex.Message}");
            steps.Add(
                "Setting write failed. No automatic rollback / no DB switch / no migrator call. " +
                "Investigate manually.");
        }

        var finalResult = new GuardedRuntimeCutoverExecutionResult
        {
            StartedAtUtc                 = started,
            CompletedAtUtc               = System.DateTime.UtcNow,
            Outcome                      = outcome,
            RuntimeFlagChanged           = flagChanged,
            TenantSubdomain              = resolvedTenant,
            CutoverStatus                = cutoverStatus,
            ConfirmationPhraseAccepted   = phraseAccepted,
            ExternalBackupAcknowledged   = options.ExternalBackupAcknowledged,
            ReviewedPreflightExportPath  = options.ReviewedPreflightExportPath,
            ReviewedInventoryExportPath  = options.ReviewedInventoryExportPath,
            RuntimeFlagBefore            = runtimeFlagBefore,
            RuntimeFlagAfter             = runtimeFlagAfter,
            Steps                        = steps,
            BlockingReasons              = blockers,
            Warnings                     = warnings,
            Errors                       = errors,
        };

        WriteAuditLogIfRequested(options, finalResult);
        return finalResult;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool ValidateReviewedExport(
        string label,
        string? path,
        string expectedDirectory,
        System.Collections.Generic.List<string> blockers)
    {
        var startCount = blockers.Count;

        if (string.IsNullOrWhiteSpace(path))
        {
            blockers.Add($"{label}: ReviewedExportPath is required.");
            return false;
        }

        if (!path.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
            blockers.Add($"{label}: must have .json extension ({path}).");

        FileInfo fi;
        try
        {
            fi = new FileInfo(path);
        }
        catch (System.Exception ex)
        {
            blockers.Add($"{label}: cannot stat {path}: {ex.Message}");
            return false;
        }

        if (!fi.Exists)
        {
            blockers.Add($"{label}: file does not exist ({path}).");
            return false;
        }

        if (!IsUnder(fi.FullName, expectedDirectory))
        {
            blockers.Add(
                $"{label}: file must live under {expectedDirectory} (was {fi.FullName}). " +
                "Prefix-similar paths (e.g. <expected>_evil\\...) are rejected.");
        }

        var age = (System.DateTime.UtcNow - fi.LastWriteTimeUtc).TotalDays;
        if (age > RecentExportDays)
        {
            blockers.Add(
                $"{label}: file is older than {RecentExportDays} days " +
                $"({(int)age}d old, mtime {fi.LastWriteTimeUtc:o}).");
        }

        return blockers.Count == startCount;
    }

    // Containment check that defeats prefix tricks (e.g.
    // `logs\preflight_evil\…` against `logs\preflight\`). Same logic as
    // GuardedRealMigrationExecutorService.IsUnder — duplicated here to keep
    // each wrapper self-contained.
    private static bool IsUnder(string filePath, string directoryPath)
    {
        try
        {
            var normalizedFile = Path.GetFullPath(filePath);
            var normalizedDir  = EnsureTrailingDirectorySeparator(Path.GetFullPath(directoryPath));
            return normalizedFile.StartsWith(normalizedDir, System.StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string EnsureTrailingDirectorySeparator(string path)
        => Path.TrimEndingDirectorySeparator(path) + Path.DirectorySeparatorChar;

    // ── Wrapper audit log writer ─────────────────────────────────────────────
    //
    // Writes one JSON entry per ExecuteAsync invocation under
    //   %LocalAppData%\PosSystem\logs\runtime-cutover\
    // Sanitized options view contains only the strict allow-list of fields;
    // the raw ConfirmationPhrase never crosses this boundary. RedactSecrets
    // + multi-phrase scrub provide defense in depth.
    private static void WriteAuditLogIfRequested(
        GuardedRuntimeCutoverExecutionOptions options,
        GuardedRuntimeCutoverExecutionResult result)
    {
        if (!options.WriteAuditLog) return;

        try
        {
            var logDir = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "PosSystem", "logs", CutoverLogSubdir);
            Directory.CreateDirectory(logDir);

            var stamp = System.DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
            var path  = Path.Combine(logDir, $"runtime-cutover-{stamp}-{result.Outcome}.json");

            // Strict allow-list. TenantSubdomain and ExternalBackupNote are
            // deliberately not in the audit options projection.
            var safeOptions = new
            {
                options.Force,
                ConfirmationPhraseProvided   = !string.IsNullOrEmpty(options.ConfirmationPhrase),
                ConfirmationPhraseAccepted   = result.ConfirmationPhraseAccepted,
                options.ExternalBackupAcknowledged,
                ExternalBackupNoteProvided   = !string.IsNullOrEmpty(options.ExternalBackupNote),
                options.AllowWarnings,
                options.WriteAuditLog,
                options.ReviewedPreflightExportPath,
                options.ReviewedInventoryExportPath,
            };

            var entry = new
            {
                TimestampUtc = System.DateTime.UtcNow,
                MachineName  = System.Environment.MachineName,
                OsUser       = System.Environment.UserName,
                Options      = safeOptions,
                Result       = result,
            };

            var raw  = JsonSerializer.Serialize(entry, JsonOptions);
            var safe = MigrationAuditLogger.RedactSecrets(raw);
            safe     = ScrubConfirmationPhrases(safe);

            var tmp = path + ".tmp";
            File.WriteAllText(tmp, safe);
            if (File.Exists(path)) File.Replace(tmp, path, destinationBackupFileName: null);
            else File.Move(tmp, path);
        }
        catch
        {
            // Best effort — audit log write must not influence execution outcome.
        }
    }

    private static string ScrubConfirmationPhrases(string json)
    {
        if (string.IsNullOrEmpty(json)) return json;
        return json
            .Replace(PhraseRuntime,     "<redacted-confirmation-phrase>")
            .Replace(PhraseMigration,   "<redacted-confirmation-phrase>")
            .Replace(PhraseRollback,    "<redacted-confirmation-phrase>")
            .Replace(PhraseRollbackOld, "<redacted-confirmation-phrase>");
    }
}
