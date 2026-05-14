using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PosSystem.Core.Mock;

namespace PosSystem.ViewModels.Admin.Modules;

public partial class InventoryViewModel : BaseViewModel
{
    public ObservableCollection<InventoryMovement> Movements { get; }
    public ObservableCollection<StockLine>         StockLines { get; }
    public ObservableCollection<StockAlert>        Alerts     { get; }

    public string KpiValue      { get; } = MockCatalog.FormatMoney(184_220_000m);
    public string KpiValueDelta { get; } = "▲ 4.2%";

    public string KpiInbound    { get; } = MockCatalog.FormatMoney(12_400_000m);
    public string KpiInboundSub { get; } = "bu hafta kirim";

    public string KpiOutbound   { get; } = MockCatalog.FormatMoney(9_820_000m);
    public string KpiOutboundSub{ get; } = "bu hafta chiqim";

    public string KpiAlerts     { get; } = "12";
    public string KpiAlertsSub  { get; } = "kam yoki tugagan";

    public InventoryViewModel()
    {
        Movements = new ObservableCollection<InventoryMovement>
        {
            new() { Number="KIR-00248", Type="Kirim",       Date="14.05.2026 11:14", Partner="Coca-Cola Bottlers", Items=24, Total=4_280_000m, Status="approved" },
            new() { Number="CHQ-00712", Type="Chiqim",      Date="14.05.2026 09:42", Partner="Sotuv (kassa)",      Items=58, Total=1_240_000m, Status="approved" },
            new() { Number="INV-00031", Type="Inventarizatsiya", Date="13.05.2026 21:00", Partner="Ichki",         Items=412, Total=0m,        Status="approved" },
            new() { Number="KIR-00247", Type="Kirim",       Date="13.05.2026 14:08", Partner="Anhor Fudz LLC",     Items=18, Total=3_120_000m, Status="approved" },
            new() { Number="KIR-00246", Type="Kirim",       Date="13.05.2026 10:42", Partner="Mahsulot Hub",       Items=22, Total=2_780_000m, Status="pending"  },
            new() { Number="CHQ-00711", Type="Chiqim",      Date="13.05.2026 09:21", Partner="Filiallar aro",      Items=14, Total=620_000m,   Status="approved" },
            new() { Number="KIR-00245", Type="Kirim",       Date="12.05.2026 16:48", Partner="Dehqonbozor MChJ",   Items=36, Total=4_640_000m, Status="approved" },
            new() { Number="CHQ-00710", Type="Yaroqsiz",    Date="12.05.2026 12:14", Partner="Yaroqsiz akti",      Items=4,  Total=92_000m,    Status="approved" },
        };

        StockLines = new ObservableCollection<StockLine>
        {
            new() { Code="CC-001", Name="Coca-Cola 0.5L",        Category="Ichimliklar",   Stock=120, Min=20,  Unit="dona", Value=660_000m,    Trend=+12 },
            new() { Code="AQ-001", Name="Aqua 1.5L",             Category="Ichimliklar",   Stock=200, Min=40,  Unit="dona", Value=680_000m,    Trend=+8  },
            new() { Code="PT-001", Name="Kartoshka 1kg",         Category="Sabzavotlar",   Stock=150, Min=40,  Unit="kg",   Value=570_000m,    Trend=-4  },
            new() { Code="ON-001", Name="Piyoz 1kg",             Category="Sabzavotlar",   Stock=90,  Min=30,  Unit="kg",   Value=252_000m,    Trend=+3  },
            new() { Code="SP-001", Name="Sprite 0.5L",           Category="Ichimliklar",   Stock=85,  Min=20,  Unit="dona", Value=442_000m,    Trend=+5  },
            new() { Code="RC-001", Name="Guruch 1kg",            Category="Don mahsulotlari",Stock=80,Min=30,  Unit="kg",   Value=816_000m,    Trend=0   },
            new() { Code="PS-001", Name="Makaron 400g",          Category="Don mahsulotlari",Stock=70,Min=20,  Unit="dona", Value=378_000m,    Trend=-6  },
            new() { Code="FL-001", Name="Un 2kg",                Category="Don mahsulotlari",Stock=60,Min=20,  Unit="kg",   Value=720_000m,    Trend=+2  },
            new() { Code="BR-001", Name="Non (katta)",           Category="Non mahsulotlari",Stock=50,Min=20,  Unit="dona", Value=120_000m,    Trend=+9  },
            new() { Code="OL-001", Name="O'simlik yog'i 1L",     Category="Moy",           Stock=45,  Min=15,  Unit="litr", Value=787_500m,    Trend=+1  },
            new() { Code="AP-001", Name="Olma 1kg",              Category="Mevalar",       Stock=40,  Min=15,  Unit="kg",   Value=480_000m,    Trend=-3  },
            new() { Code="ML-001", Name="Sut 1L",                Category="Sut mahsulotlari",Stock=40,Min=15,  Unit="litr", Value=312_000m,    Trend=+4  },
            new() { Code="EG-001", Name="Tuxum (10 dona)",       Category="Tuxum",         Stock=25,  Min=10,  Unit="quti", Value=395_000m,    Trend=+2  },
            new() { Code="BN-001", Name="Banan Ekvador 1kg",     Category="Mevalar",       Stock=18,  Min=10,  Unit="kg",   Value=261_000m,    Trend=+6  },
            new() { Code="TM-001", Name="Pomidor 1kg",           Category="Sabzavotlar",   Stock=14,  Min=20,  Unit="kg",   Value=112_000m,    Trend=-12 },
            new() { Code="PP-001", Name="Pepsi 0.5L",            Category="Ichimliklar",   Stock=12,  Min=20,  Unit="dona", Value=58_800m,     Trend=-22 },
            new() { Code="CK-001", Name="Tort \"Medovik\"",      Category="Shirinliklar",  Stock=10,  Min=4,   Unit="dona", Value=280_000m,    Trend=+1  },
            new() { Code="YG-001", Name="Qatiq 500g",            Category="Sut mahsulotlari",Stock=8, Min=12,  Unit="dona", Value=47_200m,     Trend=-8  },
            new() { Code="SG-001", Name="Shakar 1kg",            Category="Don mahsulotlari",Stock=0, Min=20,  Unit="kg",   Value=0m,          Trend=-30 },
            new() { Code="CH-001", Name="Tovuq fileysi 1kg",     Category="Sabzavotlar",   Stock=0,   Min=10,  Unit="kg",   Value=0m,          Trend=-40 },
        };

        Alerts = new ObservableCollection<StockAlert>
        {
            new() { Severity="danger",  Code="CH-001", Name="Tovuq fileysi 1kg", Detail="0 kg / kerakli minimum: 10 kg", When="12 daq oldin" },
            new() { Severity="danger",  Code="SG-001", Name="Shakar 1kg",        Detail="0 kg / kerakli minimum: 20 kg", When="34 daq oldin" },
            new() { Severity="warning", Code="PP-001", Name="Pepsi 0.5L",        Detail="12 dona / minimum: 20 dona",     When="1 soat oldin" },
            new() { Severity="warning", Code="TM-001", Name="Pomidor 1kg",       Detail="14 kg / minimum: 20 kg",         When="1 soat oldin" },
            new() { Severity="warning", Code="YG-001", Name="Qatiq 500g",        Detail="8 dona / minimum: 12 dona",      When="2 soat oldin" },
            new() { Severity="info",    Code="KIR-00246", Name="Mahsulot Hub kirim", Detail="22 ta nomenklatura tasdiqlanishi kutilmoqda", When="3 soat oldin" },
        };
    }
}

public partial class InventoryMovement : ObservableObject
{
    public string  Number  { get; init; } = "";
    public string  Type    { get; init; } = "";
    public string  Date    { get; init; } = "";
    public string  Partner { get; init; } = "";
    public int     Items   { get; init; }
    public decimal Total   { get; init; }
    public string  Status  { get; init; } = "approved";

    public string TotalFormatted => Total == 0 ? "—" : MockCatalog.FormatMoney(Total);
}

public partial class StockLine : ObservableObject
{
    public string  Code     { get; init; } = "";
    public string  Name     { get; init; } = "";
    public string  Category { get; init; } = "";
    public int     Stock    { get; init; }
    public int     Min      { get; init; }
    public string  Unit     { get; init; } = "";
    public decimal Value    { get; init; }
    public int     Trend    { get; init; }

    public string ValueFormatted => MockCatalog.FormatMoney(Value);
    public string StockFormatted => Stock + " " + Unit;
    public string TrendFormatted => (Trend >= 0 ? "+" : "") + Trend + "%";
    public string Status         => Stock == 0 ? "out" : Stock < Min ? "low" : "active";
}

public partial class StockAlert : ObservableObject
{
    public string Severity { get; init; } = "info";
    public string Code     { get; init; } = "";
    public string Name     { get; init; } = "";
    public string Detail   { get; init; } = "";
    public string When     { get; init; } = "";
}
