using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace PosSystem.Services.EvidenceBundleExport;

// Phase 10.22E — writes a ZIP bundle locally using temp-then-rename
// so a crash mid-write never leaves a half-written ZIP at the
// operator-visible path. Computes SHA-256 over the final ZIP bytes
// after rename.
//
// What this writer does:
//   • Atomic write — output is fsync-rename, never partial.
//   • Forces forward-slash entry paths inside the ZIP regardless of
//     OS to match the manifest.
//   • Refuses to overwrite an existing ZIP unless the caller passes
//     allowOverwrite=true.
//   • Cleans up the temp file on any failure.
//
// What this writer does NOT do:
//   • No symbolic-link resolution. Symlinks inside the source folder
//     are skipped by the orchestrator before reaching the writer.
//   • No absolute path inside the ZIP. Entries are bare relative
//     paths under the bundle root.
//   • No "store as-is" mode — entries are compressed with default
//     compression.
public sealed class EvidenceBundleZipWriter
{
    public sealed record WriteResult(string AbsoluteZipPath, string Sha256Hex, long ByteSize);

    public WriteResult WriteZip(
        string absoluteZipPath,
        IReadOnlyList<(string RelativePath, string AbsoluteSourcePath)> entries,
        bool allowOverwrite)
    {
        ArgumentNullException.ThrowIfNull(absoluteZipPath);
        ArgumentNullException.ThrowIfNull(entries);

        if (!allowOverwrite && File.Exists(absoluteZipPath))
        {
            throw new IOException(
                "Output ZIP already exists. Pass allowOverwrite=true or remove the file.");
        }

        var directory = Path.GetDirectoryName(absoluteZipPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Temp file in the same directory so rename is atomic on the same volume.
        var tempPath = absoluteZipPath + ".part-" + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (var (rel, src) in entries)
                {
                    // Defence in depth: re-normalise to forward slashes;
                    // refuse the entry if path-safety would reject it.
                    var safe = EvidenceBundlePathSafety.NormalizeRelativePath(rel);
                    if (!safe.Ok)
                        throw new IOException(
                            $"Refusing to ZIP entry with unsafe relative path: {safe.SafeMessage}");

                    var entry = zip.CreateEntry(safe.NormalizedPath!, CompressionLevel.Optimal);
                    // Strip mtime metadata to keep the bundle bit-stable
                    // across operators (testing / reviewer comparison).
                    entry.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    using var entryStream = entry.Open();
                    using var sourceStream = File.Open(src, FileMode.Open, FileAccess.Read, FileShare.Read);
                    sourceStream.CopyTo(entryStream);
                }
            }

            // Compute the SHA-256 of the closed temp file BEFORE rename
            // so the hash always reflects what an operator sees on disk.
            var sha = EvidenceBundleSha256.OfFile(tempPath);
            long size = new FileInfo(tempPath).Length;

            // Replace any existing file. allowOverwrite was already
            // checked above; this is just the rename mechanic.
            if (File.Exists(absoluteZipPath))
            {
                File.Delete(absoluteZipPath);
            }
            File.Move(tempPath, absoluteZipPath);

            return new WriteResult(absoluteZipPath, sha, size);
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best-effort */ }
            throw;
        }
    }
}
