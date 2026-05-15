namespace PosSystem.Core.Mock;

// Shared mock fixtures for the v1 admin panel. Reading these stays local;
// once a backend exists, replace this layer with real repository calls.
public static class MockCatalog
{
    public static readonly string[] Branches =
    [
        "Chilonzor filiali",
        "Mirobod filiali",
        "Yunusobod filiali",
        "Sergeli filiali",
    ];

    public static readonly string[] Categories =
    [
        "Barchasi",
        "Ichimliklar",
        "Non mahsulotlari",
        "Sut mahsulotlari",
        "Go'sht",
        "Mevalar",
        "Sabzavotlar",
    ];

    public static readonly string[] PaymentMethods =
    [
        "Naqd", "Karta", "Click", "Payme", "Qarz",
    ];

    public static string FormatMoney(decimal amount) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0:#,0}", amount).Replace(",", " ") + " so'm";

    public static string FormatNumber(decimal amount) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0:#,0}", amount).Replace(",", " ");
}
