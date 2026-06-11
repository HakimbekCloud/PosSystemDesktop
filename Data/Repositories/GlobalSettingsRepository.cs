using System.IO;
using System.Text.Json;

namespace PosSystem.Data.Repositories;

// Machine/app-level settings that must be available BEFORE any tenant DB is
// opened (e.g. api_base_url, last-used tenant for login prefill, UI prefs).
// Backed by a plain JSON file so we can read it without loading any DbContext
// — that's the whole point of this layer ahead of per-tenant DB partitioning.
//
// Phase 10.2 introduces this store but keeps tenant business data and
// per-tenant settings inside the existing pos.db. Readers of these keys are
// expected to fall back to the legacy SettingsRepository for forward
// compatibility with existing installs that haven't been migrated yet.
public class GlobalSettingsRepository
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private Dictionary<string, string>? _cache;

    public string FilePath => _filePath;

    public GlobalSettingsRepository()
    {
        var dir = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "PosSystem");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "global_settings.json");
    }

    public string? Get(string key)
    {
        lock (_lock)
        {
            EnsureLoaded();
            return _cache!.TryGetValue(key, out var value) ? value : null;
        }
    }

    public void Set(string key, string value)
    {
        lock (_lock)
        {
            EnsureLoaded();
            _cache![key] = value;
            Save();
        }
    }

    public void Remove(string key)
    {
        lock (_lock)
        {
            EnsureLoaded();
            if (_cache!.Remove(key)) Save();
        }
    }

    public bool ContainsKey(string key)
    {
        lock (_lock)
        {
            EnsureLoaded();
            return _cache!.ContainsKey(key);
        }
    }

    private void EnsureLoaded()
    {
        if (_cache is not null) return;
        if (!File.Exists(_filePath))
        {
            _cache = new Dictionary<string, string>(System.StringComparer.Ordinal);
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            _cache = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                     ?? new Dictionary<string, string>(System.StringComparer.Ordinal);
        }
        catch
        {
            // Corrupt or unreadable file — start over rather than crash. Worst
            // case the cashier re-enters tenant + URL on next login; existing
            // session in pos.db is untouched.
            _cache = new Dictionary<string, string>(System.StringComparer.Ordinal);
        }
    }

    // Atomic write via temp-file + rename so a power loss can't leave the file
    // half-written and unreadable.
    private void Save()
    {
        var json = JsonSerializer.Serialize(_cache,
            new JsonSerializerOptions { WriteIndented = true });
        var tmp = _filePath + ".tmp";
        File.WriteAllText(tmp, json);
        if (File.Exists(_filePath)) File.Replace(tmp, _filePath, destinationBackupFileName: null);
        else File.Move(tmp, _filePath);
    }
}
