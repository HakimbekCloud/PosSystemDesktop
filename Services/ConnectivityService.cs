using System.Net.Http;
using System.Net.NetworkInformation;

namespace PosSystem.Services;

public class ConnectivityService
{
    public bool IsOnline => NetworkInterface.GetIsNetworkAvailable();

    public async Task<bool> IsApiReachableAsync(string baseUrl)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            await client.GetAsync($"{baseUrl.TrimEnd('/')}/health");
            return true;
        }
        catch
        {
            return false;
        }
    }
}
