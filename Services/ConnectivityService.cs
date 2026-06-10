using System.Net.Http;
using System.Net.NetworkInformation;

namespace PosSystem.Services;

public class ConnectivityService
{
    // Cheap, synchronous gate. TRUE on any LAN/VPN even without WAN — used only to
    // decide whether sync is worth attempting (Bug M2). Sync handles its own
    // per-section failures, so a false positive here just causes a quick failed
    // attempt, never a crash.
    public bool IsOnline => NetworkInterface.GetIsNetworkAvailable();

    // Bug M2: the truthful signal. SyncService reports real API outcomes here:
    //   - a successful API section            → reachable = true
    //   - a pure network-level failure         → reachable = false
    //     (HttpRequestException with no StatusCode, or a timeout/cancellation)
    // Server-side rejections (4xx/5xx with a StatusCode) still prove the API was
    // reached, so they count as reachable = true.
    public bool? LastApiReachable { get; private set; }
    public DateTime? LastApiCheckAt { get; private set; }

    public void ReportApiReachable(bool reachable)
    {
        LastApiReachable = reachable;
        LastApiCheckAt   = DateTime.UtcNow;
    }

    // Combined signal for the UI/status pill: we must have a network adapter AND,
    // if we have ever learned the real API state, that state must be reachable.
    // Before the first sync (LastApiReachable == null) we optimistically mirror
    // IsOnline so the pill isn't stuck "offline" on launch.
    public bool IsEffectivelyOnline =>
        IsOnline && LastApiReachable != false;

    // Explicit on-demand probe (e.g. before a manual sync). Also records the
    // outcome so the UI reflects it. Returns false on any network/timeout error.
    public async Task<bool> IsApiReachableAsync(string baseUrl)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            var resp = await client.GetAsync($"{baseUrl.TrimEnd('/')}/health");
            // Any HTTP response (even an error code) proves the API was reached.
            ReportApiReachable(true);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            ReportApiReachable(false);
            return false;
        }
    }
}
