using System.Collections.ObjectModel;
using System.Windows;

namespace PosSystem.Services;

public class NetworkLogEntry
{
    public DateTime Timestamp    { get; init; }
    public string   Method       { get; init; } = "";
    public string   Url          { get; init; } = "";
    public int      StatusCode   { get; init; }
    public long     DurationMs   { get; init; }
    public string   RequestBody  { get; init; } = "";
    public string   ResponseBody { get; init; } = "";

    public bool   IsError     => StatusCode >= 400 || StatusCode == 0;
    public string StatusLabel => StatusCode == 0 ? "ERR" : StatusCode.ToString();
    public string TimeLabel   => Timestamp.ToString("HH:mm:ss.fff");
    public string DurationLabel => $"{DurationMs} ms";

    // Short URL shown in the list (strip host prefix)
    public string ShortUrl
    {
        get
        {
            if (Uri.TryCreate(Url, UriKind.Absolute, out var u))
                return u.PathAndQuery;
            return Url;
        }
    }
}

public class NetworkLogService
{
    public ObservableCollection<NetworkLogEntry> Entries { get; } = [];

    public void Add(NetworkLogEntry entry)
    {
        if (Application.Current is null)
        {
            Entries.Insert(0, entry);
            return;
        }
        Application.Current.Dispatcher.Invoke(() => Entries.Insert(0, entry));
    }

    public void Clear()
    {
        if (Application.Current is null) { Entries.Clear(); return; }
        Application.Current.Dispatcher.Invoke(Entries.Clear);
    }
}
