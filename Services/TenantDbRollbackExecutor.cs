using System.IO;
using System.Text.Json;
using PosSystem.Data;
using PosSystem.Data.Repositories;

namespace PosSystem.Services;

// ── Options ──────────────────────────────────────────────────────────────────

public sealed class TenantDbRollbackOptions
{
    public bool   DryRunOnly                      { get; init; } = true;
    public bool   Force                           { get; init; } = false;
    public string? ConfirmationPhrase             { get; init; }
    public bool   ArchiveTenantsDirectory         { get; init; } = true;
    public bool   DisableRuntimeFlag              { get; init; } = true;
    public bool   RestoreLegacyFromBackupIfMissing { get; init; } = false;
    public bool   WriteAuditLog                   { get; init; } = false;
}

// ── Result DTOs ──────────────────────────────────────────────────────────────

public enum TenantDbRollbackOutcome
{
    DryRun,    // dry-run completed against an active runtime tenant DB mode
    Rejected,  // any guard failure — Force/phrase/provider/legacy/checker
    NoOp,      // runtime mode already off; nothing to do (including dry-run)
    Success,   // real rollback completed
    Failed,    // unexpected/partial mutation; details in FailureReason
}

public sealed class TenantDbRollbackResult
{
    public TenantDbRollbackOutcome Outcome        { get; init; }
    public bool   DryRun                          { get; init; }
    public string? FailureReason                  { get; init; }

    public string LegacyDbPath                    { get; init; } = "";
    public string TenantsDirectory                { get; init; } = "";
    public string? ArchivedTenantsPath            { get; init; }
    public string GlobalSettingsPath              { get; init; } = "";
    public string? BackupUsedPath                 { get; init; }
    public string? BrokenLegacyRenamedTo          { get; init; }

    public bool   RuntimeFlagWasEnabled           { get; init; }
    public bool   RuntimeFlagDisabledByThisRun    { get; init; }
    public bool   TenantsDirectoryArchived        { get; init; }
    public bool   LegacyRestoredFromBackup        { get; init; }
    public bool   ConfirmationPhraseAccepted      { get; init; }

    public System.Collections.Generic.IReadOnlyList<string> Steps
        { get; init; } = System.Array.Empty<string>();

    public string? AuditLogPath                   { get; set; }
    public System.DateTime StartedAtUtc           { get; init; }
    public System.DateTime CompletedAtUtc         { get; init; }
}

// ── Executor ─────────────────────────────────────────────────────────────────

// Highly guarded executor for the manual rollback procedure documented in
// docs/tenant-db-rollback.md. Phase 10.6B.2 alignment: the outcome model is
// {DryRun, Rejected, NoOp, Success, Failed}; real rollback requires Force=true
// AND ConfirmationPhrase=RequiredConfirmationPhrase AND the path provider
// already in legacy mode; the raw phrase is never logged.
//
// What this executor never does:
//   • Delete or rename legacy pos.db (it may RESTORE it from backup when
//     RestoreLegacyFromBackupIfMissing=true).
//   • Delete tenant DBs (only renames the parent tenants\ directory).
//   • Delete backups.
//   • Run any migrator code path.
//   • Trigger sync, logout, or navigation.
//   • Switch the path provider — if provider is tenant-scoped, the executor
//     rejects rather than mutating runtime state.
public sealed class TenantDbRollbackExecutor
{
    private const string RuntimeFlagKey = "tenant_db_runtime_enabled";

    // Phase 10.6B.2 confirmation phrase. Must be passed verbatim via
    // TenantDbRollbackOptions.ConfirmationPhrase to authorize a real rollback.
    public const string RequiredConfirmationPhrase = "ROLLBACK_TO_LEGACY_POS_DB";

    private readonly ILocalDatabasePathProvider           _pathProvider;
    private readonly GlobalSettingsRepository             _global;
    private readonly TenantDbRollbackReadinessChecker     _checker;
    private readonly SyncService                          _sync;

    public TenantDbRollbackExecutor(
        ILocalDatabasePathProvider pathProvider,
        GlobalSettingsRepository global,
        TenantDbRollbackReadinessChecker checker,
        SyncService sync)
    {
        _pathProvider = pathProvider;
        _global       = global;
        _checker      = checker;
        _sync         = sync;
    }

    public async System.Threading.Tasks.Task<TenantDbRollbackResult> ExecuteAsync(
        TenantDbRollbackOptions options,
        System.Threading.CancellationToken ct = default)
    {
        if (options is null) throw new System.ArgumentNullException(nameof(options));

        var started = System.DateTime.UtcNow;
        var report  = _checker.Check();
        var phraseAccepted = string.Equals(
            options.ConfirmationPhrase,
            RequiredConfirmationPhrase,
            System.StringComparison.Ordinal);

        // 1. Runtime mode already off → NoOp regardless of dry-run / force.
        if (report.Status == TenantDbRollbackReadinessStatus.NotInTenantRuntimeMode)
        {
            return WithAuditLog(options, BaseResult(
                started, report, phraseAccepted,
                TenantDbRollbackOutcome.NoOp,
                dryRun: options.DryRunOnly,
                failureReason: "Runtime tenant DB mode is already off — rollback is not required.",
                steps: report.RecommendedSteps));
        }

        // 2. Readiness checker refused (legacy missing AND no backup) →
        //    Rejected even for dry-runs because there is no safe plan.
        if (!report.CanRollback)
        {
            return WithAuditLog(options, BaseResult(
                started, report, phraseAccepted,
                TenantDbRollbackOutcome.Rejected,
                dryRun: options.DryRunOnly,
                failureReason: "Rollback readiness checker returned " + report.Status +
                               ": " + string.Join("; ", report.Warnings),
                steps: report.RecommendedSteps));
        }

        // ── DRY-RUN PATH ─────────────────────────────────────────────────────
        // Dry-run never returns Success. Either NoOp (handled above) or DryRun.
        if (options.DryRunOnly)
        {
            var planned = new System.Collections.Generic.List<string>();
            planned.Add($"[DRY-RUN] Provider currently legacy={!_pathProvider.IsTenantScoped}. " +
                        "Real rollback requires provider already in legacy mode.");
            if (options.ArchiveTenantsDirectory && Directory.Exists(report.TenantsDirectory))
                planned.Add($"[DRY-RUN] Would rename {report.TenantsDirectory} → <same>.before-rollback-<utc>.");
            else
                planned.Add($"[DRY-RUN] Would leave {report.TenantsDirectory} in place (ArchiveTenantsDirectory=false or directory missing).");
            if (options.DisableRuntimeFlag && report.RuntimeFlagEnabled)
                planned.Add($"[DRY-RUN] Would set {RuntimeFlagKey} = \"0\" in {report.GlobalSettingsPath}.");
            else
                planned.Add($"[DRY-RUN] Would leave {RuntimeFlagKey} unchanged.");
            if (options.RestoreLegacyFromBackupIfMissing &&
                !(report.LegacyDbExists && report.LegacyDbReadable))
                planned.Add($"[DRY-RUN] Would restore {report.LegacyDbPath} from {report.MostRecentBackupPath ?? "<no backup>"}.");

            return WithAuditLog(options, new TenantDbRollbackResult
            {
                Outcome                      = TenantDbRollbackOutcome.DryRun,
                DryRun                       = true,
                LegacyDbPath                 = report.LegacyDbPath,
                TenantsDirectory             = report.TenantsDirectory,
                GlobalSettingsPath           = report.GlobalSettingsPath,
                RuntimeFlagWasEnabled        = report.RuntimeFlagEnabled,
                ConfirmationPhraseAccepted   = phraseAccepted,
                Steps                        = planned,
                StartedAtUtc                 = started,
                CompletedAtUtc               = System.DateTime.UtcNow,
            });
        }

        // ── REAL-RUN GUARDS ──────────────────────────────────────────────────

        // 3. Force=true required.
        if (!options.Force)
        {
            return WithAuditLog(options, BaseResult(
                started, report, phraseAccepted,
                TenantDbRollbackOutcome.Rejected,
                dryRun: false,
                failureReason: "Real tenant DB rollback requires Force=true.",
                steps: report.RecommendedSteps));
        }

        // 4. ConfirmationPhrase must equal RequiredConfirmationPhrase verbatim.
        if (!phraseAccepted)
        {
            return WithAuditLog(options, BaseResult(
                started, report, phraseAccepted,
                TenantDbRollbackOutcome.Rejected,
                dryRun: false,
                failureReason:
                    "Real tenant DB rollback requires the exact ConfirmationPhrase " +
                    "(see TenantDbRollbackExecutor.RequiredConfirmationPhrase). " +
                    "This deliberate double-confirmation prevents accidental rollback.",
                steps: report.RecommendedSteps));
        }

        // 5. Provider must already be in legacy mode. The executor refuses to
        //    flip the provider itself — that's the running session's job via
        //    logout / session expiry / app restart. Mutating the path provider
        //    here would race active repositories / sync.
        if (_pathProvider.IsTenantScoped)
        {
            return WithAuditLog(options, BaseResult(
                started, report, phraseAccepted,
                TenantDbRollbackOutcome.Rejected,
                dryRun: false,
                failureReason:
                    "Rollback executor must be run when the app is not actively using a tenant DB. " +
                    "Stop/restart the app or ensure provider is legacy before rollback.",
                steps: report.RecommendedSteps));
        }

        // 6. Legacy pos.db must be readable, OR
        //    RestoreLegacyFromBackupIfMissing must be true AND a backup exists.
        var legacyOk = report.LegacyDbExists && report.LegacyDbReadable;
        if (!legacyOk && !options.RestoreLegacyFromBackupIfMissing)
        {
            return WithAuditLog(options, BaseResult(
                started, report, phraseAccepted,
                TenantDbRollbackOutcome.Rejected,
                dryRun: false,
                failureReason:
                    "Legacy pos.db is missing or unreadable and " +
                    "RestoreLegacyFromBackupIfMissing=false. Restore manually or " +
                    "pass RestoreLegacyFromBackupIfMissing=true.",
                steps: report.RecommendedSteps));
        }
        if (!legacyOk && options.RestoreLegacyFromBackupIfMissing &&
            string.IsNullOrEmpty(report.MostRecentBackupPath))
        {
            return WithAuditLog(options, BaseResult(
                started, report, phraseAccepted,
                TenantDbRollbackOutcome.Rejected,
                dryRun: false,
                failureReason:
                    "RestoreLegacyFromBackupIfMissing=true but no backup file exists " +
                    "under the backups directory. External recovery required.",
                steps: report.RecommendedSteps));
        }

        // ── REAL-RUN ACTIONS ─────────────────────────────────────────────────
        //
        // Order matters:
        //   a. Restore legacy from backup if needed (so the post-rollback
        //      runtime has a DB to open).
        //   b. Archive tenants\ via rename (preserves all per-tenant DBs).
        //   c. Disable runtime flag (last, so a crash mid-run leaves the
        //      operator with a still-on flag rather than no DB).
        //
        // No sync pause / no provider flip — guarded above. Any partial
        // mutation flagged as Failed in the result.

        var steps = new System.Collections.Generic.List<string>();
        bool flagDisabled = false;
        bool tenantsArchived = false;
        bool legacyRestored = false;
        string? archivedPath = null;
        string? backupUsedPath = null;
        string? brokenLegacyRenamedTo = null;
        string? failure = null;
        TenantDbRollbackOutcome outcome = TenantDbRollbackOutcome.Failed;

        try
        {
            ct.ThrowIfCancellationRequested();

            // a. Restore legacy from backup if needed. Never overwrites a
            //    readable legacy DB.
            if (!legacyOk && options.RestoreLegacyFromBackupIfMissing)
            {
                if (report.LegacyDbExists)
                {
                    // Broken/unreadable file is present; rename it aside.
                    var brokenStamp = System.DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                    brokenLegacyRenamedTo = report.LegacyDbPath + ".broken-" + brokenStamp;
                    File.Move(report.LegacyDbPath, brokenLegacyRenamedTo);
                    steps.Add($"Renamed broken legacy DB to {brokenLegacyRenamedTo}.");
                }
                File.Copy(report.MostRecentBackupPath!, report.LegacyDbPath, overwrite: false);
                backupUsedPath = report.MostRecentBackupPath;
                legacyRestored = true;
                steps.Add($"Restored legacy pos.db from {report.MostRecentBackupPath}.");
            }
            else
            {
                steps.Add("Legacy pos.db is readable — no restore step performed.");
            }

            ct.ThrowIfCancellationRequested();

            // b. Archive tenants\ via rename (never delete).
            if (options.ArchiveTenantsDirectory && Directory.Exists(report.TenantsDirectory))
            {
                var stamp = System.DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                archivedPath = report.TenantsDirectory + ".before-rollback-" + stamp;
                Directory.Move(report.TenantsDirectory, archivedPath);
                tenantsArchived = true;
                steps.Add($"Renamed {report.TenantsDirectory} → {archivedPath}.");
            }
            else if (!Directory.Exists(report.TenantsDirectory))
            {
                steps.Add($"{report.TenantsDirectory} does not exist — no archive performed.");
            }
            else
            {
                steps.Add($"ArchiveTenantsDirectory=false — left {report.TenantsDirectory} in place.");
            }

            ct.ThrowIfCancellationRequested();

            // c. Disable runtime flag. Sets to "0" rather than removing the key
            //    so the historical signal is preserved. shared_to_tenant_migrated_at
            //    and other settings are intentionally untouched.
            if (options.DisableRuntimeFlag && report.RuntimeFlagEnabled)
            {
                _global.Set(RuntimeFlagKey, "0");
                flagDisabled = true;
                steps.Add($"Set {RuntimeFlagKey} = \"0\" in {report.GlobalSettingsPath}.");
            }
            else if (!options.DisableRuntimeFlag)
            {
                steps.Add($"DisableRuntimeFlag=false — left {RuntimeFlagKey} unchanged.");
            }
            else
            {
                steps.Add($"{RuntimeFlagKey} was not enabled — no global settings change.");
            }

            outcome = TenantDbRollbackOutcome.Success;
        }
        catch (System.Exception ex)
        {
            failure = ex.Message;
            outcome = TenantDbRollbackOutcome.Failed;
        }

        return WithAuditLog(options, new TenantDbRollbackResult
        {
            Outcome                      = outcome,
            DryRun                       = false,
            FailureReason                = failure,
            LegacyDbPath                 = report.LegacyDbPath,
            TenantsDirectory             = report.TenantsDirectory,
            ArchivedTenantsPath          = archivedPath,
            GlobalSettingsPath           = report.GlobalSettingsPath,
            BackupUsedPath               = backupUsedPath,
            BrokenLegacyRenamedTo        = brokenLegacyRenamedTo,
            RuntimeFlagWasEnabled        = report.RuntimeFlagEnabled,
            RuntimeFlagDisabledByThisRun = flagDisabled,
            TenantsDirectoryArchived     = tenantsArchived,
            LegacyRestoredFromBackup     = legacyRestored,
            ConfirmationPhraseAccepted   = phraseAccepted,
            Steps                        = steps,
            StartedAtUtc                 = started,
            CompletedAtUtc               = System.DateTime.UtcNow,
        });
    }

    private static TenantDbRollbackResult BaseResult(
        System.DateTime started,
        TenantDbRollbackReadinessReport report,
        bool phraseAccepted,
        TenantDbRollbackOutcome outcome,
        bool dryRun,
        string? failureReason,
        System.Collections.Generic.IReadOnlyList<string> steps)
        => new()
        {
            Outcome                    = outcome,
            DryRun                     = dryRun,
            FailureReason              = failureReason,
            LegacyDbPath               = report.LegacyDbPath,
            TenantsDirectory           = report.TenantsDirectory,
            GlobalSettingsPath         = report.GlobalSettingsPath,
            RuntimeFlagWasEnabled      = report.RuntimeFlagEnabled,
            ConfirmationPhraseAccepted = phraseAccepted,
            Steps                      = steps,
            StartedAtUtc               = started,
            CompletedAtUtc             = System.DateTime.UtcNow,
        };

    // ── Audit log writer ─────────────────────────────────────────────────────
    //
    // Writes when options.WriteAuditLog=true. Builds a strict shape that
    // includes only ConfirmationPhraseAccepted (boolean), never the raw
    // phrase. Runs through MigrationAuditLogger.RedactSecrets so any token /
    // DPAPI blob / sensitive-key value that might appear in failure messages
    // is scrubbed before disk. Audit write failures are swallowed.
    private static TenantDbRollbackResult WithAuditLog(
        TenantDbRollbackOptions options,
        TenantDbRollbackResult result)
    {
        if (!options.WriteAuditLog) return result;

        try
        {
            var logDir = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "PosSystem", "logs", "rollbacks");
            Directory.CreateDirectory(logDir);

            var stamp = System.DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
            var path  = Path.Combine(logDir, $"rollback-{stamp}-{result.Outcome}.json");

            // Sanitized options view — ConfirmationPhrase is replaced with a
            // boolean ConfirmationPhraseProvided. The raw value never leaves
            // this method.
            var safeOptions = new
            {
                options.DryRunOnly,
                options.Force,
                ConfirmationPhraseProvided      = !string.IsNullOrEmpty(options.ConfirmationPhrase),
                options.ArchiveTenantsDirectory,
                options.DisableRuntimeFlag,
                options.RestoreLegacyFromBackupIfMissing,
                options.WriteAuditLog,
            };

            var entry = new
            {
                TimestampUtc = System.DateTime.UtcNow,
                MachineName  = System.Environment.MachineName,
                OsUser       = System.Environment.UserName,
                Options      = safeOptions,
                Result       = result,
            };

            var raw  = JsonSerializer.Serialize(entry,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                });
            var safe = MigrationAuditLogger.RedactSecrets(raw);

            var tmp  = path + ".tmp";
            File.WriteAllText(tmp, safe);
            if (File.Exists(path)) File.Replace(tmp, path, destinationBackupFileName: null);
            else File.Move(tmp, path);

            result.AuditLogPath = path;
        }
        catch
        {
            // best effort
        }

        return result;
    }
}
