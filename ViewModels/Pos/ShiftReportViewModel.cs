using System.Windows.Documents;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PosSystem.Core.DTOs;
using PosSystem.Services;

namespace PosSystem.ViewModels.Pos;

/// <summary>
/// ViewModel for the Smena Z-Hisoboti (Z-Report) window.
/// Loads a single shift report from the backend and exposes formatted
/// display fields. Print is handled via WPF FlowDocument + PrintDialog,
/// matching the existing receipt-print pattern in PosViewModel.
/// </summary>
public partial class ShiftReportViewModel : ObservableObject
{
    private readonly ApiClient _api;
    private string _shiftUuid = "";

    // ── Loading / error state ─────────────────────────────────────────────────

    [ObservableProperty] private bool   _isLoading;
    [ObservableProperty] private string _errorMessage = "";

    // ── Shift metadata ────────────────────────────────────────────────────────

    [ObservableProperty] private string  _shiftUuidDisplay = "";
    [ObservableProperty] private string  _statusDisplay    = "";
    [ObservableProperty] private string  _openedAtDisplay  = "";
    [ObservableProperty] private string  _closedAtDisplay  = "";
    [ObservableProperty] private string  _currencyCode     = "UZS";

    // ── Sales totals ──────────────────────────────────────────────────────────

    [ObservableProperty] private decimal _cashSalesAmount;
    [ObservableProperty] private decimal _cashbackAmount;
    [ObservableProperty] private decimal _refundAmount;
    [ObservableProperty] private decimal _debtPaymentAmount;
    [ObservableProperty] private long    _orderCount;
    [ObservableProperty] private long    _refundCount;
    [ObservableProperty] private long    _debtPaymentCount;

    // ── Cash movements ────────────────────────────────────────────────────────

    [ObservableProperty] private decimal _openingCashAmount;
    [ObservableProperty] private decimal _cashInAmount;
    [ObservableProperty] private decimal _cashOutAmount;
    [ObservableProperty] private decimal _netCashMovementAmount;
    [ObservableProperty] private decimal _computedExpectedCashAmount;
    [ObservableProperty] private long    _cashInCount;
    [ObservableProperty] private long    _cashOutCount;
    [ObservableProperty] private long    _transactionCount;

    // ── Formatted strings (consumed directly in XAML) ────────────────────────

    public string FormattedCashSales    => $"{CashSalesAmount:N0} so'm";
    public string FormattedCashback     => $"{CashbackAmount:N0} so'm";
    public string FormattedRefund       => $"{RefundAmount:N0} so'm";
    public string FormattedDebtPayment  => $"{DebtPaymentAmount:N0} so'm";
    public string FormattedOpening      => $"{OpeningCashAmount:N0} so'm";
    public string FormattedCashIn       => $"{CashInAmount:N0} so'm";
    public string FormattedCashOut      => $"{Math.Abs(CashOutAmount):N0} so'm";
    public string FormattedExpected     => $"{ComputedExpectedCashAmount:N0} so'm";
    public string FormattedNetMovement  => $"{NetCashMovementAmount:N0} so'm";
    public string GeneratedAt           => DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");

    public ShiftReportViewModel(ApiClient api)
    {
        _api = api;
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoadReportAsync(string shiftUuid) =>
        await LoadAsync(shiftUuid);

    // Public entry point so the Window code-behind can await it directly
    // without going through ExecuteAsync (which silently returns
    // Task.CompletedTask when CanExecute is false, leaving IsLoading stuck).
    public async Task LoadAsync(string shiftUuid)
    {
        if (string.IsNullOrWhiteSpace(shiftUuid)) return;

        _shiftUuid    = shiftUuid;
        IsLoading     = true;
        ErrorMessage  = "";

        try
        {
            var report = await _api.GetShiftReportAsync(shiftUuid);
            MapFromDto(report);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Hisobot yuklanmadi: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void PrintReport()
    {
        var pd = new System.Windows.Controls.PrintDialog();
        if (pd.ShowDialog() != true) return;

        var doc       = BuildPrintDocument();
        var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
        pd.PrintDocument(paginator, "Smena Z-Hisoboti");
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private void MapFromDto(PosShiftReportResponse r)
    {
        ShiftUuidDisplay = r.ShiftUuid;
        CurrencyCode     = r.CurrencyCode ?? "UZS";

        StatusDisplay = r.Status switch
        {
            "OPEN"      => "OCHIQ",
            "CLOSED"    => "YOPILGAN",
            "CANCELLED" => "BEKOR QILINGAN",
            _           => r.Status
        };

        OpenedAtDisplay = r.OpenedAt.HasValue
            ? r.OpenedAt.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss")
            : "—";

        ClosedAtDisplay = r.ClosedAt.HasValue
            ? r.ClosedAt.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss")
            : "—";

        CashSalesAmount           = r.CashSalesAmount    ?? 0m;
        CashbackAmount            = r.CashbackAmount     ?? 0m;
        RefundAmount              = Math.Abs(r.RefundAmount ?? 0m);
        DebtPaymentAmount         = r.DebtPaymentAmount  ?? 0m;
        OpeningCashAmount         = r.OpeningCashAmount  ?? 0m;
        CashInAmount              = r.CashInAmount       ?? 0m;
        CashOutAmount             = r.CashOutAmount      ?? 0m;
        NetCashMovementAmount     = r.NetCashMovementAmount ?? 0m;
        ComputedExpectedCashAmount = r.ComputedExpectedCashAmount ?? 0m;

        OrderCount        = r.OrderCount;
        RefundCount       = r.RefundCount;
        DebtPaymentCount  = r.DebtPaymentCount;
        CashInCount       = r.CashInCount;
        CashOutCount      = r.CashOutCount;
        TransactionCount  = r.TransactionCount;

        // Notify all formatted string computed properties.
        OnPropertyChanged(nameof(FormattedCashSales));
        OnPropertyChanged(nameof(FormattedCashback));
        OnPropertyChanged(nameof(FormattedRefund));
        OnPropertyChanged(nameof(FormattedDebtPayment));
        OnPropertyChanged(nameof(FormattedOpening));
        OnPropertyChanged(nameof(FormattedCashIn));
        OnPropertyChanged(nameof(FormattedCashOut));
        OnPropertyChanged(nameof(FormattedExpected));
        OnPropertyChanged(nameof(FormattedNetMovement));
        OnPropertyChanged(nameof(GeneratedAt));
    }

    // ── Print document ────────────────────────────────────────────────────────

    private FlowDocument BuildPrintDocument()
    {
        var doc = new FlowDocument
        {
            FontFamily  = new FontFamily("Courier New"),
            FontSize    = 11,
            PagePadding = new System.Windows.Thickness(20),
            ColumnWidth = 300
        };

        void AddLine(string text, bool bold = false, bool center = false)
        {
            var run = new Run(text);
            var para = new Paragraph(run)
            {
                Margin        = new System.Windows.Thickness(0),
                TextAlignment = center
                    ? System.Windows.TextAlignment.Center
                    : System.Windows.TextAlignment.Left
            };
            if (bold) para.FontWeight = System.Windows.FontWeights.Bold;
            doc.Blocks.Add(para);
        }

        string Sep(char ch = '=') => new string(ch, 32);

        AddLine(Sep(), bold: true, center: true);
        AddLine("   SMENA Z-HISOBOTI",       bold: true, center: true);
        AddLine(Sep(), bold: true, center: true);
        AddLine($"Holati   : {StatusDisplay}");
        AddLine($"Ochildi  : {OpenedAtDisplay}");
        AddLine($"Yopildi  : {ClosedAtDisplay}");
        AddLine(Sep('-'));

        AddLine("NAQD SAVDO",                bold: true);
        AddLine($"  Zakazlar soni    : {OrderCount}");
        AddLine($"  Naqd savdo       : {FormattedCashSales}");
        AddLine($"  Cashback (-)     : {FormattedCashback}");
        AddLine($"  Qaytarish (-)    : {FormattedRefund}   x{RefundCount}");
        AddLine($"  Qarz to'lov (+)  : {FormattedDebtPayment}   x{DebtPaymentCount}");
        AddLine(Sep('-'));

        AddLine("NAQD HARAKATLAR",           bold: true);
        AddLine($"  Boshlang'ich     : {FormattedOpening}");
        AddLine($"  Kirim (+)        : {FormattedCashIn}   x{CashInCount}");
        AddLine($"  Chiqim (-)       : {FormattedCashOut}   x{CashOutCount}");
        AddLine($"  Sof harakat      : {FormattedNetMovement}");
        AddLine($"  Kutilayotgan     : {FormattedExpected}");
        AddLine($"  Tranzaksiyalar   : {TransactionCount}");
        AddLine(Sep());

        AddLine(DateTime.Now.ToString("dd.MM.yyyy  HH:mm:ss"), center: true);
        AddLine("");
        AddLine("Kassir:   ___________________________");
        AddLine("Menejer:  ___________________________");

        return doc;
    }
}
