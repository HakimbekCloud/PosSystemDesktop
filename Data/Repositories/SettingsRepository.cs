using Microsoft.EntityFrameworkCore;
using PosSystem.Core.Entities;

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
}
