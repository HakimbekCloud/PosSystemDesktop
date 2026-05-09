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
        var saved = auth.GetLastServerAddress();
        ServerAddress = string.IsNullOrEmpty(saved) ? "http://localhost:8080" : saved;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _serverAddress = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _username = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _password = "";

    partial void OnServerAddressChanged(string value) => ErrorMessage = "";
    partial void OnUsernameChanged(string value)      => ErrorMessage = "";
    partial void OnPasswordChanged(string value)      => ErrorMessage = "";

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        IsBusy = true;
        ErrorMessage = "";
        try
        {
            var (success, message) = await _auth.LoginAsync(ServerAddress, Username, Password);
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
        !string.IsNullOrWhiteSpace(ServerAddress) &&
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Password) &&
        !IsBusy;
}
