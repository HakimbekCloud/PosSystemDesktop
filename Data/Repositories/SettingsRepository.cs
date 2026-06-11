using Microsoft.EntityFrameworkCore;
using PosSystem.Core.Entities;
using PosSystem.Services;

namespace PosSystem.Data.Repositories;

public class SettingsRepository(IDbContextFactory<AppDbContext> factory)
{
    public string? Get(string key)
    {
        using var db = factory.CreateDbContext();
        return db.Settings.FirstOrDefault(s => s.Key == key)?.Value;
    }

    public void Set(string key, string value)
    {
        using var db = factory.CreateDbContext();
        var existing = db.Settings.Find(key);
        if (existing is null)
            db.Settings.Add(new AppSetting { Key = key, Value = value });
        else
            existing.Value = value;
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

    // Sensitive value writer. Always stores DPAPI-protected blob with the
    // enc:v1: prefix.
    public void SetEncrypted(string key, string value)
        => Set(key, TokenProtector.Protect(value));

    // Sensitive value reader. Returns:
    //   plaintext  → on encrypted-and-decrypted or legacy-plaintext values.
    //   null       → key missing, or encrypted blob that DPAPI refused to
    //                unwrap (different Windows user, corruption). In the
    //                second case we also clear the corrupted row so the next
    //                login starts from a clean slate.
    public string? GetDecrypted(string key)
    {
        var raw = Get(key);
        if (string.IsNullOrEmpty(raw)) return null;

        var plain = TokenProtector.TryUnprotect(raw);
        if (plain is null && TokenProtector.IsEncrypted(raw))
        {
            // Encrypted blob exists but cannot be decrypted by this Windows
            // user — discard so subsequent calls don't keep retrying and so
            // the row doesn't masquerade as a valid session.
            Remove(key);
        }
        return plain;
    }

    // One-time upgrade for legacy plaintext tokens written by pre-Phase-9
    // builds. Safe to call on every startup — no-op when the value is already
    // encrypted (or absent).
    public void EncryptIfLegacy(string key)
    {
        var raw = Get(key);
        if (string.IsNullOrEmpty(raw)) return;
        if (TokenProtector.IsEncrypted(raw)) return;
        Set(key, TokenProtector.Protect(raw));
    }
}
