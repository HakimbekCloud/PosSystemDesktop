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
    [ObservableProperty] private string  _activeTab      = "movements";
    [ObservableProperty] private decimal _totalValue;
    [ObservableProperty] private decimal _todayIncoming  = 6_900_000;
    [ObservableProperty] private decimal _todayOutgoing  = 284_000;
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
        _allMovements.Clear();

        var rows = new[]
        {
            ("KIR-00248", "kirim",     "Coca-Cola Uzbekistan",   14, 4_820_000m, "D. Tursunov",  "13 May, 14:22", "done"),
            ("SOT-04812", "chiqim",    "Sotuv",                  8,  284_000m,   "M. Rashidova", "13 May, 13:58", "done"),
            ("TRF-00042", "transfer",  "Markaziy → Chilonzor",   22, 1_280_000m, "D. Tursunov",  "13 May, 11:10", "progress"),
            ("KIR-00247", "kirim",     "Toshkent Non Kombinati", 6,  840_000m,   "D. Tursunov",  "13 May, 09:30", "done"),
            ("AKT-00011", "writeoff",  "Yaroqsizlik akti",       3,  42_000m,    "A. Karimov",   "12 May, 18:42", "done"),
            ("KIR-00246", "kirim",     "Sof Sut MChJ",           9,  1_240_000m, "D. Tursunov",  "12 May, 16:10", "done"),
            ("INV-00008", "inventory", "Inventarizatsiya",       128, 0m,         "A. Karimov",   "12 May, 09:00", "done"),
        };

        foreach (var (doc, type, supplier, items, sum, by, date, status) in rows)
        {
            var (typeLabel, typeColor, typeBg) = type switch
            {
                "kirim"     => ("Kirim",       "#15803D", "#DCFCE7"),
                "chiqim"    => ("Chiqim",      "#0369A1", "#E0F2FE"),
                "transfer"  => ("Transfer",    "#6D28D9", "#EDE9FE"),
                "writeoff"  => ("Yaroqsizlik", "#DC2626", "#FEE2E2"),
                "inventory" => ("Inventar",    "#B45309", "#FEF3C7"),
                _           => ("Boshqa",      "#374151", "#F3F4F6"),
            };

            var (statusLabel, statusColor, statusBg) = status switch
            {
                "done"     => ("Tasdiqlangan", "#15803D", "#DCFCE7"),
                "progress" => ("Jarayonda",    "#0369A1", "#E0F2FE"),
                _          => ("Kutilmoqda",   "#B45309", "#FEF3C7"),
            };

            _allMovements.Add(new MovementRow(
                doc, typeLabel, typeColor, typeBg,
                supplier, items, sum, by, date,
                statusLabel, statusColor, statusBg
            ));
        }

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
