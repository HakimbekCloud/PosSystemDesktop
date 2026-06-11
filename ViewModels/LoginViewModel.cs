using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PosSystem.Data.Repositories;
using PosSystem.Services;

namespace PosSystem.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    private readonly AuthService                  _auth;
    private readonly SyncService                  _sync;
    private readonly ConnectivityService          _connectivity;
    private readonly SettingsRepository           _settings;
    private readonly GlobalSettingsRepository     _globalSettings;
    private readonly TenantCutoverReadinessGate   _readinessGate;
    private readonly TenantScopeService           _tenantScope;

    public LoginViewModel(
        AuthService auth,
        SyncService sync,
        ConnectivityService connectivity,
        SettingsRepository settings,
        GlobalSettingsRepository globalSettings,
        TenantCutoverReadinessGate readinessGate,
        TenantScopeService tenantScope)
    {
        _auth           = auth;
        _sync           = sync;
        _connectivity   = connectivity;
        _settings       = settings;
        _globalSettings = globalSettings;
        _readinessGate  = readinessGate;
        _tenantScope    = tenantScope;

        // Prefill from the persistent global store so the cashier sees their
        // last-used tenant + server URL even after logout / restart.
        TenantSubdomain = auth.GetPrefillTenantSubdomain();
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

    // Non-fatal status (e.g. "downloading...", "using cached data").
    // Bind in LoginView.xaml when a slot is available; currently exposed only.
    [ObservableProperty]
    private string _statusMessage = "";

    partial void OnServerUrlChanged(string value)       => ClearMessages();
    partial void OnTenantSubdomainChanged(string value) => ClearMessages();
    partial void OnUsernameChanged(string value)        => ClearMessages();
    partial void OnPasswordChanged(string value)        => ClearMessages();

    private void ClearMessages()
    {
        ErrorMessage   = "";
        StatusMessage  = "";
    }

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        IsBusy = true;
        ClearMessages();

        bool runtimeSwitched     = false;
        bool sessionEstablished  = false;

        try
        {
            var tenant = TenantSubdomain.Trim();
            var baseUrl = !string.IsNullOrWhiteSpace(ServerUrl)
                ? ServerUrl.Trim()
                : _globalSettings.Get("api_base_url")
                  ?? _settings.Get("api_base_url")
                  ?? "https://shefpos.uz";

            // Phase 10.5B.1: under runtime tenant DB mode, the readiness gate
            // must approve the typed tenant before any credentials, settings
            // or sales touch the database. The provider then switches to the
            // tenant DB so every downstream write (auth tokens, offline cache,
            // sync watermarks) lands in the correct file.
            var runtimeMode = _globalSettings.Get("tenant_db_runtime_enabled") == "1";
            if (runtimeMode)
            {
                StatusMessage = "Tashkilot tayyorligi tekshirilmoqda...";
                var report = await _readinessGate.CheckAsync(tenant);
                if (!report.CanCutOver)
                {
                    var issues = report.Errors.Count > 0
                        ? string.Join("; ", report.Errors)
                        : $"status={report.Status}";
                    ErrorMessage =
                        "Tenant DB rejimi yoqilgan, lekin bu tashkilot tayyor emas. " +
                        "(Tenant DB mode is enabled, but this tenant is not ready for cutover. " +
                        "Run migration/verification or disable tenant_db_runtime_enabled.) " +
                        "Tafsilotlar: " + issues;
                    return;
                }

                await _tenantScope.SwitchToTenantAsync(tenant);
                runtimeSwitched = true;
                StatusMessage = "";
            }

            // Real reachability probe — adapter-up alone is not enough.
            var reachable = _connectivity.IsOnline
                            && await _connectivity.IsApiReachableAsync(baseUrl);

            if (!reachable)
            {
                // Offline path: only allowed if THIS tenant has a previous
                // successful bootstrap AND a non-empty product cache.
                if (_sync.HasUsableCacheFor(tenant))
                {
                    // Lightweight offline session — no token, just tenant + user name
                    // so new sales are tagged and the toolbar shows the cashier.
                    _settings.Set("tenant_subdomain", tenant);
                    _settings.Set("user_name", Username);
                    StatusMessage = "Internet yo'q — keshlangan ma'lumotlar bilan davom etilmoqda";
                    sessionEstablished = true;
                    WeakReferenceMessenger.Default.Send(new LoginSuccessMessage(Username));
                    return;
                }

                ErrorMessage = "Internet yo'q va keshlangan ma'lumot topilmadi. Birinchi marta kirish uchun internet kerak.";
                return;
            }

            // Online path: real authentication first.
            var (success, message) = await _auth.LoginAsync(tenant, Username, Password, ServerUrl);
            if (!success)
            {
                ErrorMessage = message;
                return;
            }

            // Then bootstrap before opening POS.
            StatusMessage = "Ma'lumotlar yuklanmoqda...";
            var outcome = await _sync.BootstrapAsync(tenant);

            switch (outcome)
            {
                case BootstrapOutcome.Success:
                    sessionEstablished = true;
                    WeakReferenceMessenger.Default.Send(
                        new LoginSuccessMessage(_auth.GetCurrentUserName() ?? Username));
                    return;

                case BootstrapOutcome.FailedButCached:
                    StatusMessage = "Ma'lumotlarni to'liq yangilab bo'lmadi. Keshlangan ma'lumotlar bilan davom etilmoqda.";
                    sessionEstablished = true;
                    WeakReferenceMessenger.Default.Send(
                        new LoginSuccessMessage(_auth.GetCurrentUserName() ?? Username));
                    return;

                case BootstrapOutcome.FailedNoCache:
                    // Roll back the half-authenticated session so the next attempt
                    // is a clean login — preserves any unsynced sales (Phase 0 rule).
                    _auth.Logout();
                    ErrorMessage = "Boshlang'ich ma'lumotlarni yuklab bo'lmadi. Internetni tekshirib qayta urinib ko'ring.";
                    return;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            // If we switched to the tenant DB but the login attempt didn't
            // produce a working session, roll the provider back to legacy so
            // the next attempt re-runs the gate from a clean state. Failures
            // during the rollback itself are swallowed — we don't want to
            // mask the original error on the login screen.
            if (runtimeSwitched && !sessionEstablished)
            {
                try { await _tenantScope.SwitchToLegacyAsync(); }
                catch { /* best effort */ }
            }
            IsBusy = false;
        }
    }

    private bool CanLogin() =>
        !string.IsNullOrWhiteSpace(TenantSubdomain) &&
        !string.IsNullOrWhiteSpace(Username)        &&
        !string.IsNullOrWhiteSpace(Password)        &&
        !IsBusy;
}
