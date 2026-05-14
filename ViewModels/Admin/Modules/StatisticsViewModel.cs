using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PosSystem.Core.Mock;

namespace PosSystem.ViewModels.Admin.Modules;

public partial class StatisticsViewModel : BaseViewModel
{
    public ObservableCollection<DailyBar>        DailyRevenue { get; }
    public ObservableCollection<CategoryShare>   Categories   { get; }
    public ObservableCollection<TopProductRow>   TopProducts  { get; }
    public ObservableCollection<CashierPerf>     Cashiers     { get; }

    public string KpiPeriod        { get; } = "Bu hafta";
    public string KpiRevenue       { get; } = MockCatalog.FormatMoney(58_420_000m);
    public string KpiRevenueDelta  { get; } = "▲ 8.4%";
    public string KpiRevenueSub    { get; } = "o'tgan haftaga nisbatan";

    public string KpiOrders        { get; } = "1 248";
    public string KpiOrdersUnit    { get; } = "chek";
    public string KpiOrdersDelta   { get; } = "▲ 12%";

    public string KpiAverage       { get; } = MockCatalog.FormatMoney(46_820m);
    public string KpiAverageDelta  { get; } = "▲ 2.1%";

    public string KpiProfit        { get; } = MockCatalog.FormatMoney(14_840_000m);
    public string KpiProfitDelta   { get; } = "▼ 1.4%";

    public StatisticsViewModel()
    {
        var labels = new[] { "Du", "Se", "Ch", "Pa", "Ju", "Sh", "Ya" };
        var amounts = new[] { 6_840_000m, 7_220_000m, 8_440_000m, 9_120_000m, 10_240_000m, 8_960_000m, 7_600_000m };
        var max = amounts.Max();
        DailyRevenue = new ObservableCollection<DailyBar>();
        for (var i = 0; i < amounts.Length; i++)
            DailyRevenue.Add(new DailyBar
            {
                Label     = labels[i],
                Amount    = amounts[i],
                BarHeight = Math.Max(8.0, (double)(amounts[i] / max) * 200.0),
            });

        Categories = new ObservableCollection<CategoryShare>
        {
            new() { Name="Ichimliklar",       Share=28, Amount=16_360_000m, Color="#155EEF" },
            new() { Name="Sabzavotlar",       Share=22, Amount=12_850_000m, Color="#22C55E" },
            new() { Name="Don mahsulotlari",  Share=18, Amount=10_510_000m, Color="#F59E0B" },
            new() { Name="Sut mahsulotlari",  Share=12, Amount= 7_010_000m, Color="#3B82F6" },
            new() { Name="Mevalar",           Share= 9, Amount= 5_260_000m, Color="#EC4899" },
            new() { Name="Boshqalar",         Share=11, Amount= 6_430_000m, Color="#94A3B8" },
        };

        TopProducts = new ObservableCollection<TopProductRow>
        {
            new() { Rank=1,  Name="Coca-Cola 0.5L",     Sold=842, Revenue=6_736_000m, Trend=+18 },
            new() { Rank=2,  Name="Aqua 1.5L",          Sold=620, Revenue=3_100_000m, Trend=+12 },
            new() { Rank=3,  Name="Non (katta)",        Sold=518, Revenue=2_072_000m, Trend=+4  },
            new() { Rank=4,  Name="Sprite 0.5L",        Sold=412, Revenue=3_090_000m, Trend=+8  },
            new() { Rank=5,  Name="Kartoshka 1kg",      Sold=388, Revenue=2_328_000m, Trend=-3  },
            new() { Rank=6,  Name="Olma 1kg",           Sold=312, Revenue=5_616_000m, Trend=+6  },
            new() { Rank=7,  Name="Tuxum (10 dona)",    Sold=284, Revenue=6_248_000m, Trend=+2  },
            new() { Rank=8,  Name="Sut 1L",             Sold=242, Revenue=2_904_000m, Trend=+9  },
            new() { Rank=9,  Name="Guruch 1kg",         Sold=218, Revenue=3_270_000m, Trend=+1  },
            new() { Rank=10, Name="O'simlik yog'i 1L",  Sold=184, Revenue=4_600_000m, Trend=+3  },
        };

        Cashiers = new ObservableCollection<CashierPerf>
        {
            new() { Initials="MR", Name="M. Rashidova", Receipts=342, Revenue=18_420_000m, Average=53_860m, Accent=0 },
            new() { Initials="BN", Name="B. Nazarov",   Receipts=284, Revenue=14_220_000m, Average=50_070m, Accent=1 },
            new() { Initials="DT", Name="D. Tursunova", Receipts=246, Revenue=12_840_000m, Average=52_190m, Accent=3 },
            new() { Initials="RM", Name="R. Murodova",  Receipts=212, Revenue=11_180_000m, Average=52_730m, Accent=6 },
            new() { Initials="GS", Name="G. Saidova",   Receipts=194, Revenue=10_240_000m, Average=52_780m, Accent=5 },
            new() { Initials="MX", Name="M. Xudoyberdiev",Receipts=182, Revenue=9_840_000m, Average=54_070m, Accent=3 },
        };
    }
}

public partial class DailyBar : ObservableObject
{
    public string  Label     { get; init; } = "";
    public decimal Amount    { get; init; }
    public double  BarHeight { get; init; }
    public string  AmountFormatted => MockCatalog.FormatMoney(Amount);
}

public partial class CategoryShare : ObservableObject
{
    public string  Name   { get; init; } = "";
    public int     Share  { get; init; }
    public decimal Amount { get; init; }
    public string  Color  { get; init; } = "#155EEF";
    public string  AmountFormatted => MockCatalog.FormatMoney(Amount);
    public string  ShareFormatted  => Share + "%";
}

public partial class TopProductRow : ObservableObject
{
    public int     Rank    { get; init; }
    public string  Name    { get; init; } = "";
    public int     Sold    { get; init; }
    public decimal Revenue { get; init; }
    public int     Trend   { get; init; }
    public string  RevenueFormatted => MockCatalog.FormatMoney(Revenue);
    public string  TrendFormatted   => (Trend >= 0 ? "+" : "") + Trend + "%";
}

public partial class CashierPerf : ObservableObject
{
    public string  Initials { get; init; } = "";
    public string  Name     { get; init; } = "";
    public int     Receipts { get; init; }
    public decimal Revenue  { get; init; }
    public decimal Average  { get; init; }
    public int     Accent   { get; init; }
    public string  RevenueFormatted => MockCatalog.FormatMoney(Revenue);
    public string  AverageFormatted => MockCatalog.FormatMoney(Average);
}
