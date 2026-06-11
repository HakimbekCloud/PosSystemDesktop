using System.IO;
using System.Text.Json;

namespace PosSystem.Services;

// ── Options / Result DTOs (Phase 10.11C) ─────────────────────────────────────

public sealed class TenantDatabaseInventoryExportOptions
{
    // Override output directory. Null → %LocalAppData%\PosSystem\logs\inventory\
    public string? OutputDirectory    { get; init; }

    public bool   IncludeMachineInfo  { get; init; } = true;
}

public sealed class TenantDatabaseInventoryExportResult
{
    public bool   Success           { get; init; }
    public string? FilePath         { get; init; }
    public System.DateTime StartedAtUtc   { get; init; }
    public System.DateTime CompletedAtUtc { get; init; }

    // Always true on a successful export. The service unconditionally pipes
    // its JSON through MigrationAuditLogger.RedactSecrets plus a literal scrub
    // of the rollback confirmation phrases (defense-in-depth).
    public bool   RedactionApplied  { get; init; }

    public System.Collections.Generic.List<string> Warnings { get; init; } = new();
    public System.Collections.Generic.List<string> Errors   { get; init; } = new();
}

// ── Payload DTO (serialized to JSON) ─────────────────────────────────────────

public sealed class TenantDatabaseInventoryExportPayload
{
    public System.DateTime GeneratedAtUtc { get; init; } = System.DateTime.UtcNow;

    public TenantDatabaseInventoryReport?         Inventory        { get; init; }
    public TenantDatabaseRetentionPreviewReport?  RetentionPreview { get; init; }

    public string Summary                  { get; init; } = "";
    public int    CleanupCandidateCount    { get; init; }
    public string CleanupCandidateSizeText { get; init; } = "";
    public int    ProtectedItemCount       { get; init; }
    public string ProtectedSizeText        { get; init; } = "";

    public System.Collections.Generic.List<string> Warnings { get; init; } = new();
    public System.Collections.Generic.List<string> Errors   { get; init; } = new();

    public object? MachineInfo { get; init; }
}

// ── Service ──────────────────────────────────────────────────────────────────

// Writes one redacted snapshot of the tenant DB inventory + retention preview
// to a JSON file. Intended as the artifact an operator/support engineer
// attaches to a storage/cleanup ticket. The export is the ONLY mutation this
// service performs — every other input comes from read-only / preview-only
// services.
//
// What this service does:
//   • Calls TenantDatabaseInventoryService.GetInventoryAsync (read-only).
//   • Calls TenantDatabaseRetentionPreviewService.PreviewAsync (read-only;
//     internally walks the inventory again).
//   • Composes a single payload, serializes it, redacts secrets, scrubs the
//     rollback confirmation phrases.
//   • Writes one file under %LocalAppData%\PosSystem\logs\inventory\ via
//     temp-file + atomic rename. Filename collisions append "-1", "-2", …
//
// What this service NEVER does:
//   • Execute migration (SharedToTenantDatabaseMigrator is not injected).
//   • Execute rollback (TenantDbRollbackExecutor is not injected).
//   • Switch the path provider (TenantScopeService is not injected).
//   • Mutate global_settings.json, the active DB, or any tenant DB.
//   • Delete, move, rename, or copy any file other than the export's
//     temp-to-final atomic rename.
//   • Serialize raw tokens, JWTs, DPAPI blobs, or the rollback confirmation
//     phrase — these are either structurally absent from the payload DTOs,
//     scrubbed by RedactSecrets, or scrubbed by the literal-phrase pass.
public sealed class TenantDatabaseInventoryExportService
{
    private const string DefaultSubdirectory = "inventory";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly TenantDatabaseInventoryService        _inventory;
    private readonly TenantDatabaseRetentionPreviewService _retention;

    public TenantDatabaseInventoryExportService(
        TenantDatabaseInventoryService inventory,
        TenantDatabaseRetentionPreviewService retention)
    {
        _inventory = inventory;
        _retention = retention;
    }

    public async System.Threading.Tasks.Task<TenantDatabaseInventoryExportResult> ExportAsync(
        TenantDatabaseInventoryExportOptions options,
        System.Threading.CancellationToken ct = default)
    {
        if (options is null) throw new System.ArgumentNullException(nameof(options));

        var started  = System.DateTime.UtcNow;
        var warnings = new System.Collections.Generic.List<string>();
        var errors   = new System.Collections.Generic.List<string>();

        try
        {
            // 1. Collect inventory (best-effort).
            TenantDatabaseInventoryReport? inventory = null;
            try
            {
                inventory = await _inventory.GetInventoryAsync(ct);
            }
            catch (System.Exception ex)
            {
                errors.Add($"Inventory: {ex.Message}");
            }

            // 2. Collect retention preview (best-effort). The retention service
            //    internally re-walks the inventory; either side can succeed
            //    or fail independently — record what we get.
            TenantDatabaseRetentionPreviewReport? retention = null;
            try
            {
                retention = await _retention.PreviewAsync(null, ct);
            }
            catch (System.Exception ex)
            {
                errors.Add($"Retention preview: {ex.Message}");
            }

            // 3. Forward subsection warnings/errors so the top-level lists
            //    reflect everything in the embedded reports.
            if (inventory is not null)
            {
                foreach (var w in inventory.Warnings) warnings.Add($"Inventory: {w}");
                foreach (var e in inventory.Errors)   errors.Add($"Inventory: {e}");
            }
            if (retention is not null)
            {
                foreach (var w in retention.Warnings) warnings.Add($"Retention: {w}");
                foreach (var e in retention.Errors)   errors.Add($"Retention: {e}");
            }

            // 4. Compose payload.
            var candidateCount     = retention?.CandidateCount     ?? 0;
            var candidateSize      = retention?.CandidateSizeText  ?? "0 B";
            var protectedItemCount = retention?.ProtectedItemCount ?? 0;
            var protectedSize      = retention?.ProtectedSizeText  ?? "0 B";

            var summary = inventory is null
                ? "Inventory unavailable — see errors."
                : $"Total known size: {inventory.TotalKnownSizeText}; " +
                  $"tenants={inventory.TenantDatabases.Count}; " +
                  $"backups={inventory.BackupFiles.Count}; " +
                  $"archives={inventory.ArchivedTenantDirectories.Count}; " +
                  $"broken-legacy={inventory.BrokenLegacyDbFiles.Count}; " +
                  $"candidates={candidateCount} ({candidateSize}); " +
                  $"protected={protectedItemCount} ({protectedSize})";

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
                warnings.Add("Machine/user info is included in this inventory export.");

            var payload = new TenantDatabaseInventoryExportPayload
            {
                GeneratedAtUtc           = System.DateTime.UtcNow,
                Inventory                = inventory,
                RetentionPreview         = retention,
                Summary                  = summary,
                CleanupCandidateCount    = candidateCount,
                CleanupCandidateSizeText = candidateSize,
                ProtectedItemCount       = protectedItemCount,
                ProtectedSizeText        = protectedSize,
                Warnings                 = new System.Collections.Generic.List<string>(warnings),
                Errors                   = new System.Collections.Generic.List<string>(errors),
                MachineInfo              = machineInfo,
            };

            // 5. Serialize + redact + scrub.
            var rawJson  = JsonSerializer.Serialize(payload, JsonOptions);
            var safeJson = MigrationAuditLogger.RedactSecrets(rawJson);
            safeJson     = ScrubConfirmationPhrases(safeJson);

            // 6. Resolve output dir + filename + write atomically.
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

            var stamp     = started.ToString("yyyyMMdd-HHmmss");
            var basePath  = Path.Combine(outputDir, $"tenant-db-inventory-{stamp}.json");
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
                errors.Add($"Failed to write inventory export: {ioEx.Message}");
                return Failure(started, warnings, errors);
            }

            // Per spec: Success=true once the JSON is on disk, even when the
            // payload itself contains subsection errors. The operator still
            // has a useful artifact to attach to a ticket.
            return new TenantDatabaseInventoryExportResult
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
            errors.Add($"Inventory export failed: {ex.Message}");
            return Failure(started, warnings, errors);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static TenantDatabaseInventoryExportResult Failure(
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

    // Defense-in-depth literal scrub of the rollback confirmation phrases.
    // None of the payload DTOs structurally carry these strings — the
    // inventory and retention services don't touch the executor — but a
    // future field addition could leak them. The current and deprecated
    // phrases are both replaced.
    private static string ScrubConfirmationPhrases(string json)
    {
        if (string.IsNullOrEmpty(json)) return json;
        const string OldPhrase = "I UNDERSTAND TENANT DB ROLLBACK";
        var current = TenantDbRollbackExecutor.RequiredConfirmationPhrase;
        return json
            .Replace(current,   "<redacted-confirmation-phrase>")
            .Replace(OldPhrase, "<redacted-confirmation-phrase>");
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
