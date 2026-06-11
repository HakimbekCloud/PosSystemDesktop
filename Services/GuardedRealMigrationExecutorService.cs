using System.IO;
using System.Text.Json;
using PosSystem.Data.Repositories;

namespace PosSystem.Services;

// ── Options / Result DTOs (Phase 10.12B) ─────────────────────────────────────

public sealed class GuardedRealMigrationExecutionOptions
{
    public string? TenantSubdomain              { get; init; }

    // Double-confirmation #1.
    public bool   Force                         { get; init; }

    // Double-confirmation #2. Must equal
    // GuardedRealMigrationExecutorService.RequiredConfirmationPhrase verbatim.
    // The raw value is NEVER logged or copied into the result.
    public string? ConfirmationPhrase           { get; init; }

    // Manual operator acknowledgement that an external (off-machine) backup
    // exists before real migration mutates files. The note is free-form.
    public bool   ExternalBackupAcknowledged    { get; init; }
    public string? ExternalBackupNote           { get; init; }

    // When the gate is AllowedWithWarnings, this must be true for execution to
    // proceed. When the gate is Allowed (no warnings), this has no effect.
    public bool   AllowWarnings                 { get; init; }

    // Paths to the preflight + inventory bundles the operator reviewed before
    // pulling the trigger. Both files must exist, be .json, live under the
    // expected logs subdirectory, and be modified within the last 7 days.
    public string? ReviewedPreflightExportPath  { get; init; }
    public string? ReviewedInventoryExportPath  { get; init; }

    // Passed through to the underlying migrator (its own audit log). The
    // wrapper writes a separate audit entry regardless.
    public bool   WriteAuditLog                 { get; init; } = true;
}

public sealed class GuardedRealMigrationExecutionResult
{
    public System.DateTime StartedAtUtc          { get; init; }
    public System.DateTime CompletedAtUtc        { get; init; }

    // Rejected | Success | Failed
    public string Outcome                        { get; init; } = "Rejected";

    // True only when the underlying migrator was actually invoked. Stays
    // false for every rejection (i.e. any guard failure short-circuits before
    // the migrator is touched).
    public bool   MigrationExecuted              { get; init; }

    public string? TenantSubdomain               { get; init; }
    public string? GateStatus                    { get; init; }

    public bool   ConfirmationPhraseAccepted     { get; init; }
    public bool   ExternalBackupAcknowledged     { get; init; }

    public string? ReviewedPreflightExportPath   { get; init; }
    public string? ReviewedInventoryExportPath   { get; init; }

    public System.Collections.Generic.List<string> Steps           { get; init; } = new();
    public System.Collections.Generic.List<string> BlockingReasons { get; init; } = new();
    public System.Collections.Generic.List<string> Warnings        { get; init; } = new();
    public System.Collections.Generic.List<string> Errors          { get; init; } = new();

    // Sanitized summary of the underlying migrator's result. Never includes
    // raw tokens / phrases — only structural fields. Null when the migrator
    // was not invoked.
    public object? MigrationResultSummary        { get; init; }
}

// ── Service ──────────────────────────────────────────────────────────────────

// Strict wrapper that's the ONLY legitimate code path to call
// SharedToTenantDatabaseMigrator.MigrateAsync with DryRunOnly=false in this
// codebase. Phase 10.12B introduces the wrapper in DI; no UI surface, no
// auto-invocation. A future operator UI / CLI must construct
// GuardedRealMigrationExecutionOptions and call ExecuteAsync explicitly.
//
// Guard sequence (all collected, then evaluated together):
//   1. Force=true
//   2. ConfirmationPhrase == RequiredConfirmationPhrase (ordinal, case-sensitive)
//   3. ExternalBackupAcknowledged=true
//   4. ReviewedPreflightExportPath + ReviewedInventoryExportPath both exist,
//      end in .json, live under the expected logs subdirs, mtime within 7 days
//   5. RealMigrationExecutionGateService verdict allows execution
//   6. Direct re-check: feature flag on, runtime flag off, provider not
//      tenant-scoped, no existing migration marker
//
// If any guard fails: Outcome=Rejected, MigrationExecuted=false, never calls
// the migrator. If every guard passes: constructs real-run options
// (DryRunOnly=false + Force=true + AllowWhenFeatureDisabled=false +
// WriteAuditLog from options) and invokes the migrator exactly once.
//
// What this wrapper NEVER does:
//   • Invoke rollback (TenantDbRollbackExecutor is not injected).
//   • Switch the path provider (TenantScopeService is not injected).
//   • Enable tenant_db_runtime_enabled — that flag is the cutover trigger
//     and is the runtime decision's responsibility, not the migrator's.
//   • Delete / archive / restore / copy any file or directory beyond the
//     wrapper audit log write + whatever the migrator itself does.
//   • Log the raw confirmation phrase anywhere — only a boolean
//     ConfirmationPhraseAccepted is captured.
public sealed class GuardedRealMigrationExecutorService
{
    public const string RequiredConfirmationPhrase = "EXECUTE_REAL_TENANT_DB_MIGRATION";

    // Outcome enum-as-string. Centralised so audit log writers, ViewModels,
    // and future CLI all reference the same labels.
    public const string OutcomeRejected = "Rejected";
    public const string OutcomeSuccess  = "Success";
    public const string OutcomeFailed   = "Failed";

    private const string MigrationFlagKey   = "shared_to_tenant_migration_enabled";
    private const string RuntimeFlagKey     = "tenant_db_runtime_enabled";
    private const string MigratedMarkerKey  = "shared_to_tenant_migrated_at";
    private const int    RecentExportDays   = 7;
    private const string PreflightSubdir    = "preflight";
    private const string InventorySubdir    = "inventory";
    private const string ExecutorLogSubdir  = "migration-executor";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly RealMigrationExecutionGateService _gate;
    private readonly SharedToTenantDatabaseMigrator    _migrator;
    private readonly GlobalSettingsRepository          _global;
    private readonly Data.ILocalDatabasePathProvider   _paths;

    public GuardedRealMigrationExecutorService(
        RealMigrationExecutionGateService gate,
        SharedToTenantDatabaseMigrator migrator,
        GlobalSettingsRepository global,
        Data.ILocalDatabasePathProvider paths)
    {
        _gate     = gate;
        _migrator = migrator;
        _global   = global;
        _paths    = paths;
    }

    public async System.Threading.Tasks.Task<GuardedRealMigrationExecutionResult> ExecuteAsync(
        GuardedRealMigrationExecutionOptions options,
        System.Threading.CancellationToken ct = default)
    {
        if (options is null) throw new System.ArgumentNullException(nameof(options));

        var started  = System.DateTime.UtcNow;
        var steps    = new System.Collections.Generic.List<string>();
        var blockers = new System.Collections.Generic.List<string>();
        var warnings = new System.Collections.Generic.List<string>();
        var errors   = new System.Collections.Generic.List<string>();

        // ── Guard 1 — Force. ────────────────────────────────────────────────
        if (!options.Force)
            blockers.Add("Real migration requires Force=true.");
        else
            steps.Add("Guard passed: Force=true.");

        // ── Guard 2 — Confirmation phrase (ordinal, case-sensitive). ────────
        // Compare directly; never store the raw value in steps/result/audit.
        var phraseAccepted = string.Equals(
            options.ConfirmationPhrase,
            RequiredConfirmationPhrase,
            System.StringComparison.Ordinal);
        if (!phraseAccepted)
            blockers.Add(
                "Real migration requires the exact ConfirmationPhrase " +
                "(see GuardedRealMigrationExecutorService.RequiredConfirmationPhrase). " +
                "This deliberate double-confirmation prevents accidental execution.");
        else
            steps.Add("Guard passed: confirmation phrase accepted.");

        // ── Guard 3 — External backup acknowledgement. ──────────────────────
        if (!options.ExternalBackupAcknowledged)
        {
            blockers.Add(
                "Real migration requires ExternalBackupAcknowledged=true. " +
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

        // ── Guard 5 — Real migration gate service. ──────────────────────────
        string? gateStatus = null;
        try
        {
            var gateReport = await _gate.CheckAsync(options.TenantSubdomain, ct);
            gateStatus = gateReport.Status;

            // Copy gate findings into the result so a rejected execution
            // surfaces every reason without the caller re-running the gate.
            foreach (var b in gateReport.BlockingReasons)
                blockers.Add($"Gate: {b}");
            foreach (var w in gateReport.Warnings)
                warnings.Add($"Gate: {w}");

            if (!gateReport.CanExecuteRealMigration)
            {
                blockers.Add($"Real migration gate refused execution (Status={gateReport.Status}).");
            }
            else if (gateReport.Status == "AllowedWithWarnings" && !options.AllowWarnings)
            {
                blockers.Add(
                    "Gate status is AllowedWithWarnings but options.AllowWarnings=false. " +
                    "Set AllowWarnings=true to proceed despite warnings.");
            }
            else
            {
                steps.Add($"Guard passed: real migration gate allowed execution (Status={gateReport.Status}).");
            }
        }
        catch (System.Exception ex)
        {
            blockers.Add($"Gate service threw: {ex.Message}");
        }

        // ── Guard 6 — Direct runtime/provider double-check. ─────────────────
        var runtimeFlag     = _global.Get(RuntimeFlagKey) == "1";
        var migrationFlag   = _global.Get(MigrationFlagKey) == "1";
        var migratedMarker  = _global.Get(MigratedMarkerKey);
        var alreadyMigrated = !string.IsNullOrEmpty(migratedMarker);
        var providerScoped  = _paths.IsTenantScoped;

        var directCheckBlockersBefore = blockers.Count;
        if (!migrationFlag)
            blockers.Add($"Direct check: {MigrationFlagKey} != \"1\".");
        if (runtimeFlag)
            blockers.Add($"Direct check: {RuntimeFlagKey} is already \"1\".");
        if (providerScoped)
            blockers.Add("Direct check: path provider is already tenant-scoped.");
        if (alreadyMigrated)
            blockers.Add($"Direct check: {MigratedMarkerKey} is already set (\"{migratedMarker}\").");
        if (blockers.Count == directCheckBlockersBefore)
            steps.Add("Guard passed: runtime/provider state is safe.");

        var resolvedTenant = string.IsNullOrWhiteSpace(options.TenantSubdomain)
            ? _global.Get("last_tenant_subdomain")
            : options.TenantSubdomain.Trim();

        // ── Evaluate guards. Any blocker → reject without invoking migrator.
        if (blockers.Count > 0)
        {
            steps.Add($"Rejected at guard stage: {blockers.Count} blocker(s).");
            var rejected = new GuardedRealMigrationExecutionResult
            {
                StartedAtUtc                 = started,
                CompletedAtUtc               = System.DateTime.UtcNow,
                Outcome                      = OutcomeRejected,
                MigrationExecuted            = false,
                TenantSubdomain              = resolvedTenant,
                GateStatus                   = gateStatus,
                ConfirmationPhraseAccepted   = phraseAccepted,
                ExternalBackupAcknowledged   = options.ExternalBackupAcknowledged,
                ReviewedPreflightExportPath  = options.ReviewedPreflightExportPath,
                ReviewedInventoryExportPath  = options.ReviewedInventoryExportPath,
                Steps                        = steps,
                BlockingReasons              = blockers,
                Warnings                     = warnings,
                Errors                       = errors,
                MigrationResultSummary       = null,
            };
            WriteAuditLogIfRequested(options, rejected);
            return rejected;
        }

        // ── Guard 7 — Construct real migrator options. Done only after all
        //    prior guards pass; these flags do not flow from the wrapper's
        //    options to keep them out of UI binding paths.
        var realOptions = new SharedToTenantMigrationOptions
        {
            DryRunOnly               = false,
            Force                    = true,
            AllowWhenFeatureDisabled = false,
            WriteAuditLog            = options.WriteAuditLog,
        };
        steps.Add(
            "All guards passed; constructed real migrator options " +
            "(DryRunOnly=false, Force=true, AllowWhenFeatureDisabled=false).");

        // ── Execute real migration. Exactly one call. ───────────────────────
        SharedToTenantMigrationResult? migrationResult = null;
        var executed = false;
        var outcome  = OutcomeFailed;
        try
        {
            executed = true;
            steps.Add("Calling SharedToTenantDatabaseMigrator.MigrateAsync exactly once.");
            migrationResult = await _migrator.MigrateAsync(realOptions, ct);
            steps.Add($"Migrator returned: Outcome={migrationResult.Outcome}, DryRun={migrationResult.DryRun}.");
            outcome = migrationResult.Outcome == SharedToTenantMigrationOutcome.Success
                ? OutcomeSuccess
                : OutcomeFailed;
            if (outcome == OutcomeFailed && !string.IsNullOrEmpty(migrationResult.FailureReason))
                errors.Add(migrationResult.FailureReason!);
        }
        catch (System.Exception ex)
        {
            outcome = OutcomeFailed;
            errors.Add($"Migrator threw: {ex.Message}");
            // Do NOT auto-rollback. Do NOT auto-switch DB. Do NOT enable
            // runtime tenant DB mode. Operator-driven recovery only.
            steps.Add("Exception raised; no automatic rollback / no DB switch / no runtime flag enable.");
        }

        var finalResult = new GuardedRealMigrationExecutionResult
        {
            StartedAtUtc                 = started,
            CompletedAtUtc               = System.DateTime.UtcNow,
            Outcome                      = outcome,
            MigrationExecuted            = executed,
            TenantSubdomain              = resolvedTenant,
            GateStatus                   = gateStatus,
            ConfirmationPhraseAccepted   = phraseAccepted,
            ExternalBackupAcknowledged   = options.ExternalBackupAcknowledged,
            ReviewedPreflightExportPath  = options.ReviewedPreflightExportPath,
            ReviewedInventoryExportPath  = options.ReviewedInventoryExportPath,
            Steps                        = steps,
            BlockingReasons              = blockers,
            Warnings                     = warnings,
            Errors                       = errors,
            MigrationResultSummary       = migrationResult is null
                ? null
                : BuildMigrationResultSummary(migrationResult),
        };

        WriteAuditLogIfRequested(options, finalResult);
        return finalResult;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    // Returns true when the export validates cleanly. Any failure is reported
    // via the blockers list and the method returns false so the caller can
    // emit a "Guard passed" step only on success.
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

    // Containment check that defeats prefix tricks like `logs\preflight_evil\…`
    // against an expected `logs\preflight\` directory. Both paths are first
    // normalized through Path.GetFullPath (resolves `..` / `.` / mixed
    // separators / casing), then the expected directory is given a trailing
    // separator so a literal `StartsWith` requires the path to be inside the
    // exact directory boundary, not just sharing a prefix string.
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

    // Belt-and-suspenders separator normaliser. Trim any existing trailing
    // separator (so we don't get `…\\` on already-normalised input) and then
    // append exactly one platform separator.
    private static string EnsureTrailingDirectorySeparator(string path)
        => Path.TrimEndingDirectorySeparator(path) + Path.DirectorySeparatorChar;

    // Curated, structural subset of the migrator's result. Never includes raw
    // tokens or phrases — those aren't structurally present anyway; this
    // method just picks the fields that are meaningful in an operator review.
    private static object BuildMigrationResultSummary(SharedToTenantMigrationResult r)
        => new
        {
            Outcome              = r.Outcome.ToString(),
            r.DryRun,
            r.SourceDbPath,
            r.BackupPath,
            r.BackupSha256,
            r.OrphanSalesQuarantined,
            TenantCount          = r.Tenants.Count,
            Tenants              = r.Tenants.Select(t => new
            {
                t.Subdomain,
                t.TargetDbPath,
                Outcome      = t.Outcome.ToString(),
                t.SalesCopied,
                t.SaleItemsCopied,
                t.SettingsCopied,
                t.CatalogCopied,
            }).ToList(),
            r.StartedAtUtc,
            r.CompletedAtUtc,
        };

    // ── Wrapper audit log writer ─────────────────────────────────────────────
    //
    // Writes one JSON entry per ExecuteAsync invocation under
    //   %LocalAppData%\PosSystem\logs\migration-executor\
    // The serialized options view contains only sanitized booleans plus the
    // reviewed export paths; the raw ConfirmationPhrase never crosses this
    // boundary. RedactSecrets + literal phrase scrub provide defense in depth.
    private static void WriteAuditLogIfRequested(
        GuardedRealMigrationExecutionOptions options,
        GuardedRealMigrationExecutionResult result)
    {
        if (!options.WriteAuditLog) return;

        try
        {
            var logDir = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "PosSystem", "logs", ExecutorLogSubdir);
            Directory.CreateDirectory(logDir);

            var stamp = System.DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
            var path  = Path.Combine(logDir, $"migration-executor-{stamp}-{result.Outcome}.json");

            // Sanitized options view. Strict allow-list per Phase 10.12B.1 — only
            // these fields cross the serialization boundary. The raw
            // ConfirmationPhrase is NOT in this projection; instead the two
            // booleans ConfirmationPhraseProvided (caller supplied any value)
            // and ConfirmationPhraseAccepted (value matched verbatim) are
            // emitted. TenantSubdomain is intentionally not in the options view
            // — it still appears at the top of the Result section, which is a
            // separate field of the audit entry.
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
            safe     = ScrubConfirmationPhrase(safe);

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

    // Multi-phrase literal scrub. Always replaces this wrapper's own
    // confirmation phrase. As defense-in-depth across phases also replaces
    // the rollback executor's confirmation phrases — they should never appear
    // in this wrapper's logs, but a future change could leak them. RedactSecrets
    // runs first and handles tokens/JWT/DPAPI; this pass handles fixed phrases.
    private const string RollbackPhraseCurrent = "ROLLBACK_TO_LEGACY_POS_DB";
    private const string RollbackPhraseLegacy  = "I UNDERSTAND TENANT DB ROLLBACK";

    private static string ScrubConfirmationPhrase(string json)
    {
        if (string.IsNullOrEmpty(json)) return json;
        return json
            .Replace(RequiredConfirmationPhrase, "<redacted-confirmation-phrase>")
            .Replace(RollbackPhraseCurrent,      "<redacted-confirmation-phrase>")
            .Replace(RollbackPhraseLegacy,       "<redacted-confirmation-phrase>");
    }
}
