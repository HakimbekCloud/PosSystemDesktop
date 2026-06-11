using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PosSystem.Services;

namespace PosSystem.ViewModels;

// Read-only diagnostics UI ViewModel. Wraps OperatorDiagnosticsService and
// OperatorDiagnosticsExportService. Phase 10.8A: not wired into the cashier
// workflow. A future operator/support trigger (menu item, hotkey, hidden
// settings panel button) will resolve this VM + matching Window from DI and
// call ShowDialog.
//
// Read-only contract:
//   • Refresh runs the diagnostics service (which is itself read-only — Phase
//     10.7A) and copies values to bindable properties.
//   • Export runs the diagnostics export service (read-everything,
//     write-one-redacted-file — Phase 10.7B/C).
//   • Neither path mutates DB, settings, or any path provider state.
//   • No migration / rollback / DB-switch commands exposed.
public partial class OperatorDiagnosticsViewModel : ObservableObject
{
    private readonly OperatorDiagnosticsService       _diagnostics;
    private readonly OperatorDiagnosticsExportService _exporter;

    public OperatorDiagnosticsViewModel(
        OperatorDiagnosticsService diagnostics,
        OperatorDiagnosticsExportService exporter)
    {
        _diagnostics = diagnostics;
        _exporter    = exporter;
    }

    // ── Input ────────────────────────────────────────────────────────────────

    [ObservableProperty] private string _tenantSubdomainInput = "";

    // ── Status ───────────────────────────────────────────────────────────────

    [ObservableProperty] private bool   _isLoading;
    [ObservableProperty] private System.DateTime? _lastUpdatedAt;
    [ObservableProperty] private string _statusMessage = "";

    // ── Active DB ────────────────────────────────────────────────────────────

    [ObservableProperty] private string _activeDbPath = "";
    [ObservableProperty] private bool   _isTenantScoped;

    // ── Feature flags / migration state ──────────────────────────────────────

    [ObservableProperty] private bool   _runtimeTenantDbEnabled;
    [ObservableProperty] private bool   _migrationFeatureEnabled;
    [ObservableProperty] private bool   _sharedToTenantMigrated;
    [ObservableProperty] private string _sharedToTenantMigratedAt = "";

    // ── Readiness ────────────────────────────────────────────────────────────

    [ObservableProperty] private string _cutoverStatus = "";
    [ObservableProperty] private string _rollbackStatus = "";

    // ── Sales ────────────────────────────────────────────────────────────────

    [ObservableProperty] private int _pendingSalesCount;
    [ObservableProperty] private int _poisonSalesCount;
    [ObservableProperty] private int _failedRetryableSalesCount;

    // ── Cache ────────────────────────────────────────────────────────────────

    [ObservableProperty] private int  _productsCount;
    [ObservableProperty] private int  _customersCount;
    [ObservableProperty] private bool _bootstrapCompleted;

    // ── Findings ─────────────────────────────────────────────────────────────

    public ObservableCollection<string> Warnings { get; } = new();
    public ObservableCollection<string> Errors   { get; } = new();

    [ObservableProperty] private string _lastExportPath = "";

    // ── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private async System.Threading.Tasks.Task RefreshAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusMessage = "Yangilanmoqda...";

        try
        {
            var tenant = string.IsNullOrWhiteSpace(TenantSubdomainInput) ? null : TenantSubdomainInput.Trim();
            var report = await _diagnostics.GetReportAsync(tenant);

            ActiveDbPath              = report.ActiveDbPath;
            IsTenantScoped            = report.IsTenantScoped;

            RuntimeTenantDbEnabled    = report.RuntimeTenantDbEnabled;
            MigrationFeatureEnabled   = report.MigrationFeatureEnabled;
            SharedToTenantMigrated    = report.SharedToTenantMigrated;
            SharedToTenantMigratedAt  = report.SharedToTenantMigratedAt ?? "";

            CutoverStatus  = report.CutoverReadiness?.Status.ToString() ?? "(no tenant)";
            RollbackStatus = report.RollbackReadiness.Status.ToString();

            PendingSalesCount         = report.Sales.PendingSalesCount;
            PoisonSalesCount          = report.Sales.PoisonSalesCount;
            FailedRetryableSalesCount = report.Sales.FailedRetryableSalesCount;

            ProductsCount      = report.Cache.ProductsCount;
            CustomersCount     = report.Cache.CustomersCount;
            BootstrapCompleted = report.Cache.BootstrapCompleted;

            Warnings.Clear();
            foreach (var w in report.Warnings) Warnings.Add(w);

            Errors.Clear();
            foreach (var e in report.Errors)   Errors.Add(e);

            LastUpdatedAt = report.CheckedAtUtc.ToLocalTime();
            StatusMessage = "Tayyor";
        }
        catch (System.Exception ex)
        {
            Errors.Add($"Diagnostics refresh failed: {ex.Message}");
            StatusMessage = "Xato";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task ExportAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusMessage = "Eksport qilinmoqda...";

        try
        {
            var tenant = string.IsNullOrWhiteSpace(TenantSubdomainInput) ? null : TenantSubdomainInput.Trim();
            var result = await _exporter.ExportAsync(new OperatorDiagnosticsExportOptions
            {
                TenantSubdomain    = tenant,
                IncludeMachineInfo = true,
            });

            if (result.Success && result.FilePath is not null)
            {
                LastExportPath = result.FilePath;
                StatusMessage  = "Eksport tayyor";
                foreach (var w in result.Warnings) Warnings.Add(w);
            }
            else
            {
                StatusMessage = "Eksport xatosi";
                foreach (var e in result.Errors)   Errors.Add(e);
                foreach (var w in result.Warnings) Warnings.Add(w);
            }
        }
        catch (System.Exception ex)
        {
            Errors.Add($"Diagnostics export failed: {ex.Message}");
            StatusMessage = "Xato";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
