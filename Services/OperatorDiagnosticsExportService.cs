using System.IO;
using System.Text.Json;

namespace PosSystem.Services;

// ── Options / Result DTOs ────────────────────────────────────────────────────

public sealed class OperatorDiagnosticsExportOptions
{
    // Tenant to scope the diagnostics report at. Null → "global" (uses
    // last_tenant_subdomain if present).
    public string? TenantSubdomain        { get; init; }

    // Override output directory. Null → %LocalAppData%\PosSystem\logs\diagnostics\
    public string? OutputDirectory        { get; init; }

    public bool IncludeMachineInfo        { get; init; } = true;

    // Phase 10.7C: RedactSensitiveValues is intentionally absent. Diagnostics
    // exports always run through MigrationAuditLogger.RedactSecrets so support
    // bundles can be shared externally without a per-caller opt-out.
}

public sealed class OperatorDiagnosticsExportResult
{
    public bool   Success          { get; init; }
    public string? FilePath        { get; init; }

    // Always true on a successful export. Phase 10.7C contract: the export
    // service unconditionally redacts before write, so any consumer can rely
    // on this flag being true whenever Success is true.
    public bool   RedactionApplied { get; init; }

    public System.DateTime StartedAtUtc   { get; init; }
    public System.DateTime CompletedAtUtc { get; init; }
    public System.Collections.Generic.List<string> Warnings { get; init; } = new();
    public System.Collections.Generic.List<string> Errors   { get; init; } = new();
}

// ── Service ──────────────────────────────────────────────────────────────────

// Writes a redacted snapshot of the operator diagnostics report to a JSON
// file. Phase 10.7B introduces the service in DI; no production-flow code
// path resolves or invokes it. A future operator UI / CLI / support workflow
// is the only intended caller.
//
// What this service does:
//   • Calls OperatorDiagnosticsService.GetReportAsync (read-only).
//   • Optionally attaches a small machine-info block (machine name, OS, user,
//     app base directory, PID, export timestamp).
//   • Serializes to JSON, optionally runs MigrationAuditLogger.RedactSecrets
//     on the serialized string (defense-in-depth — current report DTOs
//     don't carry tokens, but a future field could).
//   • Writes to a timestamped file under
//     %LocalAppData%\PosSystem\logs\diagnostics\ via temp-file + atomic
//     rename. Collisions append "-1", "-2", … suffixes.
//
// What this service never does:
//   • Run migration, rollback, or any database switch.
//   • Mutate global_settings.json, the active DB, or any tenant DB.
//   • Delete files or directories.
//   • Call TenantDbRollbackExecutor.ExecuteAsync.
//   • Serialize raw auth_token / refresh_token / Authorization / Bearer
//     values / JWTs / DPAPI blobs / rollback confirmation phrase — these are
//     either structurally absent from the diagnostics report shape, scrubbed
//     by RedactSecrets, or both.
public sealed class OperatorDiagnosticsExportService
{
    private const string DefaultSubdirectory = "diagnostics";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly OperatorDiagnosticsService _diagnostics;

    public OperatorDiagnosticsExportService(OperatorDiagnosticsService diagnostics)
    {
        _diagnostics = diagnostics;
    }

    public async System.Threading.Tasks.Task<OperatorDiagnosticsExportResult> ExportAsync(
        OperatorDiagnosticsExportOptions options,
        System.Threading.CancellationToken ct = default)
    {
        if (options is null) throw new System.ArgumentNullException(nameof(options));

        var started = System.DateTime.UtcNow;
        var warnings = new System.Collections.Generic.List<string>();
        var errors   = new System.Collections.Generic.List<string>();

        try
        {
            // 1. Generate diagnostics report (read-only).
            var report = await _diagnostics.GetReportAsync(options.TenantSubdomain, ct);

            // 2. Resolve output directory + ensure exists.
            var outputDir = string.IsNullOrWhiteSpace(options.OutputDirectory)
                ? Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    "PosSystem", "logs", DefaultSubdirectory)
                : options.OutputDirectory;
            Directory.CreateDirectory(outputDir);

            // 3. Build payload.
            var payload = options.IncludeMachineInfo
                ? new
                {
                    machineInfo = new
                    {
                        machineName            = System.Environment.MachineName,
                        osVersion              = System.Environment.OSVersion.VersionString,
                        userName               = System.Environment.UserName,
                        appBaseDirectory       = System.AppContext.BaseDirectory,
                        processId              = System.Environment.ProcessId,
                        exportGeneratedAtUtc   = System.DateTime.UtcNow,
                    },
                    report,
                }
                : (object)new { report };

            // Phase 10.7C: redaction is mandatory. Even if a future caller
            // passes a deserialized older options DTO with an unknown flag,
            // the export still runs through RedactSecrets here.
            var rawJson  = JsonSerializer.Serialize(payload, JsonOptions);
            var safeJson = MigrationAuditLogger.RedactSecrets(rawJson);

            if (options.IncludeMachineInfo)
                warnings.Add("Machine/user info is included in this diagnostics export.");

            // 4. Resolve final filename with collision suffixes.
            var label = SanitizeForFileName(options.TenantSubdomain) ?? "global";
            var stamp = started.ToString("yyyyMMdd-HHmmss");
            var basePath = Path.Combine(outputDir, $"operator-diagnostics-{stamp}-{label}.json");
            var finalPath = ResolveNonCollidingPath(basePath);

            // 5. Atomic temp-file + rename. Failures land in errors, not throws.
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
                errors.Add($"Failed to write export file: {ioEx.Message}");
                return new OperatorDiagnosticsExportResult
                {
                    Success          = false,
                    FilePath         = null,
                    RedactionApplied = false,
                    StartedAtUtc     = started,
                    CompletedAtUtc   = System.DateTime.UtcNow,
                    Warnings         = warnings,
                    Errors           = errors,
                };
            }

            return new OperatorDiagnosticsExportResult
            {
                Success          = true,
                FilePath         = finalPath,
                RedactionApplied = true,
                StartedAtUtc     = started,
                CompletedAtUtc   = System.DateTime.UtcNow,
                Warnings         = warnings,
                Errors           = errors,
            };
        }
        catch (System.Exception ex)
        {
            errors.Add($"Diagnostics export failed: {ex.Message}");
            return new OperatorDiagnosticsExportResult
            {
                Success          = false,
                FilePath         = null,
                RedactionApplied = false,
                StartedAtUtc     = started,
                CompletedAtUtc   = System.DateTime.UtcNow,
                Warnings         = warnings,
                Errors           = errors,
            };
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string? SanitizeForFileName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().ToLowerInvariant();
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        // Belt-and-suspenders: reject path separators / common shell chars
        // that aren't on GetInvalidFileNameChars in some environments.
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
        // Fallback — extremely unlikely; appends ticks to guarantee uniqueness.
        return Path.Combine(dir, $"{name}-{System.DateTime.UtcNow.Ticks}{ext}");
    }
}
