using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PosSystem.ViewModels.Admin.Modules;

public partial class SettingsViewModel : BaseViewModel
{
    public ObservableCollection<SettingsTab> Tabs { get; }

    [ObservableProperty] private SettingsTab? _selectedTab;

    // ── General ──
    [ObservableProperty] private string _businessName   = "ShefPos Demo MChJ";
    [ObservableProperty] private string _legalAddress   = "Toshkent sh., Chilonzor t., Bunyodkor 18A";
    [ObservableProperty] private string _inn            = "302145678";
    [ObservableProperty] private string _phone          = "+998 71 200 18 18";
    [ObservableProperty] private string _email          = "info@shefpos.uz";
    [ObservableProperty] private string _language       = "O'zbek (lotin)";
    [ObservableProperty] private string _currency       = "So'm (UZS)";
    [ObservableProperty] private string _timezone       = "Asia/Tashkent (UTC+5)";

    // ── Toggles ──
    [ObservableProperty] private bool   _enableLoyalty       = true;
    [ObservableProperty] private bool   _enableMultiCurrency = false;
    [ObservableProperty] private bool   _enableOnlinePayments= true;
    [ObservableProperty] private bool   _autoPrintReceipt    = true;
    [ObservableProperty] private bool   _showLogoOnReceipt   = true;
    [ObservableProperty] private bool   _twoFactor           = true;
    [ObservableProperty] private bool   _autoLogout          = true;

    // ── Receipt header / footer (live preview) ──
    [ObservableProperty] private string _receiptHeader = "ShefPos Demo MChJ\nChilonzor filiali\nINN: 302145678";
    [ObservableProperty] private string _receiptFooter = "Bizni tanlaganingiz uchun rahmat!\nIshonch raqami: +998 71 200 18 18";

    public ObservableCollection<IntegrationRow> Integrations { get; }
    public ObservableCollection<AuditRow>       Audit         { get; }

    public SettingsViewModel()
    {
        Tabs = new ObservableCollection<SettingsTab>
        {
            new() { Key="general",      Title="Umumiy",          Glyph="" },
            new() { Key="branches",     Title="Filiallar",        Glyph="" },
            new() { Key="receipt",      Title="Chek shabloni",    Glyph="" },
            new() { Key="payments",     Title="To'lov tizimlari", Glyph="" },
            new() { Key="hardware",     Title="Qurilmalar",       Glyph="" },
            new() { Key="integrations", Title="Integratsiyalar",  Glyph="" },
            new() { Key="security",     Title="Xavfsizlik",       Glyph="" },
            new() { Key="audit",        Title="Audit jurnali",    Glyph="" },
        };
        SelectedTab = Tabs[0];

        Integrations = new ObservableCollection<IntegrationRow>
        {
            new() { Name="Click",         Description="Onlayn karta to'lovlari",        Status="connected",  Updated="14.05.2026"  },
            new() { Name="Payme",         Description="Onlayn to'lovlar va invoyslar", Status="connected",  Updated="13.05.2026"  },
            new() { Name="OFD",           Description="Soliq idorasi (fiskal modul)",  Status="connected",  Updated="14.05.2026"  },
            new() { Name="1C",            Description="Buxgalteriya bilan sinxron",    Status="disconnected",Updated="—"          },
            new() { Name="Telegram bot",  Description="Sotuv hisobotlari botga",       Status="connected",  Updated="12.05.2026"  },
            new() { Name="SMS gateway",   Description="Mijozlarga xabarnomalar",        Status="error",      Updated="11.05.2026"  },
        };

        Audit = new ObservableCollection<AuditRow>
        {
            new() { When="14.05.2026 13:42", User="N. Aliyev",     Action="Sozlamalar yangilandi", Detail="Sodiqlik tizimi yoqildi"                },
            new() { When="14.05.2026 11:14", User="A. Karimov",    Action="Kirim qabul qilindi",   Detail="KIR-00248 (Coca-Cola Bottlers)"        },
            new() { When="14.05.2026 09:42", User="M. Rashidova",  Action="Smena ochildi",         Detail="Smena #248 · boshlang'ich kassa 200 000 so'm" },
            new() { When="13.05.2026 21:00", User="S. Yo'ldoshev", Action="Inventarizatsiya",      Detail="INV-00031 yakunlandi"                  },
            new() { When="13.05.2026 18:12", User="M. Rashidova",  Action="Qaytarish",             Detail="QYT-0139 ro'yxatga olindi"             },
            new() { When="13.05.2026 14:08", User="A. Karimov",    Action="Kirim tasdiqlandi",     Detail="KIR-00247 (Anhor Fudz LLC)"            },
            new() { When="13.05.2026 10:42", User="N. Aliyev",     Action="Xodim qo'shildi",       Detail="O. Nasriddinov — Kassir (Sergeli)"     },
        };
    }
}

public partial class SettingsTab : ObservableObject
{
    public string Key   { get; init; } = "";
    public string Title { get; init; } = "";
    public string Glyph { get; init; } = "";
}

public partial class IntegrationRow : ObservableObject
{
    public string Name        { get; init; } = "";
    public string Description { get; init; } = "";
    public string Status      { get; init; } = "disconnected";
    public string Updated     { get; init; } = "—";
}

public partial class AuditRow : ObservableObject
{
    public string When   { get; init; } = "";
    public string User   { get; init; } = "";
    public string Action { get; init; } = "";
    public string Detail { get; init; } = "";
}
