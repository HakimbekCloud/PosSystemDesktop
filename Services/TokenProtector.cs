using System.Security.Cryptography;
using System.Text;

namespace PosSystem.Services;

// Thin wrapper around Windows DPAPI. CurrentUser scope means the encrypted
// blob can only be unprotected by the same Windows account that wrote it —
// copying pos.db to another profile or machine leaves the tokens unreadable,
// which is the goal. No hardcoded key, no app-managed key file.
internal static class TokenProtector
{
    // Versioned prefix lets us distinguish encrypted blobs from legacy plaintext
    // tokens and from any future v2 (e.g. AES-GCM with a keyring). Anything not
    // starting with a known prefix is treated as legacy plaintext.
    private const string PrefixV1 = "enc:v1:";

    public static string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return "";
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var sealedBytes = ProtectedData.Protect(bytes, optionalEntropy: null,
            scope: DataProtectionScope.CurrentUser);
        return PrefixV1 + Convert.ToBase64String(sealedBytes);
    }

    // Returns:
    //   plaintext  → successfully unwrapped, or already-plaintext legacy value.
    //   null       → blob exists but DPAPI unwrap failed (different Windows user,
    //                profile change, corruption). Caller should treat as "no
    //                session" and discard the corrupted value.
    //   ""         → input was empty/null.
    public static string? TryUnprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        if (!stored.StartsWith(PrefixV1, StringComparison.Ordinal))
            return stored; // legacy plaintext — return verbatim so caller can re-encrypt

        try
        {
            var b64 = stored.Substring(PrefixV1.Length);
            var sealedBytes = Convert.FromBase64String(b64);
            var bytes = ProtectedData.Unprotect(sealedBytes, optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }

    public static bool IsEncrypted(string? stored) =>
        !string.IsNullOrEmpty(stored) && stored.StartsWith(PrefixV1, StringComparison.Ordinal);
}
