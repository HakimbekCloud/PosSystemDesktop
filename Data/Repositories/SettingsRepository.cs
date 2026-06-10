using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PosSystem.Core.Entities;

namespace PosSystem.Data.Repositories;

public class SettingsRepository(IDbContextFactory<AppDbContext> factory)
{
    // Bug M7: bearer/refresh tokens used to sit as plaintext rows in the SQLite
    // Settings table on shared cashier terminals. These keys are now transparently
    // encrypted at rest with Windows DPAPI (CurrentUser scope) and stored in the
    // marked format "dpapi:<base64>". Encryption is applied on Set and reversed on
    // Get for these keys only; every other setting is stored as-is.
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.Ordinal)
    {
        "auth_token",
        "refresh_token"
    };

    private const string DpapiPrefix = "dpapi:";

    public string? Get(string key)
    {
        using var db = factory.CreateDbContext();
        var stored = db.Settings.FirstOrDefault(s => s.Key == key)?.Value;
        if (stored is null) return null;

        if (!SensitiveKeys.Contains(key)) return stored;

        // Legacy plaintext (written by an older build, or stored on non-Windows):
        // return as-is — it becomes encrypted on the next Set.
        if (!stored.StartsWith(DpapiPrefix, StringComparison.Ordinal)) return stored;

        // Decryption failure (corrupt blob, or written by another OS user) → treat
        // as missing so the app cleanly falls back to the login screen, no crash.
        return TryDecrypt(stored[DpapiPrefix.Length..]);
    }

    public void Set(string key, string value)
    {
        var toStore = SensitiveKeys.Contains(key) ? Protect(value) : value;

        using var db = factory.CreateDbContext();
        var existing = db.Settings.Find(key);
        if (existing is null)
            db.Settings.Add(new AppSetting { Key = key, Value = toStore });
        else
            existing.Value = toStore;
        db.SaveChanges();
    }

    public void Remove(string key)
    {
        using var db = factory.CreateDbContext();
        var existing = db.Settings.Find(key);
        if (existing is null) return;
        db.Settings.Remove(existing);
        db.SaveChanges();
    }

    // Encrypt with DPAPI (CurrentUser scope) → "dpapi:<base64>". On non-Windows
    // (only the macOS build machine — production is Windows-only) store plaintext
    // so nothing crashes. If encryption itself fails, fall back to plaintext rather
    // than losing the token.
    private static string Protect(string plaintext)
    {
        if (!OperatingSystem.IsWindows()) return plaintext;
        try
        {
            var bytes     = Encoding.UTF8.GetBytes(plaintext);
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return DpapiPrefix + System.Convert.ToBase64String(encrypted);
        }
        catch
        {
            return plaintext;
        }
    }

    // Reverse of Protect. Returns null on any failure so the caller treats the
    // value as missing (Bug M7 robustness rule).
    private static string? TryDecrypt(string base64)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var encrypted = System.Convert.FromBase64String(base64);
            var bytes     = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }
}
