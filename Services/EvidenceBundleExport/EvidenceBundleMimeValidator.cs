using System;
using System.IO;
using System.Text;

namespace PosSystem.Services.EvidenceBundleExport;

// Phase 10.22E desktop magic / MIME-vs-extension validator. Mirrors
// the backend EvidenceBundleMimeValidator (Phase 10.22D) exactly so a
// PNG/JPEG/PDF we accept here will also pass the backend's check.
//
// Rules:
//   .png        → must start with PNG signature 89 50 4E 47 0D 0A 1A 0A
//   .jpg/.jpeg  → must start with JPEG SOI marker FF D8 FF
//   .pdf        → must start with %PDF-
//   .md/.txt/.json/.csv/.log → must NOT start with a PNG/JPEG/PDF
//     magic header AND must decode as strict UTF-8 over the probed prefix
//   any other allowed extension → defensive no-op
internal static class EvidenceBundleMimeValidator
{
    public const int SniffBytes = 16;

    private static readonly byte[] PngSig = new byte[]
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
    };

    private static readonly byte[] JpegSoi = new byte[] { 0xFF, 0xD8, 0xFF };

    private static readonly byte[] PdfHead = new byte[] { (byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-' };

    public enum Outcome { Ok, MimeMismatch, DecodingFailure }

    public readonly record struct Result(Outcome Outcome, string SafeMessage)
    {
        public bool Ok => Outcome == Outcome.Ok;
    }

    public static Result Validate(string relativePath, byte[] head)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        ArgumentNullException.ThrowIfNull(head);

        var lower = relativePath.ToLowerInvariant();
        var dot = lower.LastIndexOf('.');
        if (dot < 0) return new(Outcome.Ok, "OK");
        var ext = lower[(dot + 1)..];

        switch (ext)
        {
            case "png":
                return StartsWith(head, PngSig)
                    ? new(Outcome.Ok, "OK")
                    : new(Outcome.MimeMismatch,
                        "File extension '.png' does not match content (missing PNG signature).");

            case "jpg":
            case "jpeg":
                return StartsWith(head, JpegSoi)
                    ? new(Outcome.Ok, "OK")
                    : new(Outcome.MimeMismatch,
                        $"File extension '.{ext}' does not match content (missing JPEG SOI).");

            case "pdf":
                return StartsWith(head, PdfHead)
                    ? new(Outcome.Ok, "OK")
                    : new(Outcome.MimeMismatch,
                        "File extension '.pdf' does not match content (missing '%PDF-' header).");

            case "md":
            case "txt":
            case "json":
            case "csv":
            case "log":
                if (StartsWith(head, PngSig) || StartsWith(head, JpegSoi) || StartsWith(head, PdfHead))
                    return new(Outcome.MimeMismatch,
                        $"Text-like extension '.{ext}' does not match content (looks like a binary file).");
                if (!IsLikelyUtf8(head))
                    return new(Outcome.DecodingFailure,
                        $"Text-like extension '.{ext}' is not valid UTF-8.");
                return new(Outcome.Ok, "OK");

            default:
                return new(Outcome.Ok, "OK");
        }
    }

    /// <summary>
    /// Reads up to <see cref="SniffBytes"/> bytes from <paramref name="path"/>
    /// without throwing on short files. Returns the bytes actually read.
    /// </summary>
    public static byte[] ReadHead(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buf = new byte[SniffBytes];
        var read = 0;
        while (read < buf.Length)
        {
            var r = fs.Read(buf, read, buf.Length - read);
            if (r <= 0) break;
            read += r;
        }
        if (read == buf.Length) return buf;
        var trimmed = new byte[read];
        Array.Copy(buf, trimmed, read);
        return trimmed;
    }

    private static bool StartsWith(byte[] data, byte[] sig)
    {
        if (data.Length < sig.Length) return false;
        for (int i = 0; i < sig.Length; i++)
            if (data[i] != sig[i]) return false;
        return true;
    }

    private static bool IsLikelyUtf8(byte[] data)
    {
        if (data.Length == 0) return true;
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        try
        {
            _ = utf8.GetString(data);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
