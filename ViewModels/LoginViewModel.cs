using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PosSystem.Services;

namespace PosSystem.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    private readonly AuthService _auth;

    public LoginViewModel(AuthService auth)
    {
        _auth = auth;
        TenantSubdomain = auth.GetLastTenantSubdomain();
        ServerUrl       = auth.GetSavedServerUrl();
    }

    [ObservableProperty]
    private string _serverUrl = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _tenantSubdomain = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _username = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _password = "";

    partial void OnServerUrlChanged(string value)       => ErrorMessage = "";
    partial void OnTenantSubdomainChanged(string value) => ErrorMessage = "";
    partial void OnUsernameChanged(string value)        => ErrorMessage = "";
    partial void OnPasswordChanged(string value)        => ErrorMessage = "";

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        // Bug L4: a malformed server URL would otherwise be silently ignored by
        // ApplyBaseUrl (TryCreate fails → keeps the old BaseAddress) and login would
        // fail opaquely. Validate at input time and abort with a clear message.
        if (!string.IsNullOrWhiteSpace(ServerUrl))
        {
            var candidate = ServerUrl.Trim();
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                ErrorMessage = "Server manzili noto'g'ri";
                return;
            }
        }

        // Bug C2: logging into a DIFFERENT tenant wipes the local data. If the
        // previous tenant still has unsynced sales, those would be lost — make the
        // cashier explicitly confirm before we proceed (No aborts the login).
        var unsynced = _auth.GetUnsyncedSalesCountForOtherTenant(TenantSubdomain);
        if (unsynced > 0)
        {
            var answer = MessageBox.Show(
                $"Diqqat: avvalgi tashkilotning {unsynced} ta sinxronlanmagan savdosi o'chiriladi. Davom etasizmi?",
                "Tashkilot almashtirish",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
                return;
        }

        IsBusy = true;
        ErrorMessage = "";
        try
        {
            var (success, message) = await _auth.LoginAsync(TenantSubdomain, Username, Password, ServerUrl);
            if (success)
                WeakReferenceMessenger.Default.Send(
                    new LoginSuccessMessage(_auth.GetCurrentUserName() ?? Username));
            else
                ErrorMessage = message;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanLogin() =>
        !string.IsNullOrWhiteSpace(TenantSubdomain) &&
        !string.IsNullOrWhiteSpace(Username)        &&
        !string.IsNullOrWhiteSpace(Password)        &&
        !IsBusy;
}
