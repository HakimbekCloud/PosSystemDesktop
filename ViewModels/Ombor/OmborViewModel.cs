using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PosSystem.Data.Repositories;

namespace PosSystem.ViewModels.Ombor;

// ── Display models ─────────────────────────────────────────────────────────

public record MovementRow(
    string DocNumber, string TypeLabel, string TypeColor, string TypeBg,
    string Supplier, int Items, decimal Sum,
    string By, string Date,
    string StatusLabel, string StatusColor, string StatusBg
)
{
    public string SumDisplay => Sum > 0 ? $"{Sum:N0} so'm" : "—";
}

public record StockRow(
    string Name, string Code, string Category,
    decimal Stock, string Unit, decimal MinStock,
    decimal CostTotal, string StatusLabel, string BarColor, double FillPct
)
{
    public double FillWidth => Math.Max(0, Math.Min(110.0, FillPct / 100.0 * 110.0));
}

public record AlertRow(
    string Name, string Unit, decimal Stock,
    bool IsOut, string Message, string BadgeText,
    string AlertBg, string AlertFg
);

// ── ViewModel ──────────────────────────────────────────────────────────────

public partial class OmborViewModel : ObservableObject
{
    private readonly ProductRepository _products;

    private const int MovPageSize   = 10;
    private const int StockPageSize = 15;

    private List<MovementRow> _allMovements = [];
    private List<StockRow>    _allStock     = [];

    public OmborViewModel(ProductRepository products) => _products = products;

    // ── Page state ─────────────────────────────────────────────────────────
    // Bug L1: these used to ship hardcoded fake daily totals. There is no local
    // warehouse-movement feed yet, so the honest state is zero / empty rather than
    // fabricated figures.
    [ObservableProperty] private string  _activeTab      = "movements";
    [ObservableProperty] private decimal _totalValue;
    [ObservableProperty] private decimal _todayIncoming;
    [ObservableProperty] private decimal _todayOutgoing;
    [ObservableProperty] private int     _alertCount;

    // ── Movements pagination ───────────────────────────────────────────────
    [ObservableProperty] private int _movPage       = 1;
    [ObservableProperty] private int _movTotalPages = 1;
    [ObservableProperty] private int _movTotalCount = 0;

    // ── Stock pagination ───────────────────────────────────────────────────
    [ObservableProperty] private int _stockPage       = 1;
    [ObservableProperty] private int _stockTotalPages = 1;
    [ObservableProperty] private int _stockTotalCount = 0;

    // Tab badge counts (total, not paged)
    public int MovementsCount => _allMovements.Count;
    public int StockCount     => _allStock.Count;

    // ── Paged display collections ──────────────────────────────────────────
    public ObservableCollection<MovementRow> Movements  { get; } = [];
    public ObservableCollection<StockRow>    StockItems { get; } = [];
    public ObservableCollection<AlertRow>    Alerts     { get; } = [];

    // ── Load ───────────────────────────────────────────────────────────────
    public void Load()
    {
        BuildMovements();
        BuildStock();
        MovPage   = 1;
        StockPage = 1;
        ApplyMovPage();
        ApplyStockPage();
        ActiveTab = "movements";
    }

    // ── Movements data ─────────────────────────────────────────────────────
    private void BuildMovements()
    {
        // Bug L1: there is no local warehouse-movement source yet. Ship an empty
        // list instead of fabricated documents; the paged view shows nothing until
        // a real movements feed exists.
        _allMovements.Clear();

        MovTotalCount = _allMovements.Count;
        OnPropertyChanged(nameof(MovementsCount));
    }

    private void ApplyMovPage()
    {
        MovTotalPages = Math.Max(1, (int)Math.Ceiling((double)_allMovements.Count / MovPageSize));
        if (MovPage > MovTotalPages) MovPage = MovTotalPages;
        if (MovPage < 1)             MovPage = 1;

        Movements.Clear();
        foreach (var r in _allMovements.Skip((MovPage - 1) * MovPageSize).Take(MovPageSize))
            Movements.Add(r);

        MovPrevPageCommand.NotifyCanExecuteChanged();
        MovNextPageCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanMovPrev))]
    private void MovPrevPage() { MovPage--; ApplyMovPage(); }
    private bool CanMovPrev() => MovPage > 1;

    [RelayCommand(CanExecute = nameof(CanMovNext))]
    private void MovNextPage() { MovPage++; ApplyMovPage(); }
    private bool CanMovNext() => MovPage < MovTotalPages;

    // ── Stock data ─────────────────────────────────────────────────────────
    private void BuildStock()
    {
        _allStock.Clear();
        Alerts.Clear();

        var all = _products.GetAll();
        TotalValue = all.Sum(p => p.CostPrice * p.Stock);
        const decimal minStock = 10m;

        foreach (var p in all.OrderBy(x => x.CategoryName).ThenBy(x => x.Name))
        {
            var (statusLabel, barColor) = p.Stock switch
            {
                <= 0        => ("Tugagan",    "#EF4444"),
                <= minStock => ("Kam qoldiq", "#F59E0B"),
                _           => ("Yetarli",    "#22C55E"),
            };

            double fillPct = Math.Min(100.0, Math.Max(0,
                (double)(p.Stock / (minStock * 5)) * 100));

            _allStock.Add(new StockRow(
                p.Name,
                !string.IsNullOrEmpty(p.Barcode) ? p.Barcode : p.Code,
                p.CategoryName, p.Stock, p.Unit, minStock,
                p.CostPrice * p.Stock, statusLabel, barColor, fillPct
            ));

            if (p.Stock <= minStock)
            {
                var isOut = p.Stock <= 0;
                var msg   = isOut
                    ? $"{p.Name} tugagan. Yetkazib beruvchiga buyurtma berish kerak."
                    : $"{p.Name} qoldig'i {p.Stock:N0} {p.Unit} — minimaldan past.";
                Alerts.Add(new AlertRow(
                    p.Name, p.Unit, p.Stock, isOut, msg,
                    isOut ? "Tugagan" : $"{p.Stock:N0} {p.Unit}",
                    isOut ? "#FEE2E2" : "#FEF3C7",
                    isOut ? "#DC2626" : "#B45309"
                ));
            }
        }

        StockTotalCount = _allStock.Count;
        AlertCount      = Alerts.Count;
        OnPropertyChanged(nameof(StockCount));
    }

    private void ApplyStockPage()
    {
        StockTotalPages = Math.Max(1, (int)Math.Ceiling((double)_allStock.Count / StockPageSize));
        if (StockPage > StockTotalPages) StockPage = StockTotalPages;
        if (StockPage < 1)               StockPage = 1;

        StockItems.Clear();
        foreach (var r in _allStock.Skip((StockPage - 1) * StockPageSize).Take(StockPageSize))
            StockItems.Add(r);

        StockPrevPageCommand.NotifyCanExecuteChanged();
        StockNextPageCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanStockPrev))]
    private void StockPrevPage() { StockPage--; ApplyStockPage(); }
    private bool CanStockPrev() => StockPage > 1;

    [RelayCommand(CanExecute = nameof(CanStockNext))]
    private void StockNextPage() { StockPage++; ApplyStockPage(); }
    private bool CanStockNext() => StockPage < StockTotalPages;

    // ── Tab commands ───────────────────────────────────────────────────────
    [RelayCommand] private void ShowMovementsTab() => ActiveTab = "movements";
    [RelayCommand] private void ShowStockTab()     => ActiveTab = "stock";
    [RelayCommand] private void ShowAlertsTab()    => ActiveTab = "alerts";
}
