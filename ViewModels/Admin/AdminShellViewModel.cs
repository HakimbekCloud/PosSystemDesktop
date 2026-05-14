using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using PosSystem.Services;
using PosSystem.ViewModels.Admin.Modules;

namespace PosSystem.ViewModels.Admin;

// Root view-model for the admin shell. Owns the navigation collection,
// the currently active module, and the lazily-resolved page view-models.
public partial class AdminShellViewModel : BaseViewModel
{
    private readonly IServiceProvider _services;
    private readonly AuthService      _auth;

    public ObservableCollection<NavigationItem> NavTopGroup    { get; }
    public ObservableCollection<NavigationItem> NavMainGroup   { get; }
    public ObservableCollection<NavigationItem> NavSystemGroup { get; }

    [ObservableProperty] private NavigationItem? _selectedItem;
    [ObservableProperty] private object?         _currentPage;
    [ObservableProperty] private string          _currentTitle    = "Boshqaruv paneli";
    [ObservableProperty] private string          _currentSubtitle = "";
    [ObservableProperty] private string          _currentCrumb    = "ShefPos / Boshqaruv";

    [ObservableProperty] private string _userName     = "Admin foydalanuvchi";
    [ObservableProperty] private string _userRole     = "Administrator";
    [ObservableProperty] private string _userInitials = "AF";
    [ObservableProperty] private string _branchName   = "Chilonzor filiali";
    [ObservableProperty] private string _shiftLabel   = "Smena #248 · 08:00 dan";
    [ObservableProperty] private string _searchQuery  = "";
    [ObservableProperty] private string _clockText    = DateTime.Now.ToString("HH:mm");

    public AdminShellViewModel(IServiceProvider services, AuthService auth)
    {
        _services = services;
        _auth     = auth;

        NavTopGroup = new ObservableCollection<NavigationItem>
        {
            new() { Module = AdminModule.Dashboard, Title = "Boshqaruv paneli", Glyph = "", Shortcut = "Ctrl+1" },
            new() { Module = AdminModule.Sales,     Title = "Sotuvlar (POS)",   Glyph = "", Shortcut = "F2"     },
        };

        NavMainGroup = new ObservableCollection<NavigationItem>
        {
            new() { Module = AdminModule.Returns,    Title = "Qaytarishlar",    Glyph = "", Shortcut = "Ctrl+3", BadgeText = "4",  BadgeTone = "warn"   },
            new() { Module = AdminModule.Products,   Title = "Mahsulotlar",     Glyph = "", Shortcut = "Ctrl+4", BadgeText = "428",BadgeTone = "neutral"},
            new() { Module = AdminModule.Inventory,  Title = "Ombor",           Glyph = "", Shortcut = "Ctrl+5", BadgeText = "12", BadgeTone = "danger" },
            new() { Module = AdminModule.Customers,  Title = "Mijozlar",        Glyph = "", Shortcut = "Ctrl+6"                                          },
            new() { Module = AdminModule.Employees,  Title = "Xodimlar",        Glyph = "", Shortcut = "Ctrl+7"                                          },
            new() { Module = AdminModule.Statistics, Title = "Statistika",      Glyph = "", Shortcut = "Ctrl+8"                                          },
        };

        NavSystemGroup = new ObservableCollection<NavigationItem>
        {
            new() { Module = AdminModule.Settings, Title = "Sozlamalar", Glyph = "", Shortcut = "Ctrl+,"  },
        };

        var ticker = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        ticker.Tick += (_, _) => ClockText = DateTime.Now.ToString("HH:mm");
        ticker.Start();

        // Land on the dashboard by default.
        SelectedItem = NavTopGroup[0];
    }

    partial void OnSelectedItemChanged(NavigationItem? value)
    {
        if (value is null) return;
        NavigateTo(value.Module, value.Title);
    }

    public void NavigateTo(AdminModule module, string title)
    {
        CurrentPage    = ResolvePage(module);
        CurrentTitle   = title;
        CurrentCrumb   = $"ShefPos / {title}";
        CurrentSubtitle = SubtitleFor(module);
    }

    private object ResolvePage(AdminModule module) => module switch
    {
        AdminModule.Dashboard  => _services.GetRequiredService<DashboardViewModel>(),
        AdminModule.Sales      => _services.GetRequiredService<SalesEntryViewModel>(),
        AdminModule.Returns    => _services.GetRequiredService<ReturnsViewModel>(),
        AdminModule.Products   => _services.GetRequiredService<ProductsAdminViewModel>(),
        AdminModule.Inventory  => _services.GetRequiredService<InventoryViewModel>(),
        AdminModule.Customers  => _services.GetRequiredService<CustomersViewModel>(),
        AdminModule.Employees  => _services.GetRequiredService<EmployeesViewModel>(),
        AdminModule.Statistics => _services.GetRequiredService<StatisticsViewModel>(),
        AdminModule.Settings   => _services.GetRequiredService<SettingsViewModel>(),
        _ => new object(),
    };

    private static string SubtitleFor(AdminModule module) => module switch
    {
        AdminModule.Dashboard  => $"{DateTime.Now:dd MMMM yyyy, dddd} · joriy smena 08:00 dan boshlangan",
        AdminModule.Sales      => "Yangi chek yarating yoki ochiq cheklarni davom ettiring",
        AdminModule.Returns    => "Mijoz qaytargan chek va tovarlarni boshqarish",
        AdminModule.Products   => "Katalog, narxlar va kategoriyalar boshqaruvi",
        AdminModule.Inventory  => "Joriy qoldiq, harakatlar va inventarizatsiya",
        AdminModule.Customers  => "Mijozlar bazasi va sodiqlik darajalari",
        AdminModule.Employees  => "Kassirlar, menejerlar va ruxsatlar",
        AdminModule.Statistics => "Savdo dinamikasi, foyda va hisobotlar",
        AdminModule.Settings   => "Tizim, qurilmalar, integratsiyalar va xavfsizlik",
        _ => "",
    };

    [RelayCommand]
    private void Logout()
    {
        _auth.Logout();
        WeakReferenceMessenger.Default.Send(new LogoutMessage());
    }
}
