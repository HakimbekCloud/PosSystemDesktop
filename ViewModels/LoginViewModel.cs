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
