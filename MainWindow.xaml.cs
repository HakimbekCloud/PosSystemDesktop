using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using PosSystem.Data;
using PosSystem.Data.Repositories;
using PosSystem.Services;
using PosSystem.Views;
using PosSystem.Views.Pos;

namespace PosSystem;

public partial class MainWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly AuthService _auth;
    private readonly ILocalDatabasePathProvider _pathProvider;
    private readonly GlobalSettingsRepository _global;
    private readonly TenantScopeService _tenantScope;
    private readonly OperatorAccessService _operatorAccess;

    // Phase 10.8A.1 / 10.9A: hidden Ctrl+Shift+D and Ctrl+Shift+M open the
    // read-only operator surfaces. Phase 10.9B routes the gate decision
    // through OperatorAccessService so both the per-machine flag AND (when
    // the session has a recorded role) the allowed-role check apply.
    private OperatorDiagnosticsWindow?  _diagnosticsWindow;
    private MigrationOperationsWindow?  _migrationDashboardWindow;

    public MainWindow(
        IServiceProvider services,
        AuthService auth,
        ILocalDatabasePathProvider pathProvider,
        GlobalSettingsRepository global,
        TenantScopeService tenantScope,
        OperatorAccessService operatorAccess)
    {
        InitializeComponent();
        _services = services;
        _auth = auth;
        _pathProvider = pathProvider;
        _global = global;
        _tenantScope = tenantScope;
        _operatorAccess = operatorAccess;

        PreviewKeyDown += OnPreviewKeyDown;

        WeakReferenceMessenger.Default.Register<LoginSuccessMessage>(this,
            (_, _) => Dispatcher.Invoke(NavigateToPOS));

        WeakReferenceMessenger.Default.Register<LogoutMessage>(this,
            (_, _) => Dispatcher.Invoke(NavigateToLogin));

        WeakReferenceMessenger.Default.Register<SessionExpiredMessage>(this, (_, _) =>
            Dispatcher.Invoke(() =>
            {
                if (MainContent.Content is LoginView) return; // already on login page
                _auth.Logout();

                // Phase 10.5C: under runtime tenant DB mode, switch the path
                // provider back to legacy so the next LoginViewModel pass can
                // re-run TenantCutoverReadinessGate cleanly. AuthService.Logout
                // already cleared the session-only keys from the tenant DB
                // (Phase 10.5C contract) — tenant catalog/sales remain intact.
                if (_global.Get("tenant_db_runtime_enabled") == "1")
                {
                    try { _tenantScope.SwitchToLegacyAsync().GetAwaiter().GetResult(); }
                    catch { /* best effort */ }
                }

                NavigateToLogin();
            }));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Phase 10.5B.1: when runtime tenant DB mode is enabled but the
        // startup path-provider switch did not succeed, legacy `pos.db` is
        // still open. We MUST NOT use HasValidSession() in that state — its
        // result would come from legacy tokens, bypassing the cutover gate.
        // Force the login screen instead so LoginViewModel can run the
        // readiness gate against the typed tenant.
        var runtimeModeActive = _global.Get("tenant_db_runtime_enabled") == "1";
        if (runtimeModeActive && !_pathProvider.IsTenantScoped)
        {
            NavigateToLogin();
            return;
        }

        if (_auth.HasValidSession())
            NavigateToPOS();
        else
            NavigateToLogin();
    }

    private void NavigateToLogin() =>
        MainContent.Content = _services.GetRequiredService<LoginView>();

    private void NavigateToPOS() =>
        MainContent.Content = _services.GetRequiredService<PosView>();

    // Hidden operator shortcuts. Cashier-invisible; flag-gated; silent when
    // the flag is off so casual keypresses never reveal a hidden screen.
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if ((Keyboard.Modifiers & ModifierKeys.Shift)   == 0) return;

        switch (e.Key)
        {
            case Key.D:
                TryOpenDiagnosticsWindow(e);
                break;
            case Key.M:
                TryOpenMigrationDashboardWindow(e);
                break;
        }
    }

    private void TryOpenDiagnosticsWindow(KeyEventArgs e)
    {
        if (!_operatorAccess.CanOpenOperatorDiagnostics())
            return; // silent: cashier-invisible. Denied when flag off OR role check fails.

        e.Handled = true;

        if (_diagnosticsWindow is not null && _diagnosticsWindow.IsLoaded)
        {
            if (_diagnosticsWindow.WindowState == WindowState.Minimized)
                _diagnosticsWindow.WindowState = WindowState.Normal;
            _diagnosticsWindow.Activate();
            return;
        }

        try
        {
            var window = _services.GetRequiredService<OperatorDiagnosticsWindow>();
            window.Owner = this;
            window.Closed += (_, _) => _diagnosticsWindow = null;
            _diagnosticsWindow = window;
            window.Show();
        }
        catch
        {
            _diagnosticsWindow = null;
        }
    }

    private void TryOpenMigrationDashboardWindow(KeyEventArgs e)
    {
        if (!_operatorAccess.CanOpenMigrationOperations())
            return; // silent: cashier-invisible. Denied when flag off OR role check fails.

        e.Handled = true;

        if (_migrationDashboardWindow is not null && _migrationDashboardWindow.IsLoaded)
        {
            if (_migrationDashboardWindow.WindowState == WindowState.Minimized)
                _migrationDashboardWindow.WindowState = WindowState.Normal;
            _migrationDashboardWindow.Activate();
            return;
        }

        try
        {
            var window = _services.GetRequiredService<MigrationOperationsWindow>();
            window.Owner = this;
            window.Closed += (_, _) => _migrationDashboardWindow = null;
            _migrationDashboardWindow = window;
            window.Show();
        }
        catch
        {
            _migrationDashboardWindow = null;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        base.OnClosed(e);
    }
}
