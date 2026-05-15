using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PosSystem.Core.Mock;

namespace PosSystem.ViewModels.Admin.Modules;

// One file for all eight v1-admin modules. Each VM holds the mock data
// it needs; swap to repositories later without touching the views.

// ════════════════════════════════════════════════════════════════════
// 01 Sotuv — placeholder that links to the existing cashier UI
// ════════════════════════════════════════════════════════════════════
public partial class SotuvViewModel : BaseViewModel
{
    public string KpiRevenue { get; } = MockCatalog.FormatMoney(8_240_000m);
    public string KpiOrders  { get; } = "184";
    public string KpiAverage { get; } = MockCatalog.FormatMoney(44_780m);
    public string KpiReturns { get; } = "3";
    public string WpfHint    { get; } = "Bu kassir paneli — sizning PosView.xaml'da allaqachon mavjud. Bu yerda faqat ko'rib chiqish.";
}

// ════════════════════════════════════════════════════════════════════
// 02 Mahsulotlar
// ════════════════════════════════════════════════════════════════════
public partial class MahsulotlarViewModel : BaseViewModel
{
    public ObservableCollection<ProductRow> Products  { get; }
    public ObservableCollection<string>     Categories { get; }

    [ObservableProperty] private string _searchQuery = "";

    public string KpiTotal     { get; }
    public string KpiValue     { get; }
    public string KpiLow       { get; }
    public string KpiOut       { get; }
    public string WpfHint      { get; } = "WrapPanel (karta) yoki DataGrid (jadval) + Grid Statlar uchun. Status badge'lar — Border + CornerRadius=4.";

    public MahsulotlarViewModel()
    {
        Categories = new ObservableCollection<string>(MockCatalog.Categories);
        Products = new ObservableCollection<ProductRow>
        {
            new() { Code="CC-001", Name="Coca-Cola 1.5L",       Category="Ichimliklar",      Unit="dona", Price=14_000m,  Cost=9_800m,  Stock=84,  Status="active" },
            new() { Code="PP-001", Name="Pepsi 1.5L",           Category="Ichimliklar",      Unit="dona", Price=13_500m,  Cost=9_500m,  Stock=5,   Status="low"    },
            new() { Code="AQ-001", Name="Aqua 1L",              Category="Ichimliklar",      Unit="dona", Price=5_000m,   Cost=3_400m,  Stock=120, Status="active" },
            new() { Code="BR-001", Name="Toshkent noni",         Category="Non mahsulotlari", Unit="dona", Price=5_000m,   Cost=2_400m,  Stock=42,  Status="active" },
            new() { Code="BR-002", Name="Bug'doy non",           Category="Non mahsulotlari", Unit="dona", Price=4_500m,   Cost=2_200m,  Stock=0,   Status="out"    },
            new() { Code="ML-001", Name="Sut Prezident 1L",     Category="Sut mahsulotlari", Unit="dona", Price=18_500m,  Cost=12_400m, Stock=38,  Status="active" },
            new() { Code="ML-002", Name="Qatiq Brio 500g",      Category="Sut mahsulotlari", Unit="dona", Price=9_000m,   Cost=5_900m,  Stock=22,  Status="active" },
            new() { Code="MT-001", Name="Mol go'shti",           Category="Go'sht",           Unit="kg",   Price=95_000m,  Cost=68_000m, Stock=14,  Status="active" },
            new() { Code="MT-002", Name="Tovuq fileysi",         Category="Go'sht",           Unit="kg",   Price=55_000m,  Cost=38_000m, Stock=0,   Status="out"    },
            new() { Code="FR-001", Name="Olmacha",                Category="Mevalar",          Unit="kg",   Price=15_000m,  Cost=10_200m, Stock=46,  Status="active" },
            new() { Code="FR-002", Name="Banan Ekvador",          Category="Mevalar",          Unit="kg",   Price=22_000m,  Cost=14_500m, Stock=8,   Status="low"    },
            new() { Code="VG-001", Name="Pomidor",                Category="Sabzavotlar",      Unit="kg",   Price=12_000m,  Cost=8_000m,  Stock=24,  Status="active" },
            new() { Code="VG-002", Name="Kartoshka",              Category="Sabzavotlar",      Unit="kg",   Price=6_000m,   Cost=3_800m,  Stock=150, Status="active" },
            new() { Code="VG-003", Name="Piyoz",                  Category="Sabzavotlar",      Unit="kg",   Price=4_500m,   Cost=2_800m,  Stock=78,  Status="active" },
            new() { Code="VG-004", Name="Karam",                  Category="Sabzavotlar",      Unit="kg",   Price=5_500m,   Cost=3_200m,  Stock=4,   Status="low"    },
        };

        KpiTotal = Products.Count.ToString();
        KpiValue = MockCatalog.FormatMoney(Products.Sum(p => p.Stock * p.Cost));
        KpiLow   = Products.Count(p => p.Status == "low").ToString();
        KpiOut   = Products.Count(p => p.Status == "out").ToString();
    }
}

public partial class ProductRow : ObservableObject
{
    public string  Code     { get; init; } = "";
    public string  Name     { get; init; } = "";
    public string  Category { get; init; } = "";
    public string  Unit     { get; init; } = "";
    public decimal Price    { get; init; }
    public decimal Cost     { get; init; }
    public int     Stock    { get; init; }
    public string  Status   { get; init; } = "active";

    public string PriceFmt => MockCatalog.FormatMoney(Price);
    public string CostFmt  => MockCatalog.FormatMoney(Cost);
    public string StockFmt => Stock + " " + Unit;
}

// ════════════════════════════════════════════════════════════════════
// 03 Ombor
// ════════════════════════════════════════════════════════════════════
public partial class OmborViewModel : BaseViewModel
{
    public ObservableCollection<MovementRow> Movements { get; }
    public ObservableCollection<StockRow>    Stock     { get; }
    public ObservableCollection<AlertRow>    Alerts    { get; }

    public string KpiValue   { get; } = MockCatalog.FormatMoney(184_220_000m);
    public string KpiInbound { get; } = MockCatalog.FormatMoney(6_900_000m);
    public string KpiOutbound{ get; } = MockCatalog.FormatMoney(284_000m);
    public string KpiAlerts  { get; } = "5";
    public string WpfHint    { get; } = "TabControl uchta tab uchun, ItemsControl + Grid qatorlar uchun. Operatsiya turi badge — Border + ranglar siz App.xaml'da aniqlagansiz.";

    public OmborViewModel()
    {
        Movements = new ObservableCollection<MovementRow>
        {
            new() { Date="13 May, 14:22", Doc="KIR-00248", Type="kirim",    Partner="Coca-Cola Uzbekistan",      Items=14,  Sum=4_820_000m, By="D. Tursunov",  Status="done"     },
            new() { Date="13 May, 13:58", Doc="SOT-04812", Type="chiqim",   Partner="Sotuv",                      Items=8,   Sum=284_000m,   By="M. Rashidova", Status="done"     },
            new() { Date="13 May, 11:10", Doc="TRF-00042", Type="transfer", Partner="Markaziy → Chilonzor",        Items=22,  Sum=1_280_000m, By="D. Tursunov",  Status="progress" },
            new() { Date="13 May, 09:30", Doc="KIR-00247", Type="kirim",    Partner="Toshkent Non Kombinati",      Items=6,   Sum=840_000m,   By="D. Tursunov",  Status="done"     },
            new() { Date="12 May, 18:42", Doc="AKT-00011", Type="writeoff", Partner="Yaroqsizlik",                 Items=3,   Sum=42_000m,    By="A. Karimov",   Status="done"     },
            new() { Date="12 May, 16:10", Doc="KIR-00246", Type="kirim",    Partner="Sof Sut MChJ",                Items=9,   Sum=1_240_000m, By="D. Tursunov",  Status="done"     },
            new() { Date="12 May, 09:00", Doc="INV-00008", Type="inventory",Partner="Inventarizatsiya",            Items=128, Sum=0m,         By="A. Karimov",   Status="done"     },
        };

        Stock = new ObservableCollection<StockRow>
        {
            new() { Name="Coca-Cola 1.5L", Code="CC-001", Cat="Ichimliklar",      Stock=84,  Unit="dona", Min=10, Status="active", Value=823_200m   },
            new() { Name="Pepsi 1.5L",     Code="PP-001", Cat="Ichimliklar",      Stock=5,   Unit="dona", Min=10, Status="low",    Value=47_500m    },
            new() { Name="Toshkent noni",  Code="BR-001", Cat="Non mahsulotlari", Stock=42,  Unit="dona", Min=15, Status="active", Value=100_800m   },
            new() { Name="Bug'doy non",     Code="BR-002", Cat="Non mahsulotlari", Stock=0,   Unit="dona", Min=15, Status="out",    Value=0m         },
            new() { Name="Mol go'shti",     Code="MT-001", Cat="Go'sht",           Stock=14,  Unit="kg",   Min=5,  Status="active", Value=952_000m   },
            new() { Name="Tovuq fileysi",   Code="MT-002", Cat="Go'sht",           Stock=0,   Unit="kg",   Min=8,  Status="out",    Value=0m         },
            new() { Name="Olmacha",         Code="FR-001", Cat="Mevalar",          Stock=46,  Unit="kg",   Min=10, Status="active", Value=469_200m   },
            new() { Name="Banan Ekvador",   Code="FR-002", Cat="Mevalar",          Stock=8,   Unit="kg",   Min=10, Status="low",    Value=116_000m   },
            new() { Name="Karam",           Code="VG-004", Cat="Sabzavotlar",      Stock=4,   Unit="kg",   Min=10, Status="low",    Value=12_800m    },
        };

        Alerts = new ObservableCollection<AlertRow>
        {
            new() { Severity="danger",  Name="Bug'doy non",   Detail="Bug'doy non tugagan. Yetkazib beruvchiga buyurtma berish kerak.", Right="Tugagan" },
            new() { Severity="danger",  Name="Tovuq fileysi", Detail="Tovuq fileysi tugagan. Yetkazib beruvchiga buyurtma berish kerak.", Right="Tugagan" },
            new() { Severity="warning", Name="Pepsi 1.5L",    Detail="Pepsi 1.5L qoldig'i 5 dona — minimaldan past.",  Right="5 dona" },
            new() { Severity="warning", Name="Banan Ekvador", Detail="Banan Ekvador qoldig'i 8 kg — minimaldan past.",  Right="8 kg"   },
            new() { Severity="warning", Name="Karam",         Detail="Karam qoldig'i 4 kg — minimaldan past.",          Right="4 kg"   },
        };
    }
}

public partial class MovementRow : ObservableObject
{
    public string  Date    { get; init; } = "";
    public string  Doc     { get; init; } = "";
    public string  Type    { get; init; } = "";
    public string  Partner { get; init; } = "";
    public int     Items   { get; init; }
    public decimal Sum     { get; init; }
    public string  By      { get; init; } = "";
    public string  Status  { get; init; } = "done";

    public string SumFmt => Sum == 0 ? "—" : MockCatalog.FormatMoney(Sum);
}

public partial class StockRow : ObservableObject
{
    public string  Name   { get; init; } = "";
    public string  Code   { get; init; } = "";
    public string  Cat    { get; init; } = "";
    public int     Stock  { get; init; }
    public string  Unit   { get; init; } = "";
    public int     Min    { get; init; }
    public string  Status { get; init; } = "active";
    public decimal Value  { get; init; }

    public string StockFmt => Stock + " " + Unit;
    public string ValueFmt => MockCatalog.FormatMoney(Value);
}

public partial class AlertRow : ObservableObject
{
    public string Severity { get; init; } = "warning";
    public string Name     { get; init; } = "";
    public string Detail   { get; init; } = "";
    public string Right    { get; init; } = "";
}

// ════════════════════════════════════════════════════════════════════
// 04 Hisobotlar
// ════════════════════════════════════════════════════════════════════
public partial class HisobotlarViewModel : BaseViewModel
{
    public ObservableCollection<DailyBar>      Days        { get; }
    public ObservableCollection<HourBar>       Hours       { get; }
    public ObservableCollection<PaymentSlice>  Payments    { get; }
    public ObservableCollection<TopProductRow> TopProducts { get; }
    public ObservableCollection<CashierPerf>   Cashiers    { get; }

    public string KpiRevenue { get; } = MockCatalog.FormatMoney(56_440_000m);
    public string KpiProfit  { get; } = MockCatalog.FormatMoney(18_240_000m);
    public string KpiOrders  { get; } = "1 248";
    public string KpiAverage { get; } = MockCatalog.FormatMoney(45_220m);
    public string WpfHint    { get; } = "Charts: Polyline (line) + Path/ArcSegment (donut) + Rectangle'lar (bar). Yoki LiveCharts2 NuGet paketini ulang.";

    public HisobotlarViewModel()
    {
        var dayLabels = new[] { "Du", "Se", "Ch", "Pa", "Ju", "Sh", "Ya" };
        var dayAmts   = new[] { 6_240_000m, 7_120_000m, 5_840_000m, 8_120_000m, 9_640_000m, 11_240_000m, 8_240_000m };
        var dayMax    = dayAmts.Max();
        Days = new ObservableCollection<DailyBar>();
        for (var i = 0; i < dayLabels.Length; i++)
            Days.Add(new DailyBar { Label = dayLabels[i], Amount = dayAmts[i],
                                    BarHeight = Math.Max(8.0, (double)(dayAmts[i] / dayMax) * 200.0) });

        var hourLabels = new[] { "08", "10", "12", "14", "16", "18", "20", "22" };
        var hourAmts   = new[] { 240_000m, 580_000m, 1_240_000m, 1_680_000m, 920_000m, 1_840_000m, 1_120_000m, 620_000m };
        var hourMax    = hourAmts.Max();
        Hours = new ObservableCollection<HourBar>();
        for (var i = 0; i < hourLabels.Length; i++)
            Hours.Add(new HourBar { Label = hourLabels[i], Amount = hourAmts[i],
                                    BarHeight = Math.Max(8.0, (double)(hourAmts[i] / hourMax) * 180.0) });

        Payments = new ObservableCollection<PaymentSlice>
        {
            new() { Label="Naqd",  Amount=18_400_000m, Color="#155EEF" },
            new() { Label="Karta", Amount=22_840_000m, Color="#22C55E" },
            new() { Label="Click", Amount=8_120_000m,  Color="#3B82F6" },
            new() { Label="Payme", Amount=5_840_000m,  Color="#F59E0B" },
            new() { Label="Qarz",  Amount=1_240_000m,  Color="#94A3B8" },
        };
        var paySum = Payments.Sum(p => p.Amount);
        foreach (var p in Payments)
            p.Share = (int)Math.Round((double)(p.Amount / paySum) * 100);

        TopProducts = new ObservableCollection<TopProductRow>
        {
            new() { Rank=1, Name="Coca-Cola 1.5L",       Sold=284, Revenue=3_976_000m },
            new() { Rank=2, Name="Toshkent noni",        Sold=512, Revenue=2_560_000m },
            new() { Rank=3, Name="Mol go'shti",          Sold=42,  Revenue=3_990_000m },
            new() { Rank=4, Name="Sut Prezident 1L",     Sold=128, Revenue=2_368_000m },
            new() { Rank=5, Name="Olmacha",              Sold=96,  Revenue=1_440_000m },
        };
        var topMax = TopProducts.Max(p => p.Revenue);
        foreach (var p in TopProducts)
            p.BarWidth = Math.Max(8.0, (double)(p.Revenue / topMax) * 200.0);

        Cashiers = new ObservableCollection<CashierPerf>
        {
            new() { Initials="MR", Name="Mavluda Rashidova", Sales=4_280_000m, Checks=84, Average=50_950m, Accent=0 },
            new() { Initials="BN", Name="Bobur Nazarov",     Sales=1_840_000m, Checks=42, Average=43_810m, Accent=1 },
            new() { Initials="SY", Name="Shaxnoza Yusupova", Sales=2_120_000m, Checks=58, Average=36_550m, Accent=4 },
        };
        var cashMax = Cashiers.Max(c => c.Sales);
        foreach (var c in Cashiers)
            c.Share = (int)Math.Round((double)(c.Sales / cashMax) * 100);
    }
}

public partial class DailyBar : ObservableObject
{
    public string  Label     { get; init; } = "";
    public decimal Amount    { get; init; }
    public double  BarHeight { get; init; }
    public string  AmountFmt => MockCatalog.FormatMoney(Amount);
}
public partial class HourBar : ObservableObject
{
    public string  Label     { get; init; } = "";
    public decimal Amount    { get; init; }
    public double  BarHeight { get; init; }
}
public partial class PaymentSlice : ObservableObject
{
    public string  Label  { get; init; } = "";
    public decimal Amount { get; init; }
    public string  Color  { get; init; } = "#155EEF";
    [ObservableProperty] private int _share;
    public string  AmountFmt => MockCatalog.FormatMoney(Amount);
}
public partial class TopProductRow : ObservableObject
{
    public int     Rank    { get; init; }
    public string  Name    { get; init; } = "";
    public int     Sold    { get; init; }
    public decimal Revenue { get; init; }
    [ObservableProperty] private double _barWidth;
    public string  RevenueFmt => MockCatalog.FormatMoney(Revenue);
}
public partial class CashierPerf : ObservableObject
{
    public string  Initials { get; init; } = "";
    public string  Name     { get; init; } = "";
    public decimal Sales    { get; init; }
    public int     Checks   { get; init; }
    public decimal Average  { get; init; }
    public int     Accent   { get; init; }
    [ObservableProperty] private int _share;
    public string  SalesFmt   => MockCatalog.FormatMoney(Sales);
    public string  AverageFmt => MockCatalog.FormatMoney(Average);
}

// ════════════════════════════════════════════════════════════════════
// 05 Mijozlar
// ════════════════════════════════════════════════════════════════════
public partial class MijozlarViewModel : BaseViewModel
{
    public ObservableCollection<CustomerRow> Customers { get; }

    public string KpiTotal  { get; } = "1 842";
    public string KpiNew    { get; } = "+86";
    public string KpiActive { get; } = "342";
    public string KpiDebt   { get; } = MockCatalog.FormatMoney(4_280_000m);
    public string WpfHint   { get; } = "DataGrid + custom DataGridTemplateColumn (avatar uchun). Yoki ListView + GridView. Daraja badge'lar — Border + Tag binding.";

    public MijozlarViewModel()
    {
        Customers = new ObservableCollection<CustomerRow>
        {
            new() { Initials="AK", Name="Alisher Karimov",     Phone="+998 90 123 45 67", Tier="Oltin",  Visits=42, LastVisit="14.05.2026", Total=8_240_000m, Debt=0m,        Accent=0 },
            new() { Initials="MY", Name="Malika Yusupova",     Phone="+998 90 765 43 21", Tier="Kumush", Visits=28, LastVisit="13.05.2026", Total=4_180_000m, Debt=0m,        Accent=1 },
            new() { Initials="BT", Name="Bobur Toshmatov",     Phone="+998 99 111 22 33", Tier="Oltin",  Visits=51, LastVisit="14.05.2026", Total=9_640_000m, Debt=240_000m,  Accent=2 },
            new() { Initials="DE", Name="Dilnoza Ergasheva",   Phone="+998 99 333 44 55", Tier="Bronza", Visits=14, LastVisit="12.05.2026", Total=1_280_000m, Debt=0m,        Accent=3 },
            new() { Initials="SH", Name="Sardor Holiqov",      Phone="+998 94 667 78 89", Tier="Kumush", Visits=22, LastVisit="13.05.2026", Total=3_420_000m, Debt=180_000m,  Accent=4 },
            new() { Initials="NH", Name="N. Hasanov",          Phone="+998 91 444 55 66", Tier="Bronza", Visits=6,  LastVisit="08.05.2026", Total=540_000m,   Debt=0m,        Accent=5 },
            new() { Initials="ZM", Name="Z. Mahmudova",        Phone="+998 93 888 99 00", Tier="Oltin",  Visits=68, LastVisit="14.05.2026", Total=12_840_000m,Debt=0m,        Accent=6 },
            new() { Initials="RE", Name="R. Ergasheva",        Phone="+998 90 222 33 44", Tier="Kumush", Visits=18, LastVisit="11.05.2026", Total=2_180_000m, Debt=420_000m,  Accent=0 },
            new() { Initials="JF", Name="J. Fayzullayev",      Phone="+998 91 777 88 99", Tier="Oltin",  Visits=37, LastVisit="14.05.2026", Total=6_240_000m, Debt=0m,        Accent=2 },
            new() { Initials="GI", Name="G. Iskandarova",      Phone="+998 99 456 78 90", Tier="Oltin",  Visits=44, LastVisit="13.05.2026", Total=7_180_000m, Debt=860_000m,  Accent=5 },
        };
    }
}

public partial class CustomerRow : ObservableObject
{
    public string  Initials  { get; init; } = "";
    public string  Name      { get; init; } = "";
    public string  Phone     { get; init; } = "";
    public string  Tier      { get; init; } = "Bronza";
    public int     Visits    { get; init; }
    public string  LastVisit { get; init; } = "";
    public decimal Total     { get; init; }
    public decimal Debt      { get; init; }
    public int     Accent    { get; init; }

    public string TotalFmt => MockCatalog.FormatMoney(Total);
    public string DebtFmt  => Debt == 0 ? "—" : MockCatalog.FormatMoney(Debt);
    public bool   HasDebt  => Debt > 0;
}

// ════════════════════════════════════════════════════════════════════
// 06 Yetkazib beruvchilar (Suppliers) — v1 unique module
// ════════════════════════════════════════════════════════════════════
public partial class YetkazibViewModel : BaseViewModel
{
    public ObservableCollection<SupplierRow> Suppliers { get; }

    public string KpiTotal  { get; } = "24";
    public string KpiActive { get; } = "19";
    public string KpiDebt   { get; } = MockCatalog.FormatMoney(18_420_000m);
    public string KpiOrders { get; } = "8";
    public string WpfHint   { get; } = "DataGrid + qator harakatlari (Button action column). Status — Border + DataTrigger orqali rang.";

    public YetkazibViewModel()
    {
        Suppliers = new ObservableCollection<SupplierRow>
        {
            new() { Code="YB-001", Name="Coca-Cola Uzbekistan",    Contact="+998 71 200 18 00", Category="Ichimliklar",      Orders=42, Debt=6_240_000m,  LastOrder="13 May",  Status="active"   },
            new() { Code="YB-002", Name="Toshkent Non Kombinati",   Contact="+998 71 234 56 78", Category="Non mahsulotlari", Orders=84, Debt=820_000m,    LastOrder="13 May",  Status="active"   },
            new() { Code="YB-003", Name="Sof Sut MChJ",             Contact="+998 99 111 22 33", Category="Sut mahsulotlari", Orders=36, Debt=1_240_000m,  LastOrder="12 May",  Status="active"   },
            new() { Code="YB-004", Name="Pepsi Bottlers",           Contact="+998 71 333 44 55", Category="Ichimliklar",      Orders=28, Debt=3_840_000m,  LastOrder="11 May",  Status="active"   },
            new() { Code="YB-005", Name="Dehqonbozor MChJ",         Contact="+998 90 444 55 66", Category="Sabzavotlar",      Orders=18, Debt=0m,           LastOrder="14 May",  Status="active"   },
            new() { Code="YB-006", Name="Anhor Fudz LLC",           Contact="+998 71 567 89 01", Category="Go'sht",           Orders=22, Debt=4_280_000m,  LastOrder="10 May",  Status="overdue"  },
            new() { Code="YB-007", Name="Lazzat Brio",              Contact="+998 99 666 77 88", Category="Sut mahsulotlari", Orders=14, Debt=0m,           LastOrder="07 May",  Status="active"   },
            new() { Code="YB-008", Name="Toshkent Mevalari",        Contact="+998 90 777 88 99", Category="Mevalar",          Orders=12, Debt=480_000m,    LastOrder="08 May",  Status="active"   },
            new() { Code="YB-009", Name="Eski Bozor MChJ",          Contact="+998 91 999 00 11", Category="Sabzavotlar",      Orders=4,  Debt=0m,           LastOrder="01 May",  Status="inactive" },
            new() { Code="YB-010", Name="Markaziy Don Markazi",     Contact="+998 71 888 99 00", Category="Don mahsulotlari", Orders=8,  Debt=1_520_000m,  LastOrder="11 May",  Status="active"   },
        };
    }
}

public partial class SupplierRow : ObservableObject
{
    public string  Code      { get; init; } = "";
    public string  Name      { get; init; } = "";
    public string  Contact   { get; init; } = "";
    public string  Category  { get; init; } = "";
    public int     Orders    { get; init; }
    public decimal Debt      { get; init; }
    public string  LastOrder { get; init; } = "";
    public string  Status    { get; init; } = "active";

    public string DebtFmt => Debt == 0 ? "—" : MockCatalog.FormatMoney(Debt);
    public bool   HasDebt => Debt > 0;
}

// ════════════════════════════════════════════════════════════════════
// 07 Xodimlar
// ════════════════════════════════════════════════════════════════════
public partial class XodimlarViewModel : BaseViewModel
{
    public ObservableCollection<EmployeeRow> Employees { get; }

    public string KpiTotal   { get; } = "12";
    public string KpiOnShift { get; } = "4";
    public string KpiPayroll { get; } = MockCatalog.FormatMoney(38_240_000m);
    public string KpiRate    { get; } = "94.2%";
    public string WpfHint    { get; } = "WrapPanel xodim kartalari uchun (200×220 px), Ellipse avatar uchun. Onlayn indikator — Ellipse z-index'da.";

    public XodimlarViewModel()
    {
        Employees = new ObservableCollection<EmployeeRow>
        {
            new() { Initials="MR", Name="Mavluda Rashidova", Role="Kassir",    Branch="Chilonzor", Phone="+998 90 123 45 67", Shift="Tongi (08-16)", Status="online",  Accent=0 },
            new() { Initials="BN", Name="Bobur Nazarov",     Role="Kassir",    Branch="Chilonzor", Phone="+998 90 765 43 21", Shift="Tongi (08-16)", Status="online",  Accent=1 },
            new() { Initials="AK", Name="Alisher Karimov",   Role="Menejer",   Branch="Chilonzor", Phone="+998 99 111 22 33", Shift="Doimiy",        Status="online",  Accent=2 },
            new() { Initials="DT", Name="Diyora Tursunova",  Role="Kassir",    Branch="Mirobod",   Phone="+998 99 333 44 55", Shift="Kechki (14-22)",Status="online",  Accent=3 },
            new() { Initials="SY", Name="Saidjon Yo'ldoshev",Role="Omborchi",  Branch="Mirobod",   Phone="+998 94 667 78 89", Shift="Tongi (08-16)", Status="offline", Accent=4 },
            new() { Initials="GS", Name="Gulchehra Saidova", Role="Kassir",    Branch="Yunusobod", Phone="+998 91 444 55 66", Shift="Kechki (14-22)",Status="online",  Accent=5 },
            new() { Initials="RM", Name="Ruslan Murodov",    Role="Kassir",    Branch="Yunusobod", Phone="+998 93 888 99 00", Shift="Tongi (08-16)", Status="leave",   Accent=6 },
            new() { Initials="NA", Name="Nodir Aliyev",      Role="Administrator", Branch="Bosh ofis", Phone="+998 90 555 66 77", Shift="Doimiy",   Status="online",  Accent=2 },
        };
    }
}

public partial class EmployeeRow : ObservableObject
{
    public string Initials { get; init; } = "";
    public string Name     { get; init; } = "";
    public string Role     { get; init; } = "";
    public string Branch   { get; init; } = "";
    public string Phone    { get; init; } = "";
    public string Shift    { get; init; } = "";
    public string Status   { get; init; } = "online";
    public int    Accent   { get; init; }
}

// ════════════════════════════════════════════════════════════════════
// 08 Sozlamalar
// ════════════════════════════════════════════════════════════════════
public partial class SozlamalarViewModel : BaseViewModel
{
    public ObservableCollection<SettingsTab> Tabs { get; }

    [ObservableProperty] private SettingsTab? _selectedTab;

    [ObservableProperty] private string _businessName = "ShefPos Demo MChJ";
    [ObservableProperty] private string _legalAddress = "Toshkent sh., Chilonzor t., Bunyodkor 18A";
    [ObservableProperty] private string _inn          = "302145678";
    [ObservableProperty] private string _phone        = "+998 71 200 18 18";

    [ObservableProperty] private bool _enableLoyalty       = true;
    [ObservableProperty] private bool _enableOnlinePayments= true;
    [ObservableProperty] private bool _autoPrintReceipt    = true;
    [ObservableProperty] private bool _autoLogout          = true;

    public string WpfHint { get; } = "Chap menyu — ListBox custom ItemContainerStyle bilan, asosiy maydon — ScrollViewer + StackPanel. Toggle — sizning ToggleSwitchStyle'ingiz allaqachon tayyor.";

    public SozlamalarViewModel()
    {
        // Segoe MDL2 Assets glyphs — guaranteed to render on Win10+.
        // Use \u escapes so the source itself encodes the codepoint and
        // is robust to any transit/editor that might strip raw Unicode.
        Tabs = new ObservableCollection<SettingsTab>
        {
            new() { Key="general",      Title="Umumiy",          Glyph="" }, // Settings
            new() { Key="branches",     Title="Filiallar",        Glyph="" }, // Map
            new() { Key="receipt",      Title="Chek shabloni",    Glyph="" }, // Print
            new() { Key="payments",     Title="To'lov tizimlari", Glyph="" }, // ShoppingCart
            new() { Key="hardware",     Title="Qurilmalar",       Glyph="" }, // StorageOptical
            new() { Key="security",     Title="Xavfsizlik",       Glyph="" }, // Lock
            new() { Key="audit",        Title="Audit jurnali",    Glyph="" }, // Memo
        };
        SelectedTab = Tabs[0];
    }
}

public partial class SettingsTab : ObservableObject
{
    public string Key   { get; init; } = "";
    public string Title { get; init; } = "";
    public string Glyph { get; init; } = "";
}
