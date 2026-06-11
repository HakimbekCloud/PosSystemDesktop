using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PosSystem.Core.DTOs;
using PosSystem.Data.Repositories;

namespace PosSystem.Services.EvidenceBundleExport;

// Phase 10.22E — orchestrates the desktop-side
// validate → redaction-scan → MIME-validate → manifest → ZIP pipeline.
//
// Behaviour contract:
//   • Behind the local default-OFF flag
//     `operator_evidence_bundle_export_ui_enabled`. When OFF every
//     entry point returns a `Disabled` result and touches nothing.
//   • Reads files from the user-selected folder; writes the ZIP +
//     manifest to a user-selected output folder. Never writes to the
//     POS database, the global settings, or any backend.
//   • Calls ZERO backend endpoints. No `OperatorEvidenceBundle*ApiClient`
//     is injected into this service.
//   • Computes per-file SHA-256 server-side (well, desktop-side) and
//     a final bundle SHA-256 over the ZIP bytes.
//   • Excludes hidden / VCS / IDE / build folders from the bundle
//     (`.git`, `.idea`, `.vs`, `bin`, `obj`, `node_modules`, `target`,
//     `out`, `.gradle`, `.next`).
//   • Refuses to include the output ZIP itself (or any previously
//     written ZIP) if it happens to live under the same folder.
//   • Temp file is cleaned up on every failure path; the operator
//     never sees a stale `.part-*.tmp`.
public sealed class EvidenceBundleExportService
{
    public const string LocalFlagKey = "operator_evidence_bundle_export_ui_enabled";

    private const int MaxFileBytesDefault     = 25 * 1024 * 1024;       // 25 MiB
    private const long MaxBundleBytesDefault  = 200L * 1024 * 1024;     // 200 MiB
    private const int MaxFileCountDefault     = 200;

    private static readonly HashSet<string> ExcludedFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".idea", ".vs", "bin", "obj", "node_modules",
        "target", "out", ".gradle", ".next",
    };

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly GlobalSettingsRepository _global;
    private readonly EvidenceBundleRedactionScanner _scanner;
    private readonly EvidenceBundleManifestGenerator _manifestGenerator;
    private readonly EvidenceBundleZipWriter _zipWriter;

    public EvidenceBundleExportService(
        GlobalSettingsRepository global,
        EvidenceBundleRedactionScanner scanner,
        EvidenceBundleManifestGenerator manifestGenerator,
        EvidenceBundleZipWriter zipWriter)
    {
        _global = global;
        _scanner = scanner;
        _manifestGenerator = manifestGenerator;
        _zipWriter = zipWriter;
    }

    public bool IsEnabled() => string.Equals(_global.Get(LocalFlagKey), "1", StringComparison.Ordinal);

    public sealed class EvidenceBundleExportRequest
    {
        public string SourceFolder { get; init; } = "";
        public string OutputFolder { get; init; } = "";

        public string EvidenceType { get; init; } = "";
        public string Environment  { get; init; } = "";
        public string Phase        { get; init; } = "";

        public string? TenantId   { get; init; }
        public string? StoreId    { get; init; }
        public int?    WaveNumber { get; init; }

        public string CreatedBy { get; init; } = "";

        /// <summary>
        /// When false, only the validate/scan steps run; no manifest or ZIP is written.
        /// </summary>
        public bool GenerateZip { get; init; }

        /// <summary>
        /// When true, replaces an existing ZIP at the target path.
        /// </summary>
        public bool AllowOverwrite { get; init; }
    }

    public EvidenceBundleExportResult Run(EvidenceBundleExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsEnabled())
        {
            return Disabled();
        }
        if (string.IsNullOrWhiteSpace(request.SourceFolder)
            || !Directory.Exists(request.SourceFolder))
        {
            return Blocked("Selected evidence folder does not exist.");
        }
        if (request.GenerateZip && string.IsNullOrWhiteSpace(request.OutputFolder))
        {
            return Blocked("Output folder is required when generating the ZIP.");
        }
        if (string.IsNullOrWhiteSpace(request.Phase))
        {
            return Blocked("Phase is required.");
        }
        if (string.IsNullOrWhiteSpace(request.EvidenceType))
        {
            return Blocked("Evidence type is required.");
        }
        if (string.IsNullOrWhiteSpace(request.Environment))
        {
            return Blocked("Environment is required.");
        }

        var sourceRoot = Path.GetFullPath(request.SourceFolder);
        var outputRoot = request.GenerateZip
            ? Path.GetFullPath(request.OutputFolder)
            : sourceRoot;

        // Phase 10.22E always names the bundle after evidenceType + phase + UTC.
        var bundleStem = SanitizeBundleStem(
            $"{request.EvidenceType}-{request.Phase}-{DateTime.UtcNow:yyyyMMddTHHmmssZ}");

        var manifestRelative = "manifest.json";
        var zipRelative      = bundleStem + ".zip";
        var absoluteZipPath  = Path.Combine(outputRoot, zipRelative);

        var validationIssues = new List<EvidenceBundleValidationIssue>();
        var allFindings      = new List<EvidenceBundleRedactionFinding>();
        var acceptedItems    = new List<EvidenceBundleExportFileItem>();
        var entriesForZip    = new List<(string RelativePath, string AbsoluteSourcePath)>();

        long aggregateBytes = 0L;
        bool sawTextLikeFile = false;
        bool textScanPassedForAll = true;

        foreach (var absoluteFile in EnumerateCandidateFiles(sourceRoot, absoluteZipPath))
        {
            var rel = MakeRelative(sourceRoot, absoluteFile);

            // Skip a previously-generated manifest at the root — we will
            // write a fresh one. Refuse the export if the operator placed
            // a manifest.json in a subfolder (ambiguous bundle root).
            if (string.Equals(rel, "manifest.json", StringComparison.OrdinalIgnoreCase))
                continue;
            if (rel.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase))
            {
                validationIssues.Add(new EvidenceBundleValidationIssue(
                    rel, "NestedManifest",
                    "manifest.json is only allowed at the bundle root."));
                continue;
            }

            var safe = EvidenceBundlePathSafety.NormalizeRelativePath(rel);
            if (!safe.Ok)
            {
                validationIssues.Add(new EvidenceBundleValidationIssue(
                    rel, safe.Outcome.ToString(), safe.SafeMessage));
                continue;
            }
            var safePath = safe.NormalizedPath!;

            long size;
            try { size = new FileInfo(absoluteFile).Length; }
            catch (Exception ex)
            {
                validationIssues.Add(new EvidenceBundleValidationIssue(
                    safePath, "FileStatFailed", "Unable to stat candidate file: " + ex.Message));
                continue;
            }
            if (size <= 0)
            {
                validationIssues.Add(new EvidenceBundleValidationIssue(
                    safePath, "EmptyFile", "Empty files are not allowed."));
                continue;
            }
            if (size > MaxFileBytesDefault)
            {
                validationIssues.Add(new EvidenceBundleValidationIssue(
                    safePath, "FileTooLarge",
                    $"File exceeds maximum size ({MaxFileBytesDefault} bytes)."));
                continue;
            }
            if (aggregateBytes + size > MaxBundleBytesDefault)
            {
                validationIssues.Add(new EvidenceBundleValidationIssue(
                    safePath, "BundleTooLarge",
                    $"Bundle would exceed maximum aggregate size ({MaxBundleBytesDefault} bytes)."));
                continue;
            }
            if (acceptedItems.Count + 1 > MaxFileCountDefault)
            {
                validationIssues.Add(new EvidenceBundleValidationIssue(
                    safePath, "FileCountExceeded",
                    $"Bundle would exceed maximum file count ({MaxFileCountDefault})."));
                continue;
            }

            // MIME / magic check.
            var head = EvidenceBundleMimeValidator.ReadHead(absoluteFile);
            var mime = EvidenceBundleMimeValidator.Validate(safePath, head);
            if (!mime.Ok)
            {
                validationIssues.Add(new EvidenceBundleValidationIssue(
                    safePath, mime.Outcome.ToString(), mime.SafeMessage));
                if (mime.Outcome == EvidenceBundleMimeValidator.Outcome.DecodingFailure)
                    textScanPassedForAll = false;
                continue;
            }

            // Redaction scan (text-like only; binary is passthrough).
            var ext = ExtensionOf(safePath);
            var isTextLike = ext is "md" or "txt" or "json" or "csv" or "log";
            EvidenceBundleRedactionScanner.ScanResult? scan = null;
            if (isTextLike)
            {
                sawTextLikeFile = true;
                using var fs = File.Open(absoluteFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                scan = _scanner.Scan(safePath, fs);
                if (scan.Findings.Count > 0)
                {
                    allFindings.AddRange(scan.Findings);
                    textScanPassedForAll = false;
                    continue;
                }
                if (scan.Truncated)
                {
                    validationIssues.Add(new EvidenceBundleValidationIssue(
                        safePath, "ScannerTruncated",
                        $"Text-like file exceeds scanner byte cap ({_scanner.MaxBytes}). " +
                                "Cannot certify redaction; split or shrink the file."));
                    textScanPassedForAll = false;
                    continue;
                }
            }

            // SHA-256 + accept.
            string sha;
            try { sha = EvidenceBundleSha256.OfFile(absoluteFile); }
            catch (Exception ex)
            {
                validationIssues.Add(new EvidenceBundleValidationIssue(
                    safePath, "Sha256Failed", "Failed to compute SHA-256: " + ex.Message));
                continue;
            }
            acceptedItems.Add(new EvidenceBundleExportFileItem(safePath, size, sha, isTextLike));
            entriesForZip.Add((safePath, absoluteFile));
            aggregateBytes += size;
        }

        // Block-vs-validate-only routing.
        if (validationIssues.Count > 0 || allFindings.Count > 0)
        {
            return new EvidenceBundleExportResult(
                Outcome: "Blocked",
                StatusMessage: BuildBlockedSummary(validationIssues, allFindings),
                FileCount: acceptedItems.Count,
                TotalBytes: aggregateBytes,
                ManifestRelativePath: null,
                ZipRelativePath: null,
                BundleSha256Hex: null,
                Files: acceptedItems,
                ValidationIssues: validationIssues,
                RedactionFindings: allFindings,
                GeneratedArtifactRelativePaths: Array.Empty<string>());
        }

        if (acceptedItems.Count == 0)
        {
            return Blocked("No safe evidence files found in the selected folder.");
        }

        if (!request.GenerateZip)
        {
            return new EvidenceBundleExportResult(
                Outcome: "ValidationOnly",
                StatusMessage:
                    $"Validated {acceptedItems.Count} file(s); no manifest or ZIP written yet.",
                FileCount: acceptedItems.Count,
                TotalBytes: aggregateBytes,
                ManifestRelativePath: null,
                ZipRelativePath: null,
                BundleSha256Hex: null,
                Files: acceptedItems,
                ValidationIssues: validationIssues,
                RedactionFindings: allFindings,
                GeneratedArtifactRelativePaths: Array.Empty<string>());
        }

        // Generate manifest in a temp folder under the OS temp area, then
        // ZIP both manifest + accepted files. Write the ZIP atomically to
        // the output folder.
        string tempBundleDir = Path.Combine(
            Path.GetTempPath(),
            "operator-evidence-bundle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempBundleDir);

        try
        {
            var manifest = _manifestGenerator.Build(
                phase: request.Phase.Trim(),
                evidenceType: request.EvidenceType.Trim(),
                environment: request.Environment.Trim(),
                tenantId: request.TenantId,
                storeId: request.StoreId,
                waveNumber: request.WaveNumber,
                createdBy: request.CreatedBy,
                includedFiles: acceptedItems,
                redactionPassedForAllText: sawTextLikeFile ? textScanPassedForAll : true);

            var manifestJson = JsonSerializer.Serialize(manifest, ManifestJsonOptions);
            var manifestAbsolute = Path.Combine(tempBundleDir, manifestRelative);
            File.WriteAllText(manifestAbsolute, manifestJson, new UTF8Encoding(false));

            // Compose ZIP entries: manifest first, then all accepted files
            // in stable sorted order so identical inputs produce identical
            // bundles.
            var zipEntries = new List<(string, string)>(entriesForZip.Count + 1)
            {
                (manifestRelative, manifestAbsolute),
            };
            zipEntries.AddRange(entriesForZip.OrderBy(e => e.RelativePath, StringComparer.Ordinal));

            var zipResult = _zipWriter.WriteZip(absoluteZipPath, zipEntries, request.AllowOverwrite);

            return new EvidenceBundleExportResult(
                Outcome: "Generated",
                StatusMessage:
                    $"Generated bundle with {acceptedItems.Count} file(s); " +
                    $"ZIP {FormatBytes(zipResult.ByteSize)}.",
                FileCount: acceptedItems.Count,
                TotalBytes: aggregateBytes,
                ManifestRelativePath: manifestRelative,
                ZipRelativePath: zipRelative,
                BundleSha256Hex: zipResult.Sha256Hex,
                Files: acceptedItems,
                ValidationIssues: validationIssues,
                RedactionFindings: allFindings,
                GeneratedArtifactRelativePaths: new[] { manifestRelative, zipRelative });
        }
        catch (Exception ex)
        {
            return new EvidenceBundleExportResult(
                Outcome: "Failed",
                StatusMessage: "Bundle generation failed: " + ex.Message,
                FileCount: acceptedItems.Count,
                TotalBytes: aggregateBytes,
                ManifestRelativePath: null,
                ZipRelativePath: null,
                BundleSha256Hex: null,
                Files: acceptedItems,
                ValidationIssues: validationIssues,
                RedactionFindings: allFindings,
                GeneratedArtifactRelativePaths: Array.Empty<string>());
        }
        finally
        {
            try { Directory.Delete(tempBundleDir, recursive: true); }
            catch { /* best-effort temp cleanup */ }
        }
    }

    private static IEnumerable<string> EnumerateCandidateFiles(string sourceRoot, string excludedAbsoluteZip)
    {
        // Manual recursion so we can skip excluded folder names without
        // pulling a full file list into memory first.
        var stack = new Stack<string>();
        stack.Push(sourceRoot);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            string[] subDirs;
            try { subDirs = Directory.GetDirectories(dir); }
            catch { continue; }
            foreach (var sub in subDirs)
            {
                var name = Path.GetFileName(sub);
                if (string.IsNullOrEmpty(name) || ExcludedFolderNames.Contains(name)) continue;
                // Skip OS-hidden / system dirs at the root level.
                try
                {
                    var attrs = File.GetAttributes(sub);
                    if ((attrs & FileAttributes.Hidden) != 0
                        || (attrs & FileAttributes.System) != 0) continue;
                }
                catch { /* best-effort */ }
                stack.Push(sub);
            }

            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch { continue; }
            foreach (var f in files)
            {
                // Never include the destination ZIP itself.
                if (string.Equals(Path.GetFullPath(f), excludedAbsoluteZip, StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    var attrs = File.GetAttributes(f);
                    if ((attrs & FileAttributes.Hidden) != 0
                        || (attrs & FileAttributes.System) != 0
                        || (attrs & FileAttributes.ReparsePoint) != 0) continue;
                }
                catch { /* best-effort */ }
                yield return f;
            }
        }
    }

    private static string MakeRelative(string rootAbsolute, string fileAbsolute)
    {
        var rel = Path.GetRelativePath(rootAbsolute, fileAbsolute);
        return rel.Replace('\\', '/');
    }

    private static string ExtensionOf(string relativePath)
    {
        var dot = relativePath.LastIndexOf('.');
        return dot < 0 ? "" : relativePath[(dot + 1)..].ToLowerInvariant();
    }

    private static string SanitizeBundleStem(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.') sb.Append(c);
            else sb.Append('-');
        }
        // Collapse runs of dashes.
        var s = sb.ToString();
        while (s.Contains("--")) s = s.Replace("--", "-");
        return s.Trim('-');
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KiB";
        return $"{bytes / 1024.0 / 1024.0:F2} MiB";
    }

    private static string BuildBlockedSummary(
        IReadOnlyList<EvidenceBundleValidationIssue> issues,
        IReadOnlyList<EvidenceBundleRedactionFinding> findings)
    {
        var parts = new List<string>(2);
        if (issues.Count > 0) parts.Add($"{issues.Count} validation issue(s)");
        if (findings.Count > 0) parts.Add($"{findings.Count} redaction finding(s)");
        return parts.Count == 0
            ? "Bundle blocked."
            : "Bundle blocked: " + string.Join(", ", parts) + ".";
    }

    private static EvidenceBundleExportResult Disabled()
    {
        return new EvidenceBundleExportResult(
            Outcome: "Disabled",
            StatusMessage: $"Local export UI is disabled (set {LocalFlagKey}=1 to enable).",
            FileCount: 0,
            TotalBytes: 0,
            ManifestRelativePath: null,
            ZipRelativePath: null,
            BundleSha256Hex: null,
            Files: Array.Empty<EvidenceBundleExportFileItem>(),
            ValidationIssues: Array.Empty<EvidenceBundleValidationIssue>(),
            RedactionFindings: Array.Empty<EvidenceBundleRedactionFinding>(),
            GeneratedArtifactRelativePaths: Array.Empty<string>());
    }

    private static EvidenceBundleExportResult Blocked(string reason)
    {
        return new EvidenceBundleExportResult(
            Outcome: "Blocked",
            StatusMessage: reason,
            FileCount: 0,
            TotalBytes: 0,
            ManifestRelativePath: null,
            ZipRelativePath: null,
            BundleSha256Hex: null,
            Files: Array.Empty<EvidenceBundleExportFileItem>(),
            ValidationIssues: Array.Empty<EvidenceBundleValidationIssue>(),
            RedactionFindings: Array.Empty<EvidenceBundleRedactionFinding>(),
            GeneratedArtifactRelativePaths: Array.Empty<string>());
    }
}
