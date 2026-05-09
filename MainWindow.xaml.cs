using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using PosSystem.Services;
using PosSystem.Views;
using PosSystem.Views.Pos;

namespace PosSystem;

public partial class MainWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly AuthService _auth;

    public MainWindow(IServiceProvider services, AuthService auth)
    {
        InitializeComponent();
        _services = services;
        _auth = auth;

        WeakReferenceMessenger.Default.Register<LoginSuccessMessage>(this,
            (_, _) => Dispatcher.Invoke(NavigateToPOS));

        WeakReferenceMessenger.Default.Register<LogoutMessage>(this,
            (_, _) => Dispatcher.Invoke(NavigateToLogin));

        WeakReferenceMessenger.Default.Register<SessionExpiredMessage>(this, (_, _) =>
            Dispatcher.Invoke(() =>
            {
                if (MainContent.Content is LoginView) return; // already on login page
                _auth.Logout();
                NavigateToLogin();
            }));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (_auth.HasValidSession())
            NavigateToPOS();
        else
            NavigateToLogin();
    }

    private void NavigateToLogin() =>
        MainContent.Content = _services.GetRequiredService<LoginView>();

    private void NavigateToPOS() =>
        MainContent.Content = _services.GetRequiredService<PosView>();

    protected override void OnClosed(EventArgs e)
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        base.OnClosed(e);
    }
}
