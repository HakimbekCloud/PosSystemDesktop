using System.IO;
using System.Security.Cryptography;

namespace PosSystem.Services.EvidenceBundleExport;

// Phase 10.22E SHA-256 helper. Streaming so 25 MiB files don't load
// into a managed byte[]. Returns lowercase hex matching the backend.
internal static class EvidenceBundleSha256
{
    public static string OfFile(string path)
    {
        using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return OfStream(fs);
    }

    public static string OfStream(Stream stream)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(stream);
        return BytesToHex(bytes);
    }

    private static string BytesToHex(byte[] bytes)
    {
        var sb = new System.Text.StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        return sb.ToString();
    }
}
