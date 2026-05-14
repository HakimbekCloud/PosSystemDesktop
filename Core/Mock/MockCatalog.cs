using System.Collections.Generic;

namespace PosSystem.Core.Mock;

// Shared static demo data. Real backend integration replaces this layer.
// Kept deliberately verbose (realistic numbers, real-looking names)
// so the UI feels production-grade even in screenshot reviews.
public static class MockCatalog
{
    public static readonly string[] Branches =
    [
        "Chilonzor filiali",
        "Mirobod filiali",
        "Yunusobod filiali",
        "Sergeli filiali",
    ];

    public static readonly string[] Cashiers =
    [
        "M. Rashidova",
        "B. Nazarov",
        "A. Karimov",
        "D. Tursunova",
        "S. Yo'ldoshev",
        "G. Saidova",
    ];

    public static readonly string[] Roles =
    [
        "Administrator",
        "Menejer",
        "Kassir",
        "Omborchi",
        "Yetkazib beruvchi",
    ];

    public static readonly string[] PaymentMethods =
    [
        "Naqd", "Karta", "Click", "Payme", "Aralash",
    ];

    public static readonly string[] Categories =
    [
        "Barchasi",
        "Ichimliklar",
        "Non mahsulotlari",
        "Sut mahsulotlari",
        "Don mahsulotlari",
        "Sabzavotlar",
        "Mevalar",
        "Tuxum",
        "Moy",
        "Shirinliklar",
    ];

    public static string FormatMoney(decimal amount)
        => string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0:#,0}", amount).Replace(",", " ") + " so'm";

    public static string FormatNumber(decimal amount)
        => string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0:#,0}", amount).Replace(",", " ");
}
