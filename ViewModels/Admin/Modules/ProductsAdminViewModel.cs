using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PosSystem.Core.Mock;

namespace PosSystem.ViewModels.Admin.Modules;

public partial class ProductsAdminViewModel : BaseViewModel
{
    public ObservableCollection<ProductRow> Products  { get; }
    public ObservableCollection<string>     Categories { get; }

    [ObservableProperty] private string _selectedCategory = "Barchasi";
    [ObservableProperty] private string _searchQuery      = "";

    public string KpiTotal      { get; } = "428";
    public string KpiTotalUnit  { get; } = "ta";
    public string KpiTotalSub   { get; } = "10 kategoriya";

    public string KpiValue      { get; } = MockCatalog.FormatMoney(184_220_000m);
    public string KpiValueDelta { get; } = "▲ 8.2%";
    public string KpiValueSub   { get; } = "bu hafta";

    public string KpiLow        { get; } = "18";
    public string KpiLowUnit    { get; } = "ta";
    public string KpiLowSub     { get; } = "diqqat kerak";

    public string KpiOut        { get; } = "4";
    public string KpiOutUnit    { get; } = "ta";
    public string KpiOutSub     { get; } = "zudlik bilan to'ldirish";

    public ProductsAdminViewModel()
    {
        Categories = new ObservableCollection<string>(MockCatalog.Categories);

        Products = new ObservableCollection<ProductRow>
        {
            new() { Code="CC-001", Name="Coca-Cola 0.5L",         Category="Ichimliklar",    Unit="dona", Price=8_000m,  Cost=5_500m,  Stock=120, Min=20,  Status="active" },
            new() { Code="SP-001", Name="Sprite 0.5L",            Category="Ichimliklar",    Unit="dona", Price=7_500m,  Cost=5_200m,  Stock=85,  Min=20,  Status="active" },
            new() { Code="PP-001", Name="Pepsi 0.5L",             Category="Ichimliklar",    Unit="dona", Price=7_000m,  Cost=4_900m,  Stock=12,  Min=20,  Status="low"    },
            new() { Code="AQ-001", Name="Aqua 1.5L",              Category="Ichimliklar",    Unit="dona", Price=5_000m,  Cost=3_400m,  Stock=200, Min=40,  Status="active" },
            new() { Code="BR-001", Name="Non (katta)",            Category="Non mahsulotlari",Unit="dona", Price=4_000m,  Cost=2_400m,  Stock=50,  Min=20,  Status="active" },
            new() { Code="CK-001", Name="Tort \"Medovik\"",       Category="Shirinliklar",   Unit="dona", Price=45_000m, Cost=28_000m, Stock=10,  Min=4,   Status="active" },
            new() { Code="ML-001", Name="Sut 1L",                 Category="Sut mahsulotlari",Unit="litr",Price=12_000m, Cost=7_800m,  Stock=40,  Min=15,  Status="active" },
            new() { Code="YG-001", Name="Qatiq 500g",             Category="Sut mahsulotlari",Unit="dona", Price=9_000m,  Cost=5_900m,  Stock=8,   Min=12,  Status="low"    },
            new() { Code="EG-001", Name="Tuxum (10 dona)",        Category="Tuxum",          Unit="quti", Price=22_000m, Cost=15_800m, Stock=25,  Min=10,  Status="active" },
            new() { Code="RC-001", Name="Guruch lazer 1kg",       Category="Don mahsulotlari",Unit="kg",  Price=15_000m, Cost=10_200m, Stock=80,  Min=30,  Status="active" },
            new() { Code="FL-001", Name="Un oliy nav 2kg",        Category="Don mahsulotlari",Unit="kg",  Price=18_000m, Cost=12_000m, Stock=60,  Min=20,  Status="active" },
            new() { Code="SG-001", Name="Shakar 1kg",             Category="Don mahsulotlari",Unit="kg",  Price=14_000m, Cost=9_500m,  Stock=0,   Min=20,  Status="out"    },
            new() { Code="OL-001", Name="O'simlik yog'i 1L",      Category="Moy",            Unit="litr", Price=25_000m, Cost=17_500m, Stock=45,  Min=15,  Status="active" },
            new() { Code="PS-001", Name="Makaron 400g",           Category="Don mahsulotlari",Unit="dona",Price=8_500m,  Cost=5_400m,  Stock=70,  Min=20,  Status="active" },
            new() { Code="TM-001", Name="Pomidor (yumshoq) 1kg",  Category="Sabzavotlar",    Unit="kg",   Price=12_000m, Cost=8_000m,  Stock=14,  Min=20,  Status="low"    },
            new() { Code="PT-001", Name="Kartoshka qaynama 1kg",  Category="Sabzavotlar",    Unit="kg",   Price=6_000m,  Cost=3_800m,  Stock=150, Min=40,  Status="active" },
            new() { Code="ON-001", Name="Piyoz oddiy 1kg",        Category="Sabzavotlar",    Unit="kg",   Price=4_500m,  Cost=2_800m,  Stock=90,  Min=30,  Status="active" },
            new() { Code="AP-001", Name="Olma sariq 1kg",         Category="Mevalar",        Unit="kg",   Price=18_000m, Cost=12_000m, Stock=40,  Min=15,  Status="active" },
            new() { Code="BN-001", Name="Banan Ekvador 1kg",      Category="Mevalar",        Unit="kg",   Price=22_000m, Cost=14_500m, Stock=18,  Min=10,  Status="active" },
            new() { Code="CH-001", Name="Tovuq fileysi 1kg",      Category="Sabzavotlar",    Unit="kg",   Price=55_000m, Cost=38_000m, Stock=0,   Min=10,  Status="out"    },
            new() { Code="MZ-001", Name="Mol go'shti 1kg",        Category="Sabzavotlar",    Unit="kg",   Price=92_000m, Cost=68_000m, Stock=22,  Min=8,   Status="active" },
            new() { Code="CB-001", Name="Karam 1kg",              Category="Sabzavotlar",    Unit="kg",   Price=5_500m,  Cost=3_200m,  Stock=35,  Min=15,  Status="active" },
        };
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
    public int     Min      { get; init; }
    public string  Status   { get; init; } = "active";

    public string  PriceFormatted => MockCatalog.FormatMoney(Price);
    public string  CostFormatted  => MockCatalog.FormatMoney(Cost);
    public decimal Margin         => Price == 0 ? 0 : Math.Round((Price - Cost) / Price * 100m, 1);
    public string  MarginFormatted => Margin.ToString("0.0") + "%";
    public string  StockFormatted  => Stock + " " + Unit;
}
