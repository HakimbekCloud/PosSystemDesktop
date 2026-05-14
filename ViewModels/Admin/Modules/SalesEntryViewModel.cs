using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PosSystem.Core.Mock;

namespace PosSystem.ViewModels.Admin.Modules;

// Sales (chek) ro'yxati — admin perspektivasi. Bu sahifa kassa POS interfeysi
// emas; u smena davomidagi sotuvlarni umumiy ko'rinishda ko'rsatadi va
// kerak bo'lsa to'liq POS oynasini ochish uchun harakat tugmasi qoldiradi.
public partial class SalesEntryViewModel : BaseViewModel
{
    public ObservableCollection<SaleListRow> Sales { get; }

    public string KpiTotal     { get; } = MockCatalog.FormatMoney(8_240_000m);
    public string KpiCount     { get; } = "184";
    public string KpiAverage   { get; } = MockCatalog.FormatMoney(44_780m);
    public string KpiRefunds   { get; } = MockCatalog.FormatMoney(124_500m);

    public SalesEntryViewModel()
    {
        Sales = new ObservableCollection<SaleListRow>();
        var rnd = new Random(42);
        for (var i = 0; i < 48; i++)
        {
            var receipt = 04812 - i;
            var hour    = 14 - (i / 8);
            var minute  = 59 - ((i * 7) % 60);
            Sales.Add(new SaleListRow
            {
                Receipt = $"#{receipt:00000}",
                Time    = $"{hour:00}:{minute:00}",
                Cashier = MockCatalog.Cashiers[i % MockCatalog.Cashiers.Length],
                Items   = rnd.Next(1, 14),
                Payment = MockCatalog.PaymentMethods[i % MockCatalog.PaymentMethods.Length],
                Total   = rnd.Next(8, 320) * 1000m,
                Status  = i % 17 == 0 ? "refunded" : "completed",
            });
        }
    }
}

public partial class SaleListRow : ObservableObject
{
    public string  Receipt { get; init; } = "";
    public string  Time    { get; init; } = "";
    public string  Cashier { get; init; } = "";
    public int     Items   { get; init; }
    public string  Payment { get; init; } = "";
    public decimal Total   { get; init; }
    public string  Status  { get; init; } = "completed";

    public string TotalFormatted => MockCatalog.FormatMoney(Total);
    public bool   IsRefunded     => Status == "refunded";
}
