using System;
using System.Collections.Generic;

namespace PosSystem.Services.EvidenceBundleExport;

// Phase 10.22E desktop mirror of the backend Phase 10.22D
// EvidenceBundlePathSafety. Pure functions; no I/O.
//
// One source of truth for relative-path rejection rules across the
// desktop export pipeline (folder validator, manifest generator, ZIP
// writer). Mirrors the backend so a desktop-generated manifest will
// never carry a path the backend would later reject at upload time.
//
// Rejection rules:
//   • blank / null
//   • leading slash or backslash (absolute / drive-relative)
//   • Windows drive-letter prefix (C:\ …)
//   • UNC path (\\server\share …)
//   • ".." traversal segment
//   • single "." segment
//   • empty segments / duplicate slashes / trailing slash
//   • ASCII control characters (NUL, tab, newline, etc.)
//   • > 1024 chars total
//   • > 255 chars per segment
//   • Windows reserved names: CON / PRN / AUX / NUL / COM1-9 / LPT1-9
//   • DB-suffix files: .db / .db-wal / .db-shm
//   • extension in BlockedExtensions (.zip / .exe / .sql / …)
//   • extension NOT in AllowedExtensions
//   • double-extension trick: any intermediate segment matches a
//     blocked extension (e.g. report.sql.txt, screenshot.exe.png)
internal static class EvidenceBundlePathSafety
{
    public const int MaxRelativePathLength = 1024;
    public const int MaxFileNameLength     = 255;

    public static readonly IReadOnlySet<string> AllowedExtensions = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        "md", "txt", "json", "csv", "log",
        "png", "jpg", "jpeg", "pdf",
    };

    public static readonly IReadOnlySet<string> BlockedExtensions = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        // Archive bombs / nested-zip risk.
        "zip", "7z", "rar", "tar", "gz", "tgz", "bz2", "xz",
        // Native executables / scripts.
        "exe", "dll", "bat", "cmd", "ps1", "psm1", "sh", "bash",
        "vbs", "js", "jse", "wsf", "msi", "scr", "com",
        // Database / backup / SQL.
        "db", "sqlite", "sqlite3", "bak", "dump", "sql",
    };

    private static readonly IReadOnlySet<string> DbSuffixes = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase) { ".db", ".db-wal", ".db-shm" };

    private static readonly IReadOnlySet<string> WindowsReservedNames = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        "con", "prn", "aux", "nul",
        "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
    };

    public enum Outcome
    {
        Ok,
        BlankPath,
        TooLong,
        ControlCharacter,
        LeadingSeparator,
        DriveLetter,
        UncPath,
        EmptySegment,
        TrailingSeparator,
        TraversalSegment,
        SegmentTooLong,
        WindowsReservedName,
        DbSuffix,
        BlockedExtension,
        DoubleExtensionTrick,
        MissingExtension,
        DisallowedExtension,
    }

    public readonly record struct Result(
        Outcome Outcome,
        string? NormalizedPath,
        string SafeMessage)
    {
        public bool Ok => Outcome == Outcome.Ok;
    }

    public static Result NormalizeRelativePath(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Reject(Outcome.BlankPath, "relativePath must not be blank.");

        var trimmed = input.Trim();
        if (trimmed.Length > MaxRelativePathLength)
            return Reject(Outcome.TooLong,
                $"relativePath exceeds maximum length ({MaxRelativePathLength}).");

        for (int i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            if (c < 0x20 || c == 0x7f)
                return Reject(Outcome.ControlCharacter,
                    "relativePath contains control characters.");
        }

        if (trimmed.StartsWith('/') || trimmed.StartsWith('\\'))
            return Reject(Outcome.LeadingSeparator,
                "relativePath must not start with a separator.");
        if (trimmed.Length >= 2 && trimmed[1] == ':')
            return Reject(Outcome.DriveLetter,
                "relativePath must not contain a drive letter.");
        if (trimmed.StartsWith(@"\\"))
            return Reject(Outcome.UncPath,
                "relativePath must not be a UNC path.");

        var unified = trimmed.Replace('\\', '/');
        if (unified.Contains("//"))
            return Reject(Outcome.EmptySegment,
                "relativePath must not contain empty segments.");
        if (unified.EndsWith('/'))
            return Reject(Outcome.TrailingSeparator,
                "relativePath must not end with a separator.");

        var segments = unified.Split('/');
        foreach (var seg in segments)
        {
            if (seg.Length == 0 || seg == "." || seg == "..")
                return Reject(Outcome.TraversalSegment,
                    "relativePath must not contain empty / '.' / '..' segments.");
            if (seg.Length > MaxFileNameLength)
                return Reject(Outcome.SegmentTooLong,
                    $"relativePath segment too long (max {MaxFileNameLength}).");

            var bare = StripExtension(seg);
            if (WindowsReservedNames.Contains(bare))
                return Reject(Outcome.WindowsReservedName,
                    "relativePath segment is a Windows reserved name.");
        }

        var last = segments[^1];
        var lower = last.ToLowerInvariant();
        foreach (var dbSuffix in DbSuffixes)
        {
            if (lower.EndsWith(dbSuffix))
                return Reject(Outcome.DbSuffix,
                    "DB-suffixed files are not allowed in evidence bundles.");
        }

        var dot = lower.LastIndexOf('.');
        if (dot < 0 || dot == lower.Length - 1)
            return Reject(Outcome.MissingExtension,
                "relativePath filename must include an allowed extension.");

        var ext = lower[(dot + 1)..];
        if (BlockedExtensions.Contains(ext))
            return Reject(Outcome.BlockedExtension,
                $"File extension '.{ext}' is explicitly blocked in evidence bundles.");

        // Double-extension trick: scan all intermediate segments.
        for (int i = 0; i < lower.Length - 1; i++)
        {
            var innerDot = lower.IndexOf('.', i);
            if (innerDot < 0 || innerDot == lower.Length - 1) break;
            var nextDot = lower.IndexOf('.', innerDot + 1);
            var end = nextDot < 0 ? lower.Length : nextDot;
            if (end <= innerDot + 1) { i = innerDot; continue; }
            var inner = lower[(innerDot + 1)..end];
            if (BlockedExtensions.Contains(inner) && !string.Equals(inner, ext, StringComparison.Ordinal))
                return Reject(Outcome.DoubleExtensionTrick,
                    $"Double-extension trick detected: '.{inner}' inside filename.");
            i = innerDot;
        }

        if (!AllowedExtensions.Contains(ext))
            return Reject(Outcome.DisallowedExtension,
                $"File extension '.{ext}' is not allowed in Phase 10.22E.");

        return new Result(Outcome.Ok, unified, "OK");
    }

    private static Result Reject(Outcome outcome, string safeMessage) =>
        new(outcome, null, safeMessage);

    private static string StripExtension(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot <= 0 ? name : name[..dot];
    }
}
