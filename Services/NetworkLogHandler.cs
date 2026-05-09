using System.Diagnostics;
using System.Net.Http;
using System.Text;
using CommunityToolkit.Mvvm.Messaging;

namespace PosSystem.Services;

public class NetworkLogHandler(NetworkLogService log) : DelegatingHandler(new HttpClientHandler())
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        string reqBody = "";
        if (request.Content is not null)
        {
            reqBody = await request.Content.ReadAsStringAsync(ct);
            // Restore content so the actual handler can re-read it
            request.Content = new StringContent(reqBody, Encoding.UTF8,
                request.Content.Headers.ContentType?.MediaType ?? "application/json");
        }

        int    statusCode = 0;
        string respBody   = "";
        HttpResponseMessage? response = null;

        try
        {
            response   = await base.SendAsync(request, ct);
            statusCode = (int)response.StatusCode;
            respBody   = await response.Content.ReadAsStringAsync(ct);
            response.Content = new StringContent(respBody, Encoding.UTF8,
                response.Content.Headers.ContentType?.MediaType ?? "application/json");
        }
        catch (Exception ex)
        {
            sw.Stop();
            respBody = ex.Message;
            log.Add(new NetworkLogEntry
            {
                Timestamp    = DateTime.Now,
                Method       = request.Method.Method,
                Url          = request.RequestUri?.ToString() ?? "",
                StatusCode   = 0,
                DurationMs   = sw.ElapsedMilliseconds,
                RequestBody  = Truncate(reqBody),
                ResponseBody = respBody
            });
            throw;
        }

        sw.Stop();
        log.Add(new NetworkLogEntry
        {
            Timestamp    = DateTime.Now,
            Method       = request.Method.Method,
            Url          = request.RequestUri?.ToString() ?? "",
            StatusCode   = statusCode,
            DurationMs   = sw.ElapsedMilliseconds,
            RequestBody  = Truncate(reqBody),
            ResponseBody = Truncate(respBody)
        });

        // 401 on any endpoint other than login/refresh means the token has expired.
        var path = request.RequestUri?.AbsolutePath ?? "";
        if (statusCode == 401
            && !path.Contains("auth/login", StringComparison.OrdinalIgnoreCase)
            && !path.Contains("auth/refresh", StringComparison.OrdinalIgnoreCase))
        {
            WeakReferenceMessenger.Default.Send(new SessionExpiredMessage());
        }

        return response;
    }

    private static string Truncate(string s, int max = 4000) =>
        s.Length <= max ? s : s[..max] + $"\n... ({s.Length - max} chars truncated)";
}
