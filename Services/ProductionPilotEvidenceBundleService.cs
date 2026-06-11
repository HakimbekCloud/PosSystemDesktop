using System.IO;
using System.IO.Compression;
using System.Text.Json;
using PosSystem.Data.Repositories;

namespace PosSystem.Services;

// ── Result DTO (Phase 10.17B) ────────────────────────────────────────────────

public sealed class ProductionPilotEvidenceBundleResult
{
    public System.DateTime StartedAtUtc   { get; init; }
    public System.DateTime CompletedAtUtc { get; init; }

    // Success | Failed
    public string  Outcome             { get; init; } = "Failed";
    public string? BundleDirectory     { get; init; }
    public string? BundleZipPath       { get; init; }
    public string? ManifestPath        { get; init; }

    public int     FileCount           { get; init; }
    public long    TotalBytes          { get; init; }
    public string? TotalSizeText       { get; init; }

    public System.Collections.Generic.List<string> IncludedFiles { get; init; } = new();
    public System.Collections.Generic.List<string> Warnings      { get; init; } = new();
    public System.Collections.Generic.List<string> Errors        { get; init; } = new();
    public System.Collections.Generic.List<string> Steps         { get; init; } = new();
}

// ── Service ──────────────────────────────────────────────────────────────────

// Strictly read-only evidence bundler. Composes existing read-only services
// into a single sanitized folder (and optional ZIP) under
// %LocalAppData%\PosSystem\logs\pilot-evidence\. Designed to be the artifact
// an operator/support engineer attaches to a change-management ticket before
// a controlled production pilot.
//
// What this service does:
//   • Calls only read-only services (pilot readiness, diagnostics, verifier,
//     cutover gate, rollback checker, retention preview).
//   • Reads global-settings flags via Get only.
//   • Writes one timestamped folder under logs\pilot-evidence\ with up to
//     eight sanitized JSON files inside.
//   • Optionally zips the folder into a sibling .zip.
//
// What this service NEVER does:
//   • Invoke any guarded executor (migration / runtime cutover / rollback /
//     retention cleanup wrappers are not injected).
//   • Invoke the underlying migrator, rollback executor, or TenantScopeService.
//   • Mutate any setting or any file outside the new evidence directory.
//   • Switch the path provider.
//   • Copy / include pos.db, tenant DB files, backup files, raw logs,
//     global_settings.json, auth tokens, JWTs, DPAPI blobs, or raw
//     confirmation phrases. Only sanitized JSON summaries are emitted.
//   • Auto-logout / auto-restart.
public sealed class ProductionPilotEvidenceBundleService
{
    private const string EvidenceLogSubdir = "pilot-evidence";

    private const string OutcomeSuccess = "Success";
    private const string OutcomeFailed  = "Failed";

    // Phrases scrubbed from every emitted JSON.
    private const string PhraseCleanup            = "EXECUTE_RETENTION_CLEANUP";
    private const string PhraseRollbackWrapper    = "EXECUTE_TENANT_DB_RUNTIME_ROLLBACK";
    private const string PhraseRollbackInner      = "ROLLBACK_TO_LEGACY_POS_DB";
    private const string PhraseRollbackLegacy     = "I UNDERSTAND TENANT DB ROLLBACK";
    private const string PhraseMigration          = "EXECUTE_REAL_TENANT_DB_MIGRATION";
    private const string PhraseRuntime            = "ENABLE_TENANT_DB_RUNTIME_MODE";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ProductionPilotReadinessReportService _pilotReadiness;
    private readonly OperatorDiagnosticsService            _diagnostics;
    private readonly SharedToTenantMigrationVerifier       _verifier;
    private readonly TenantCutoverReadinessGate            _cutoverGate;
    private readonly TenantDbRollbackReadinessChecker      _rollbackChecker;
    private readonly TenantDatabaseRetentionPreviewService _retention;
    private readonly GlobalSettingsRepository              _global;
    private readonly Data.ILocalDatabasePathProvider       _paths;
    private readonly BackendOperatorPermissionSnapshotService _backendPermissionSnapshot;

    public ProductionPilotEvidenceBundleService(
        ProductionPilotReadinessReportService pilotReadiness,
        OperatorDiagnosticsService diagnostics,
        SharedToTenantMigrationVerifier verifier,
        TenantCutoverReadinessGate cutoverGate,
        TenantDbRollbackReadinessChecker rollbackChecker,
        TenantDatabaseRetentionPreviewService retention,
        GlobalSettingsRepository global,
        Data.ILocalDatabasePathProvider paths,
        BackendOperatorPermissionSnapshotService backendPermissionSnapshot)
    {
        _pilotReadiness            = pilotReadiness;
        _diagnostics               = diagnostics;
        _verifier                  = verifier;
        _cutoverGate               = cutoverGate;
        _rollbackChecker           = rollbackChecker;
        _retention                 = retention;
        _global                    = global;
        _backendPermissionSnapshot = backendPermissionSnapshot;
        _paths           = paths;
    }

    public async System.Threading.Tasks.Task<ProductionPilotEvidenceBundleResult> ExportAsync(
        string? tenantSubdomain,
        System.Threading.CancellationToken ct = default)
    {
        var started  = System.DateTime.UtcNow;
        var steps    = new System.Collections.Generic.List<string>();
        var warnings = new System.Collections.Generic.List<string>();
        var errors   = new System.Collections.Generic.List<string>();
        var included = new System.Collections.Generic.List<string>();
        long totalBytes = 0;

        try
        {
            var resolvedTenant = string.IsNullOrWhiteSpace(tenantSubdomain)
                ? _global.Get("last_tenant_subdomain")
                : tenantSubdomain.Trim();

            var legacyDb = _paths.GetLegacyDbPath();
            var baseDir  = Path.GetDirectoryName(legacyDb)
                           ?? Path.Combine(
                                  System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                                  "PosSystem");
            var rootDir  = Path.Combine(baseDir, "logs", EvidenceLogSubdir);
            Directory.CreateDirectory(rootDir);

            var stamp        = started.ToString("yyyyMMddTHHmmssZ");
            var bundleDir    = ResolveNonCollidingPath(Path.Combine(rootDir, $"pilot-evidence-{stamp}"));
            Directory.CreateDirectory(bundleDir);
            steps.Add($"Created bundle directory: {bundleDir}");

            // 1. pilot-readiness.json — runs the Phase 10.17A readiness aggregator.
            totalBytes += await TryWriteJsonAsync(
                "pilot-readiness.json", bundleDir,
                async () => await _pilotReadiness.GenerateAsync(resolvedTenant, ct),
                steps, included, warnings, errors);

            // 2. diagnostics-summary.json — summary-level diagnostics report.
            totalBytes += await TryWriteJsonAsync(
                "diagnostics-summary.json", bundleDir,
                async () =>
                {
                    var d = await _diagnostics.GetReportAsync(resolvedTenant, ct);
                    return BuildDiagnosticsSummary(d);
                },
                steps, included, warnings, errors);

            // 3. migration-verification-summary.json.
            totalBytes += await TryWriteJsonAsync(
                "migration-verification-summary.json", bundleDir,
                async () =>
                {
                    var v = await _verifier.VerifyAsync(ct);
                    return BuildVerificationSummary(v);
                },
                steps, included, warnings, errors);

            // 4. runtime-cutover-readiness-summary.json.
            totalBytes += await TryWriteJsonAsync(
                "runtime-cutover-readiness-summary.json", bundleDir,
                async () =>
                {
                    if (string.IsNullOrWhiteSpace(resolvedTenant))
                        return new { Status = "n/a", Note = "No tenant resolved." };
                    var c = await _cutoverGate.CheckAsync(resolvedTenant, ct);
                    return BuildCutoverSummary(c);
                },
                steps, included, warnings, errors);

            // 5. rollback-readiness-summary.json.
            totalBytes += TryWriteJsonSync(
                "rollback-readiness-summary.json", bundleDir,
                () =>
                {
                    var r = _rollbackChecker.Check();
                    return BuildRollbackSummary(r);
                },
                steps, included, warnings, errors);

            // 6. retention-preview-summary.json.
            totalBytes += await TryWriteJsonAsync(
                "retention-preview-summary.json", bundleDir,
                async () =>
                {
                    var p = await _retention.PreviewAsync(null, ct);
                    return BuildRetentionSummary(p, baseDir);
                },
                steps, included, warnings, errors);

            // 7. runbook-existence-summary.json.
            totalBytes += TryWriteJsonSync(
                "runbook-existence-summary.json", bundleDir,
                () => BuildRunbookExistenceSummary(),
                steps, included, warnings, errors);

            // 8. backend-permission-summary.json — Phase 10.19E.
            totalBytes += await TryWriteJsonAsync(
                "backend-permission-summary.json", bundleDir,
                async () => await _backendPermissionSnapshot.GenerateAsync(resolvedTenant, ct),
                steps, included, warnings, errors);

            // 9. manifest.json — written last so its included-files list is complete.
            var manifestPath = TryWriteManifest(
                bundleDir, started, resolvedTenant, included, warnings, errors, steps);
            if (manifestPath != null)
            {
                var size = SafeFileSize(manifestPath);
                totalBytes += size;
                included.Add("manifest.json");
                steps.Add($"Wrote manifest.json ({FormatSize(size)}).");
            }

            // 10. Optional ZIP.
            string? zipPath = TryCreateZip(rootDir, bundleDir, warnings, steps);

            return new ProductionPilotEvidenceBundleResult
            {
                StartedAtUtc    = started,
                CompletedAtUtc  = System.DateTime.UtcNow,
                Outcome         = OutcomeSuccess,
                BundleDirectory = bundleDir,
                BundleZipPath   = zipPath,
                ManifestPath    = manifestPath,
                FileCount       = included.Count,
                TotalBytes      = totalBytes,
                TotalSizeText   = FormatSize(totalBytes),
                IncludedFiles   = included,
                Warnings        = warnings,
                Errors          = errors,
                Steps           = steps,
            };
        }
        catch (System.Exception ex)
        {
            errors.Add($"Evidence bundle export failed: {ex.Message}");
            return new ProductionPilotEvidenceBundleResult
            {
                StartedAtUtc    = started,
                CompletedAtUtc  = System.DateTime.UtcNow,
                Outcome         = OutcomeFailed,
                IncludedFiles   = included,
                Warnings        = warnings,
                Errors          = errors,
                Steps           = steps,
                TotalSizeText   = FormatSize(totalBytes),
            };
        }
    }

    // ── Summary projections (kept narrow on purpose). ───────────────────────

    private static object BuildDiagnosticsSummary(OperatorDiagnosticsReport? d)
    {
        if (d is null) return new { Note = "Diagnostics report unavailable." };
        return new
        {
            d.CheckedAtUtc,
            d.ActiveDbPath,
            d.IsTenantScoped,
            d.LegacyDbPath,
            d.CurrentTenantSubdomain,
            d.RequestedTenantSubdomain,
            d.SanitizedRequestedTenantSubdomain,
            d.TargetTenantDbPath,
            d.RuntimeTenantDbEnabled,
            d.MigrationFeatureEnabled,
            d.SharedToTenantMigrated,
            d.SharedToTenantMigratedAt,
            d.LastTenantSubdomain,
            d.ApiBaseUrlConfigured,
            Sales = new
            {
                d.Sales.PendingSalesCount,
                d.Sales.PoisonSalesCount,
                d.Sales.FailedRetryableSalesCount,
                d.Sales.SyncedSalesCount,
                d.Sales.TotalSalesCount,
                d.Sales.ErrorMessage,
            },
            Cache = new
            {
                d.Cache.ProductsCount,
                d.Cache.CustomersCount,
                d.Cache.CategoriesCount,
                d.Cache.PriceListsCount,
                d.Cache.ProductTypesCount,
                d.Cache.BootstrapCompleted,
                d.Cache.BootstrapCompletedAt,
                d.Cache.LastProductSyncAt,
                d.Cache.LastCustomerSyncAt,
                d.Cache.LastStockReconcileAt,
                d.Cache.ErrorMessage,
            },
            d.Warnings,
            d.Errors,
        };
    }

    private static object BuildVerificationSummary(MigrationVerificationReport? r)
    {
        if (r is null) return new { Note = "Verification report unavailable." };
        return new
        {
            r.GeneratedAtUtc,
            r.SourceDbExists,
            r.GlobalMarkerPresent,
            r.OrphanCountInSource,
            r.AllVerified,
            Tenants = r.Tenants.Select(t => new
            {
                t.Subdomain,
                t.Verified,
                Issues = t.Issues,
            }).ToList(),
        };
    }

    private static object BuildCutoverSummary(TenantCutoverReadinessReport? r)
    {
        if (r is null) return new { Status = "n/a", Note = "Cutover readiness report unavailable." };
        return new
        {
            Status = r.Status.ToString(),
            r.CanCutOver,
            r.TenantSubdomain,
            r.SanitizedTenantSubdomain,
            r.LegacyDbPath,
            r.TargetDbPath,
            r.RuntimeFeatureEnabled,
            r.GlobalMigrationMarkerPresent,
            r.LegacyDbExists,
            r.TargetDbExists,
            r.MigrationHistoryExists,
            r.PerTenantMarkerExists,
            r.SchemaUpToDate,
            r.ProviderInLegacyMode,
            r.VerifierPassed,
            Warnings = r.Warnings.ToList(),
            Errors   = r.Errors.ToList(),
            r.CheckedAtUtc,
        };
    }

    private static object BuildRollbackSummary(TenantDbRollbackReadinessReport? r)
    {
        if (r is null) return new { Status = "n/a", Note = "Rollback readiness report unavailable." };
        return new
        {
            Status = r.Status.ToString(),
            r.CanRollback,
            r.LegacyDbPath,
            r.LegacyDbExists,
            r.LegacyDbReadable,
            r.LegacyDbSizeBytes,
            r.TenantsDirectory,
            r.TenantsDirectoryExists,
            r.TenantDbCount,
            r.TenantDbs,
            r.BackupsDirectory,
            r.LegacyBackupCount,
            r.MostRecentBackupPath,
            r.GlobalSettingsPath,
            r.RuntimeFlagEnabled,
            r.GlobalMigrationMarkerPresent,
            r.ProviderInTenantMode,
            r.LastTenantSubdomain,
            Warnings         = r.Warnings.ToList(),
            RecommendedSteps = r.RecommendedSteps.ToList(),
            r.GeneratedAtUtc,
        };
    }

    private static object BuildRetentionSummary(
        TenantDatabaseRetentionPreviewReport? r,
        string baseDir)
    {
        if (r is null) return new { Note = "Retention preview report unavailable." };

        // Group candidate/protected counts and bytes by category to avoid
        // emitting every raw absolute path.
        var candByCategory = r.Candidates
            .GroupBy(c => c.Category)
            .Select(g => new
            {
                Category   = g.Key,
                Count      = g.Count(),
                TotalBytes = g.Sum(x => x.SizeBytes),
                TotalSize  = FormatSize(g.Sum(x => x.SizeBytes)),
                // Normalised relative names so the bundle never leaks the
                // raw absolute path on the operator's machine.
                Names      = g.Select(x => Normalize(x.Path, baseDir)).ToList(),
            }).ToList();

        var protByCategory = r.ProtectedItems
            .GroupBy(c => c.Category)
            .Select(g => new
            {
                Category   = g.Key,
                Count      = g.Count(),
                TotalBytes = g.Sum(x => x.SizeBytes),
                TotalSize  = FormatSize(g.Sum(x => x.SizeBytes)),
                Names      = g.Select(x => Normalize(x.Path, baseDir)).ToList(),
            }).ToList();

        return new
        {
            r.CheckedAtUtc,
            r.PreviewOnly,
            r.Summary,
            r.CandidateCount,
            r.CandidateBytes,
            r.CandidateSizeText,
            r.ProtectedItemCount,
            r.ProtectedBytes,
            r.ProtectedSizeText,
            CandidatesByCategory = candByCategory,
            ProtectedByCategory  = protByCategory,
            Warnings             = r.Warnings,
            Errors               = r.Errors,
        };
    }

    private static object BuildRunbookExistenceSummary()
    {
        var docsDir = Path.Combine(System.AppContext.BaseDirectory, "docs");
        string[] runbooks =
        {
            "operator-tenant-db-migration-runbook.md",
            "operator-retention-cleanup-runbook.md",
            "tenant-db-rollback.md",
        };
        return new
        {
            DocsDirectory = docsDir,
            Runbooks = runbooks.Select(name =>
            {
                var p = Path.Combine(docsDir, name);
                var exists = false;
                try { exists = File.Exists(p); } catch { /* ignore */ }
                return new
                {
                    Name   = name,
                    Found  = exists,
                    Status = exists ? "Info" : "Warning",
                    Note   = exists
                        ? "Runbook is deployed alongside the app."
                        : "Runbook not deployed alongside app — operator must read it from the source repo.",
                };
            }).ToList(),
        };
    }

    private string? TryWriteManifest(
        string bundleDir,
        System.DateTime started,
        string? resolvedTenant,
        System.Collections.Generic.List<string> included,
        System.Collections.Generic.List<string> warnings,
        System.Collections.Generic.List<string> errors,
        System.Collections.Generic.List<string> steps)
    {
        try
        {
            // Pull a snapshot of overall status from the pilot-readiness
            // file we just wrote; if it's not parseable, fall back to a
            // neutral string. This avoids re-running the readiness logic.
            string overallStatus = "Unknown";
            var readinessFile = Path.Combine(bundleDir, "pilot-readiness.json");
            if (File.Exists(readinessFile))
            {
                try
                {
                    using var fs = File.OpenRead(readinessFile);
                    using var doc = JsonDocument.Parse(fs);
                    if (doc.RootElement.TryGetProperty("OverallStatus", out var p))
                        overallStatus = p.GetString() ?? "Unknown";
                }
                catch
                {
                    // ignore
                }
            }

            var manifest = new
            {
                GeneratedAtUtc       = System.DateTime.UtcNow,
                BundleDirectoryName  = Path.GetFileName(bundleDir),
                StartedAtUtc         = started,
                TenantSubdomain      = resolvedTenant,
                AppLocalBaseSummary  = "%LocalAppData%\\PosSystem\\",
                IncludedFiles        = included.ToList(),
                OverallStatus        = overallStatus,
                WarningCount         = warnings.Count,
                ErrorCount           = errors.Count,
                ContentSafetyStatement =
                    "This bundle contains only sanitized JSON summaries. " +
                    "It does NOT include pos.db, tenant DB files, backup files, raw logs, " +
                    "global_settings.json, auth tokens, JWTs, DPAPI blobs, or raw confirmation phrases. " +
                    "Confirmation phrases (if any appeared) are replaced with <redacted-confirmation-phrase>. " +
                    "JWTs are replaced with <redacted-jwt>. DPAPI blobs are replaced with <redacted-encrypted>.",
            };
            return WriteSanitizedJson(bundleDir, "manifest.json", manifest);
        }
        catch (System.Exception ex)
        {
            errors.Add($"Failed to write manifest.json: {ex.Message}");
            steps.Add($"manifest.json: FAILED ({ex.Message}).");
            return null;
        }
    }

    private static string? TryCreateZip(
        string rootDir,
        string bundleDir,
        System.Collections.Generic.List<string> warnings,
        System.Collections.Generic.List<string> steps)
    {
        try
        {
            var zipName = Path.GetFileName(bundleDir) + ".zip";
            var zipPath = ResolveNonCollidingPath(Path.Combine(rootDir, zipName));
            ZipFile.CreateFromDirectory(bundleDir, zipPath);
            steps.Add($"Created ZIP: {zipPath}.");
            return zipPath;
        }
        catch (System.Exception ex)
        {
            warnings.Add($"ZIP creation failed (folder is still usable): {ex.Message}");
            return null;
        }
    }

    // ── Sanitized JSON write helpers. ───────────────────────────────────────

    // Returns the file size written (0 on failure). The caller accumulates
    // into its own total; async cannot accept `ref long`.
    private async System.Threading.Tasks.Task<long> TryWriteJsonAsync(
        string fileName,
        string bundleDir,
        System.Func<System.Threading.Tasks.Task<object>> producer,
        System.Collections.Generic.List<string> steps,
        System.Collections.Generic.List<string> included,
        System.Collections.Generic.List<string> warnings,
        System.Collections.Generic.List<string> errors)
    {
        try
        {
            var payload = await producer();
            var path = WriteSanitizedJson(bundleDir, fileName, payload);
            var size = SafeFileSize(path);
            included.Add(fileName);
            steps.Add($"Wrote {fileName} ({FormatSize(size)}).");
            return size;
        }
        catch (System.Exception ex)
        {
            errors.Add($"{fileName}: {ex.Message}");
            steps.Add($"{fileName}: FAILED ({ex.Message}).");
            return 0;
        }
    }

    private long TryWriteJsonSync(
        string fileName,
        string bundleDir,
        System.Func<object> producer,
        System.Collections.Generic.List<string> steps,
        System.Collections.Generic.List<string> included,
        System.Collections.Generic.List<string> warnings,
        System.Collections.Generic.List<string> errors)
    {
        try
        {
            var payload = producer();
            var path = WriteSanitizedJson(bundleDir, fileName, payload);
            var size = SafeFileSize(path);
            included.Add(fileName);
            steps.Add($"Wrote {fileName} ({FormatSize(size)}).");
            return size;
        }
        catch (System.Exception ex)
        {
            errors.Add($"{fileName}: {ex.Message}");
            steps.Add($"{fileName}: FAILED ({ex.Message}).");
            return 0;
        }
    }

    private static string WriteSanitizedJson(string bundleDir, string fileName, object payload)
    {
        var finalPath = Path.Combine(bundleDir, fileName);

        var raw  = JsonSerializer.Serialize(payload, JsonOptions);
        var safe = MigrationAuditLogger.RedactSecrets(raw);
        safe     = ScrubConfirmationPhrases(safe);

        var tmp = finalPath + ".tmp";
        File.WriteAllText(tmp, safe);
        if (File.Exists(finalPath))
            File.Replace(tmp, finalPath, destinationBackupFileName: null);
        else
            File.Move(tmp, finalPath);
        return finalPath;
    }

    private static string ScrubConfirmationPhrases(string json)
    {
        if (string.IsNullOrEmpty(json)) return json;
        return json
            .Replace(PhraseCleanup,         "<redacted-confirmation-phrase>")
            .Replace(PhraseRollbackWrapper, "<redacted-confirmation-phrase>")
            .Replace(PhraseRollbackInner,   "<redacted-confirmation-phrase>")
            .Replace(PhraseRollbackLegacy,  "<redacted-confirmation-phrase>")
            .Replace(PhraseMigration,       "<redacted-confirmation-phrase>")
            .Replace(PhraseRuntime,         "<redacted-confirmation-phrase>");
    }

    private static string ResolveNonCollidingPath(string basePath)
    {
        var dir  = Path.GetDirectoryName(basePath) ?? "";
        var ext  = Path.GetExtension(basePath);
        var name = string.IsNullOrEmpty(ext)
            ? Path.GetFileName(basePath)
            : Path.GetFileNameWithoutExtension(basePath);

        if (!Exists(basePath, ext)) return basePath;

        for (int i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{name}-{i}{ext}");
            if (!Exists(candidate, ext)) return candidate;
        }
        return Path.Combine(dir, $"{name}-{System.DateTime.UtcNow.Ticks}{ext}");

        static bool Exists(string p, string ext)
            => string.IsNullOrEmpty(ext) ? Directory.Exists(p) : File.Exists(p);
    }

    private static long SafeFileSize(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)            return $"{bytes} B";
        if (bytes < 1024L * 1024)    return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    // Normalises an absolute path under <base> to a relative form so the
    // bundle never leaks the operator's user profile in the JSON. Paths
    // outside <base> are left as-is (they're already not user-profile).
    private static string Normalize(string path, string baseDir)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var b    = Path.GetFullPath(baseDir);
            if (full.StartsWith(b, System.StringComparison.OrdinalIgnoreCase))
            {
                var rel = full.Substring(b.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return "%LocalAppData%\\PosSystem\\" + rel;
            }
            return full;
        }
        catch
        {
            return path;
        }
    }
}
