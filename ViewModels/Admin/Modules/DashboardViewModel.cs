using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PosSystem.Core.Mock;

namespace PosSystem.ViewModels.Admin.Modules;

// Landing page — KPI strip, recent receipts, attention list.
public partial class DashboardViewModel : BaseViewModel
{
    public string KpiRevenue       { get; } = MockCatalog.FormatMoney(8_240_000m);
    public string KpiRevenueDelta  { get; } = "▲ 12.4%";
    public string KpiRevenueSub    { get; } = "vs kecha";

    public string KpiReceipts      { get; } = "184";
    public string KpiReceiptsUnit  { get; } = "ta";
    public string KpiReceiptsDelta { get; } = "+8 chek";
    public string KpiReceiptsSub   { get; } = "bu smena";

    public string KpiAverage       { get; } = MockCatalog.FormatMoney(44_780m);
    public string KpiAverageDelta  { get; } = "▲ 3.1%";
    public string KpiAverageSub    { get; } = "bu hafta";

    public string KpiProfit        { get; } = MockCatalog.FormatMoney(2_140_000m);
    public string KpiProfitDelta   { get; } = "▼ 1.2%";
    public string KpiProfitSub     { get; } = "vs hafta o'rt.";

    public ObservableCollection<RecentSaleRow>  RecentSales { get; }
    public ObservableCollection<AttentionItem>  Attention   { get; }
    public ObservableCollection<HourlyBar>      HourlyBars  { get; }

    public DashboardViewModel()
    {
        RecentSales = new ObservableCollection<RecentSaleRow>
        {
            new() { Receipt = "#04812", Time = "14:22", Cashier = "M. Rashidova", Items = 3,  Payment = "Karta", Total = 41_500m  },
            new() { Receipt = "#04811", Time = "14:18", Cashier = "M. Rashidova", Items = 7,  Payment = "Naqd",  Total = 128_000m },
            new() { Receipt = "#04810", Time = "14:14", Cashier = "B. Nazarov",   Items = 2,  Payment = "Click", Total = 24_500m  },
            new() { Receipt = "#04809", Time = "14:09", Cashier = "M. Rashidova", Items = 5,  Payment = "Karta", Total = 86_000m  },
            new() { Receipt = "#04808", Time = "14:02", Cashier = "B. Nazarov",   Items = 1,  Payment = "Naqd",  Total = 14_000m  },
            new() { Receipt = "#04807", Time = "13:56", Cashier = "M. Rashidova", Items = 12, Payment = "Karta", Total = 246_000m },
            new() { Receipt = "#04806", Time = "13:51", Cashier = "A. Karimov",   Items = 4,  Payment = "Payme", Total = 68_500m  },
            new() { Receipt = "#04805", Time = "13:44", Cashier = "D. Tursunova", Items = 6,  Payment = "Karta", Total = 102_000m },
        };

        Attention = new ObservableCollection<AttentionItem>
        {
            new() { Severity = "danger",  Text = "Tovuq fileysi — 0 kg qoldi",          When = "12 daq oldin", Glyph = "" },
            new() { Severity = "warning", Text = "Baklajan — 8 kg, minimaldan past",     When = "34 daq oldin", Glyph = "" },
            new() { Severity = "warning", Text = "Pepsi 1.5L — 5 shisha qoldi",          When = "1 soat oldin", Glyph = "" },
            new() { Severity = "info",    Text = "KIR-00248 tasdiqlanishini kutmoqda",   When = "2 soat oldin", Glyph = "" },
            new() { Severity = "info",    Text = "Yangi yetkazib berish jadval qilindi", When = "3 soat oldin", Glyph = "" },
        };

        // 7 hours, 08:00 — 14:00. BarHeight precomputed so the view can bind
        // directly without a custom converter (max ≈ 180 px tall).
        decimal[] amounts = [240_000, 380_000, 580_000, 920_000, 1_240_000, 1_680_000, 1_840_000];
        decimal max = amounts.Max();
        HourlyBars = new ObservableCollection<HourlyBar>();
        for (var i = 0; i < amounts.Length; i++)
        {
            HourlyBars.Add(new HourlyBar
            {
                Hour      = (8 + i).ToString("00"),
                Amount    = amounts[i],
                BarHeight = Math.Max(8.0, (double)(amounts[i] / max) * 180.0),
            });
        }
    }
}

public partial class RecentSaleRow : ObservableObject
{
    public string  Receipt { get; init; } = "";
    public string  Time    { get; init; } = "";
    public string  Cashier { get; init; } = "";
    public int     Items   { get; init; }
    public string  Payment { get; init; } = "";
    public decimal Total   { get; init; }
    public string  TotalFormatted => MockCatalog.FormatMoney(Total);
}

public partial class AttentionItem : ObservableObject
{
    public string Severity { get; init; } = "info";
    public string Text     { get; init; } = "";
    public string When     { get; init; } = "";
    public string Glyph    { get; init; } = "";
}

public partial class HourlyBar : ObservableObject
{
    public string  Hour      { get; init; } = "";
    public decimal Amount    { get; init; }
    public double  BarHeight { get; init; }
}
