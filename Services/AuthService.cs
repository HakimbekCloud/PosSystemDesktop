using System.Net.Http;
using PosSystem.Data.Repositories;

namespace PosSystem.Services;

public class AuthService(ApiClient api, SettingsRepository settings)
{
    public bool HasValidSession() =>
        !string.IsNullOrEmpty(settings.Get("auth_token"));

    public string? GetCurrentUserName() =>
        settings.Get("user_name");

    public long GetCurrentUserId() =>
        long.TryParse(settings.Get("user_id"), out var id) ? id : 0;

    public string GetLastServerAddress() =>
        settings.Get("api_base_url") ?? "";

    public async Task<(bool Success, string Message)> LoginAsync(
        string serverAddress, string username, string password)
    {
        if (string.IsNullOrWhiteSpace(serverAddress))
            return (false, "Server manzilini kiriting");

        settings.Set("api_base_url", serverAddress.TrimEnd('/'));
        api.ApplyBaseUrl();

        try
        {
            var response = await api.LoginAsync(username, password);

            settings.Set("auth_token",    response.AccessToken);
            settings.Set("refresh_token", response.RefreshToken);
            settings.Set("user_name",     response.User.Username);
            settings.Set("user_id",       response.User.Id.ToString());
            if (response.Tenant is not null)
                settings.Set("tenant_name", response.Tenant.Name);

            api.ApplyAuthToken();
            return (true, "");
        }
        catch (HttpRequestException ex) when
            (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return (false, "Login yoki parol noto'g'ri");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public void Logout()
    {
        settings.Remove("auth_token");
        settings.Remove("refresh_token");
        settings.Remove("user_name");
        settings.Remove("user_id");
        api.ApplyAuthToken();
    }
}
