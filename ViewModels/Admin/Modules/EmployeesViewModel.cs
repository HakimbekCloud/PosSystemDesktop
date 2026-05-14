using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PosSystem.Core.Mock;

namespace PosSystem.ViewModels.Admin.Modules;

public partial class EmployeesViewModel : BaseViewModel
{
    public ObservableCollection<EmployeeRow> Employees { get; }

    public string KpiTotal       { get; } = "12";
    public string KpiTotalUnit   { get; } = "xodim";
    public string KpiTotalSub    { get; } = "barchasi faol";

    public string KpiOnShift     { get; } = "4";
    public string KpiOnShiftUnit { get; } = "ta";
    public string KpiOnShiftSub  { get; } = "hozir smenada";

    public string KpiPayroll     { get; } = MockCatalog.FormatMoney(38_240_000m);
    public string KpiPayrollSub  { get; } = "oylik fondi";

    public string KpiAverage     { get; } = "94.2%";
    public string KpiAverageSub  { get; } = "davomat ko'rsatkichi";

    public EmployeesViewModel()
    {
        Employees = new ObservableCollection<EmployeeRow>
        {
            new() { Initials="MR", Name="M. Rashidova",  Role="Kassir",        Branch="Chilonzor",   Phone="+998 90 123 45 67", Sales=18_420_000m, Shift="Tongi (08-16)", Status="active",    Accent=0 },
            new() { Initials="BN", Name="B. Nazarov",    Role="Kassir",        Branch="Chilonzor",   Phone="+998 90 765 43 21", Sales=14_220_000m, Shift="Tongi (08-16)", Status="active",    Accent=1 },
            new() { Initials="AK", Name="A. Karimov",    Role="Menejer",       Branch="Chilonzor",   Phone="+998 99 111 22 33", Sales=0m,          Shift="Doimiy",        Status="active",    Accent=2 },
            new() { Initials="DT", Name="D. Tursunova",  Role="Kassir",        Branch="Mirobod",     Phone="+998 99 333 44 55", Sales=12_840_000m, Shift="Kechki (14-22)",Status="active",    Accent=3 },
            new() { Initials="SY", Name="S. Yo'ldoshev", Role="Omborchi",      Branch="Mirobod",     Phone="+998 94 667 78 89", Sales=0m,          Shift="Tongi (08-16)", Status="active",    Accent=4 },
            new() { Initials="GS", Name="G. Saidova",    Role="Kassir",        Branch="Yunusobod",   Phone="+998 91 444 55 66", Sales=10_240_000m, Shift="Kechki (14-22)",Status="active",    Accent=5 },
            new() { Initials="RM", Name="R. Murodova",   Role="Kassir",        Branch="Yunusobod",   Phone="+998 93 888 99 00", Sales=11_180_000m, Shift="Tongi (08-16)", Status="leave",     Accent=6 },
            new() { Initials="TS", Name="T. Sodiqov",    Role="Yetkazib beruvchi", Branch="Sergeli", Phone="+998 90 222 33 44", Sales=0m,          Shift="Moslashuvchan", Status="active",    Accent=0 },
            new() { Initials="NA", Name="N. Aliyev",     Role="Administrator", Branch="Bosh ofis",   Phone="+998 90 555 66 77", Sales=0m,          Shift="Doimiy",        Status="active",    Accent=2 },
            new() { Initials="MX", Name="M. Xudoyberdiev",Role="Kassir",       Branch="Sergeli",     Phone="+998 91 777 88 99", Sales=9_840_000m,  Shift="Tongi (08-16)", Status="active",    Accent=3 },
            new() { Initials="ZK", Name="Z. Kamilov",    Role="Menejer",       Branch="Mirobod",     Phone="+998 99 234 56 78", Sales=0m,          Shift="Doimiy",        Status="active",    Accent=4 },
            new() { Initials="ON", Name="O. Nasriddinov",Role="Kassir",        Branch="Sergeli",     Phone="+998 90 345 67 89", Sales=8_640_000m,  Shift="Kechki (14-22)",Status="probation", Accent=5 },
        };
    }
}

public partial class EmployeeRow : ObservableObject
{
    public string  Initials { get; init; } = "";
    public string  Name     { get; init; } = "";
    public string  Role     { get; init; } = "";
    public string  Branch   { get; init; } = "";
    public string  Phone    { get; init; } = "";
    public decimal Sales    { get; init; }
    public string  Shift    { get; init; } = "";
    public string  Status   { get; init; } = "active";
    public int     Accent   { get; init; }

    public string SalesFormatted => Sales == 0 ? "—" : MockCatalog.FormatMoney(Sales);
}
