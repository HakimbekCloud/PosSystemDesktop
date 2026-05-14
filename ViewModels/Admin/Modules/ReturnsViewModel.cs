using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PosSystem.Core.Mock;

namespace PosSystem.ViewModels.Admin.Modules;

public partial class ReturnsViewModel : BaseViewModel
{
    public ObservableCollection<ReturnRow> Returns { get; }
    public ObservableCollection<string>    StatusFilters { get; } = ["Barchasi", "Kutilmoqda", "Tasdiqlangan", "Rad etilgan"];

    [ObservableProperty] private string _statusFilter = "Barchasi";
    [ObservableProperty] private string _searchQuery  = "";

    // KPIs for the top row
    public string KpiToday        { get; } = "4";
    public string KpiTodayUnit    { get; } = "ta";
    public string KpiTodaySub     { get; } = "bugun ro'yxatga olingan";

    public string KpiAmount       { get; } = MockCatalog.FormatMoney(412_500m);
    public string KpiAmountSub    { get; } = "bu hafta qaytarilgan";

    public string KpiPending      { get; } = "2";
    public string KpiPendingUnit  { get; } = "ta";
    public string KpiPendingSub   { get; } = "tasdiqlash kerak";

    public string KpiRate         { get; } = "1.8%";
    public string KpiRateSub      { get; } = "sotuvga nisbati";

    public ReturnsViewModel()
    {
        Returns = new ObservableCollection<ReturnRow>
        {
            new() { Number="QYT-0142", Date="14.05.2026 13:42", OriginalReceipt="#04787", Customer="Alisher Karimov",   Cashier="M. Rashidova", Items=2, Reason="Yaroqsiz mahsulot",   Total=86_000m,  Status="pending"   },
            new() { Number="QYT-0141", Date="14.05.2026 11:18", OriginalReceipt="#04752", Customer="Malika Yusupova",   Cashier="B. Nazarov",   Items=1, Reason="Mijoz qabul qilmadi",  Total=42_000m,  Status="approved"  },
            new() { Number="QYT-0140", Date="14.05.2026 09:44", OriginalReceipt="#04731", Customer="Bobur Toshmatov",   Cashier="A. Karimov",   Items=4, Reason="Noto'g'ri tovar",      Total=164_000m, Status="approved"  },
            new() { Number="QYT-0139", Date="13.05.2026 18:12", OriginalReceipt="#04688", Customer="Dilnoza Ergasheva", Cashier="M. Rashidova", Items=1, Reason="Yaroqsiz mahsulot",   Total=24_000m,  Status="pending"   },
            new() { Number="QYT-0138", Date="13.05.2026 16:30", OriginalReceipt="#04654", Customer="Sardor Holiqov",    Cashier="G. Saidova",   Items=3, Reason="Mijoz ikkilanishi",   Total=98_500m,  Status="approved"  },
            new() { Number="QYT-0137", Date="13.05.2026 14:21", OriginalReceipt="#04612", Customer="N. Hasanov",        Cashier="D. Tursunova", Items=2, Reason="Yaroqsiz mahsulot",   Total=58_000m,  Status="rejected"  },
            new() { Number="QYT-0136", Date="12.05.2026 17:54", OriginalReceipt="#04498", Customer="Z. Mahmudova",      Cashier="B. Nazarov",   Items=1, Reason="O'zgartirish so'rovi", Total=18_500m,  Status="approved"  },
            new() { Number="QYT-0135", Date="12.05.2026 12:08", OriginalReceipt="#04412", Customer="R. Ergasheva",      Cashier="M. Rashidova", Items=5, Reason="Noto'g'ri tovar",      Total=212_000m, Status="approved"  },
            new() { Number="QYT-0134", Date="12.05.2026 10:32", OriginalReceipt="#04388", Customer="K. Tursunov",       Cashier="A. Karimov",   Items=2, Reason="Mijoz qabul qilmadi",  Total=44_000m,  Status="approved"  },
            new() { Number="QYT-0133", Date="11.05.2026 19:14", OriginalReceipt="#04321", Customer="Naqd mijoz",         Cashier="G. Saidova",   Items=1, Reason="Yaroqsiz mahsulot",   Total=12_500m,  Status="approved"  },
        };
    }
}

public partial class ReturnRow : ObservableObject
{
    public string  Number          { get; init; } = "";
    public string  Date            { get; init; } = "";
    public string  OriginalReceipt { get; init; } = "";
    public string  Customer        { get; init; } = "";
    public string  Cashier         { get; init; } = "";
    public int     Items           { get; init; }
    public string  Reason          { get; init; } = "";
    public decimal Total           { get; init; }
    public string  Status          { get; init; } = "pending";

    public string TotalFormatted => MockCatalog.FormatMoney(Total);
}
