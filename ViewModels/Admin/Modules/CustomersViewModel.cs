using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PosSystem.Core.Mock;

namespace PosSystem.ViewModels.Admin.Modules;

public partial class CustomersViewModel : BaseViewModel
{
    public ObservableCollection<CustomerRow> Customers { get; }

    public string KpiTotal       { get; } = "1 842";
    public string KpiTotalUnit   { get; } = "mijoz";
    public string KpiTotalSub    { get; } = "bazada ro'yxatdan o'tgan";

    public string KpiNew         { get; } = "+86";
    public string KpiNewSub      { get; } = "shu oyda qo'shilgan";

    public string KpiDebt        { get; } = MockCatalog.FormatMoney(4_280_000m);
    public string KpiDebtSub     { get; } = "umumiy qarzdorlik";

    public string KpiActive      { get; } = "342";
    public string KpiActiveUnit  { get; } = "ta";
    public string KpiActiveSub   { get; } = "haftada xarid qilgan";

    public CustomersViewModel()
    {
        Customers = new ObservableCollection<CustomerRow>
        {
            new() { Initials="AK", Name="Alisher Karimov",     Phone="+998 90 123 45 67", Tier="Oltin",   Visits=42, LastVisit="14.05.2026", Total=8_240_000m, Debt=0m,         Accent=0 },
            new() { Initials="MY", Name="Malika Yusupova",     Phone="+998 90 765 43 21", Tier="Kumush",  Visits=28, LastVisit="13.05.2026", Total=4_180_000m, Debt=0m,         Accent=1 },
            new() { Initials="BT", Name="Bobur Toshmatov",     Phone="+998 99 111 22 33", Tier="Oltin",   Visits=51, LastVisit="14.05.2026", Total=9_640_000m, Debt=240_000m,   Accent=2 },
            new() { Initials="DE", Name="Dilnoza Ergasheva",   Phone="+998 99 333 44 55", Tier="Bronza",  Visits=14, LastVisit="12.05.2026", Total=1_280_000m, Debt=0m,         Accent=3 },
            new() { Initials="SH", Name="Sardor Holiqov",      Phone="+998 94 667 78 89", Tier="Kumush",  Visits=22, LastVisit="13.05.2026", Total=3_420_000m, Debt=180_000m,   Accent=4 },
            new() { Initials="NH", Name="N. Hasanov",          Phone="+998 91 444 55 66", Tier="Bronza",  Visits=6,  LastVisit="08.05.2026", Total=540_000m,   Debt=0m,         Accent=5 },
            new() { Initials="ZM", Name="Z. Mahmudova",        Phone="+998 93 888 99 00", Tier="Oltin",   Visits=68, LastVisit="14.05.2026", Total=12_840_000m,Debt=0m,         Accent=6 },
            new() { Initials="RE", Name="R. Ergasheva",        Phone="+998 90 222 33 44", Tier="Kumush",  Visits=18, LastVisit="11.05.2026", Total=2_180_000m, Debt=420_000m,   Accent=0 },
            new() { Initials="KT", Name="K. Tursunov",         Phone="+998 90 555 66 77", Tier="Bronza",  Visits=4,  LastVisit="06.05.2026", Total=320_000m,   Debt=0m,         Accent=1 },
            new() { Initials="JF", Name="J. Fayzullayev",      Phone="+998 91 777 88 99", Tier="Oltin",   Visits=37, LastVisit="14.05.2026", Total=6_240_000m, Debt=0m,         Accent=2 },
            new() { Initials="OB", Name="O. Bozorov",          Phone="+998 99 234 56 78", Tier="Kumush",  Visits=12, LastVisit="10.05.2026", Total=1_640_000m, Debt=0m,         Accent=3 },
            new() { Initials="LK", Name="L. Karimova",         Phone="+998 90 345 67 89", Tier="Bronza",  Visits=2,  LastVisit="04.05.2026", Total=140_000m,   Debt=0m,         Accent=4 },
            new() { Initials="GI", Name="G. Iskandarova",      Phone="+998 99 456 78 90", Tier="Oltin",   Visits=44, LastVisit="13.05.2026", Total=7_180_000m, Debt=860_000m,   Accent=5 },
            new() { Initials="AM", Name="A. Mansurov",         Phone="+998 90 567 89 01", Tier="Kumush",  Visits=24, LastVisit="12.05.2026", Total=2_980_000m, Debt=0m,         Accent=6 },
            new() { Initials="SR", Name="S. Rashidova",        Phone="+998 91 678 90 12", Tier="Bronza",  Visits=8,  LastVisit="09.05.2026", Total=720_000m,   Debt=0m,         Accent=0 },
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

    public string TotalFormatted => MockCatalog.FormatMoney(Total);
    public string DebtFormatted  => Debt == 0 ? "—" : MockCatalog.FormatMoney(Debt);
    public bool   HasDebt        => Debt > 0;
}
